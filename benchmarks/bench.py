# /// script
# requires-python = ">=3.10"
# dependencies = ["brotli"]
# ///
"""Benchmark the F# ports against the upstream C# workloads (and Rust, where a
port exists) on NetWasm under Wasmtime.

    eng/build-local-feed.sh                      # this checkout's NetWasm, into artifacts/feed
    uv run benchmarks/bench.py                   # build, then 3 warmups + 15 samples
    uv run benchmarks/bench.py --runs 5 --scenarios nbody sort

C# and F# build with the same NetWasm: the packages of artifacts/feed
(benchmarks/NuGet.Config, benchmarks/global.json).

Every sample runs each language once per scenario, in an order that rotates
with the sample, so drift on the machine spreads across languages. A sample
whose checksum differs from C#'s fails the run. Prints a Markdown table
(median, min, max ms), the artifact sizes, and one summary line:

    METRIC fs_vs_cs=<geometric mean over scenarios of F# median / C# median>

Lower is better; 1.0 means F# matches C#. Results also go to
benchmarks/results/<stamp>.json and benchmarks/results/latest.md.
"""
import argparse
import json
import math
import os
import platform
import shutil
import statistics
import subprocess
import sys
import time
from pathlib import Path

import brotli

BENCH = Path(__file__).resolve().parent
OUT = BENCH / 'out'
RESULTS = BENCH / 'results'

# The upstream run.py scenarios that these workloads cover.
SCENARIOS = {
    'transaction': ('transaction', ['preallocated', '100000', '101']),
    'transaction-full': ('transaction', ['full', '100000', '101']),
    'object-graph': ('gc', ['object-graph', '100000']),
    'ui-churn': ('gc', ['ui-churn', '500']),
    'nbody': ('compute', ['nbody', '1000000']),
    'sort': ('compute', ['sort', '10']),
    'crc32': ('compute', ['crc32', '16']),
}
NAMES = {'transaction': 'Transaction', 'gc': 'GcWorkloads', 'compute': 'ComputeKernels', 'hello': 'NetWasmApp'}
LANGS = ['cs', 'fs', 'rust']
RUST_WORKLOADS = {'compute'}


def project(lang, workload):
    if lang == 'cs':
        name = 'Hello' if workload == 'hello' else NAMES[workload]
        return BENCH / 'csharp' / workload / f'{name}.csproj'
    name = 'Hello' if workload == 'hello' else NAMES[workload]
    return BENCH / 'fsharp' / workload / f'{name}.fsproj'


def run(command, **kwargs):
    completed = subprocess.run(command, capture_output=True, text=True, **kwargs)
    if completed.returncode != 0:
        sys.exit(f'{" ".join(map(str, command))} exited {completed.returncode}:\n'
                 f'{completed.stdout[-2000:]}{completed.stderr[-2000:]}')
    return completed.stdout


def publish(lang, workload, optimization, collector, build):
    """Publishes one .NET workload; returns its component."""
    out = OUT / f'{lang}-{workload}-{optimization.lower()}-{collector.lower()}'
    if build:
        shutil.rmtree(out, ignore_errors=True)
        run(['dotnet', 'publish', str(project(lang, workload)), '-c', 'Release',
             f'-p:NetWasmOptimization={optimization}', f'-p:NetWasmGarbageCollector={collector}',
             '-o', str(out), '--nologo', '-v:q'])
    return out / f'{NAMES[workload]}.wasm'


def cargo(crate, profile, build):
    """Builds one Rust crate for wasm32-wasip2; returns its component."""
    manifest = BENCH / 'rust' / crate / 'Cargo.toml'
    target = BENCH / 'target'
    if build:
        run(['cargo', 'build', '--profile', profile, '--target', 'wasm32-wasip2',
             '--manifest-path', str(manifest), '--target-dir', str(target)])
    return target / 'wasm32-wasip2' / profile / f'{crate}.wasm'


def sizes(path):
    data = path.read_bytes()
    return len(data), len(brotli.compress(data, quality=11, lgwin=22))


def parse(text):
    """Returns (milliseconds, checksum) from a workload's result line."""
    for line in text.splitlines():
        fields = line.split('|')
        if fields[0] in ('PROBE', 'GCWORK', 'COMPUTE'):
            return int(fields[2]) * 1000 / int(fields[3]), fields[4]
    raise ValueError(f'no result line in: {text[:200]}')


def execute(wasmtime, component, arguments):
    return parse(run([wasmtime, 'run', str(component), *arguments]))


def machine():
    info = {'os': platform.platform(), 'arch': platform.machine(), 'cpus': os.cpu_count()}
    try:
        for line in Path('/proc/cpuinfo').read_text().splitlines():
            if line.startswith('model name'):
                info['cpu'] = line.split(':', 1)[1].strip()
                break
    except OSError:
        info['cpu'] = run(['sysctl', '-n', 'machdep.cpu.brand_string']).strip() if sys.platform == 'darwin' else '?'
    return info


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument('--scenarios', nargs='+', choices=SCENARIOS, default=list(SCENARIOS))
    parser.add_argument('--langs', nargs='+', choices=LANGS, default=LANGS)
    parser.add_argument('--runs', type=int, default=15)
    parser.add_argument('--warmups', type=int, default=3)
    parser.add_argument('--optimization', default='O3', choices=['O3', 'Oz'])
    parser.add_argument('--collector', default='NetWasm', choices=['Compact', 'NetWasm', 'Bump'])
    parser.add_argument('--no-build', action='store_true', help='reuse benchmarks/out and benchmarks/target')
    parser.add_argument('--no-sizes', action='store_true', help='skip the print-42 size builds')
    parser.add_argument('--wasmtime', default=os.environ.get('WASMTIME') or shutil.which('wasmtime'))
    args = parser.parse_args()
    if not args.wasmtime:
        sys.exit('wasmtime not found: put it on PATH or set WASMTIME')
    build = not args.no_build
    if build and not (BENCH.parent / 'artifacts' / 'feed').is_dir():
        sys.exit('artifacts/feed not found: run eng/build-local-feed.sh first')
    langs = [lang for lang in LANGS if lang in args.langs]
    if 'rust' in langs and build and not shutil.which('cargo'):
        sys.exit('cargo not found: install Rust or pass --langs cs fs')
    workloads = sorted({SCENARIOS[s][0] for s in args.scenarios})

    components = {}
    for workload in workloads:
        for lang in langs:
            if lang == 'rust':
                if workload in RUST_WORKLOADS:
                    components[lang, workload] = cargo(workload, 'release', build)
            else:
                components[lang, workload] = publish(lang, workload, args.optimization, args.collector, build)
            if (lang, workload) in components:
                print(f'built {lang} {workload}', file=sys.stderr)

    size_rows = []
    if not args.no_sizes:
        # The upstream size comparison: print-42, Release/Oz, default (Compact) collector.
        for lang in langs:
            path = cargo('console42', 'small', build) if lang == 'rust' else publish(lang, 'hello', 'Oz', 'Compact', build)
            size_rows.append(('print-42 (Oz, Compact GC)', lang, *sizes(path)))
    for (lang, workload), path in sorted(components.items()):
        size_rows.append((f'{workload} ({args.optimization}, {args.collector} GC)', lang, *sizes(path)))

    plan = [(s, lang) for s in args.scenarios for lang in langs if (lang, SCENARIOS[s][0]) in components]
    samples = {key: [] for key in plan}
    checksums = {}
    total = args.warmups + args.runs
    for k in range(total):
        for scenario in args.scenarios:
            entries = [lang for s, lang in plan if s == scenario]
            shift = k % len(entries)
            for lang in entries[shift:] + entries[:shift]:
                workload, arguments = SCENARIOS[scenario]
                ms, checksum = execute(args.wasmtime, components[lang, workload], arguments)
                expected = checksums.setdefault(scenario, checksum)
                if checksum != expected:
                    sys.exit(f'{scenario}: {lang} checksum {checksum} differs from {expected}')
                if k >= args.warmups:
                    samples[scenario, lang].append(ms)
        print(f'sample {k + 1}/{total}{" (warmup)" if k < args.warmups else ""}', file=sys.stderr)

    def stats(values):
        return {'median': statistics.median(values), 'min': min(values), 'max': max(values),
                'stdev': statistics.stdev(values) if len(values) > 1 else 0.0, 'samples': values}

    table = {f'{s}/{lang}': stats(v) for (s, lang), v in samples.items()}
    ratios = [table[f'{s}/fs']['median'] / table[f'{s}/cs']['median']
              for s in args.scenarios if f'{s}/fs' in table and f'{s}/cs' in table]
    metric = math.exp(sum(map(math.log, ratios)) / len(ratios)) if ratios else float('nan')

    head = ['scenario'] + [f'{lang} ms' for lang in langs] + (['F#/C#'] if 'fs' in langs and 'cs' in langs else [])
    lines = [f'| {" | ".join(head)} |', '|' + '---|' * len(head)]
    for s in args.scenarios:
        row = [s]
        for lang in langs:
            cell = table.get(f'{s}/{lang}')
            row.append(f'{cell["median"]:.2f} ({cell["min"]:.2f}-{cell["max"]:.2f})' if cell else '-')
        if len(head) > len(langs) + 1:
            row.append(f'{table[f"{s}/fs"]["median"] / table[f"{s}/cs"]["median"]:.3f}')
        lines.append(f'| {" | ".join(row)} |')
    lines += ['', '| artifact | lang | raw bytes | Brotli-11 bytes |', '|---|---|---:|---:|']
    lines += [f'| {name} | {lang} | {raw:,} | {br:,} |' for name, lang, raw, br in size_rows]
    wasmtime_version = run([args.wasmtime, '--version']).strip()
    info = machine()
    lines += ['', f'Median of {args.runs} samples (min-max) after {args.warmups} warmups; '
              f'NetWasm {args.optimization}, {args.collector} GC; {wasmtime_version}; '
              f'{info.get("cpu", "?")}, {info["cpus"]} CPUs, {info["os"]}.']
    report = '\n'.join(lines)
    print(report)
    print(f'METRIC fs_vs_cs={metric:.4f}')

    RESULTS.mkdir(exist_ok=True)
    stamp = time.strftime('%Y%m%d-%H%M%S')
    (RESULTS / f'{stamp}.json').write_text(json.dumps({
        'machine': info, 'wasmtime': wasmtime_version, 'optimization': args.optimization,
        'collector': args.collector, 'runs': args.runs, 'warmups': args.warmups,
        'results': table, 'sizes': size_rows, 'checksums': checksums, 'fs_vs_cs': metric}, indent=1))
    (RESULTS / 'latest.md').write_text(report + f'\n\nMETRIC fs_vs_cs={metric:.4f}\n')


if __name__ == '__main__':
    main()
