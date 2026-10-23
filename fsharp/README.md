# F# on NetWasm

This folder compiles F# programs with NetWasm (`NetWasm.Sdk` 0.10.2) and ports
the upstream runtime-performance workloads to F#, so F# can be measured against
the original C# and, for the compute kernels, Rust.

## Is "beats Rust" real?

Only for size. The claim in the upstream README is about one program,
`Console.WriteLine(42)`. Its NetWasm component is 43,109 B (13,477 B with
Brotli). The Rust 1.95.0 size-profile build is 53,635 B (16,963 B). We rebuilt
both and got exactly those bytes.

Upstream's performance tables compare NetWasm only with Mono AOT and desktop
.NET, never with Rust. The table below adds Rust for the three compute kernels.
Under Wasmtime, Rust is 0–6% faster than C# on NetWasm, and F# lands between
the two.

## How it works

NetWasm's SDK compiles C# against its own `NetWasm.CoreLib` and rejects
packages that are not built for `netwasm0.1`, FSharp.Core included.
`build/FSharp.NetWasm.targets` is imported for every `.fsproj` here, and does
three things:

1. fsc compiles against the net10.0 reference pack and the ordinary
   FSharp.Core 10.1.401. Both are fetched with `PackageDownload`.
2. `tools/Retarget`, a Mono.Cecil tool, then rewrites the program and a copy of
   FSharp.Core so every framework reference points at `NetWasm.CoreLib`. On the
   way it makes these changes:
   - FSharp.Core's resource-string lookups, which use `CultureInfo.CurrentUICulture`
     (NetWasm has no such API), become the bare resource key.
   - FSharp.Core's `String.Format` calls on error paths go to a small helper.
     `String.Format` alone pulls about 220 KB of formatting code into the Wasm,
     and every `Array.zeroCreate` reaches one. With
     `-p:FsWasmErrorMessageArguments=true`, the messages keep their arguments,
     for about 16 KB more.
   - In the program, `x.ToString()` on a primitive becomes the direct call that
     C# emits. fsc's `constrained.` form ran about 7% slower on ui-churn. Set
     `FSWASM_PEEPHOLE=0` to turn this off.
3. NetWasm compiles the result as usual. It is a closed-world compiler, so only
   the reachable part of FSharp.Core ends up in the Wasm.

The ports (`workloads/{compute,gc,transaction}/Program.fs`) produce the same
output lines, checksums and return codes as upstream's
`compiler-qualification/runtime-performance/workloads`. They use F# idioms
where those cost nothing: modules, `let` functions, byrefs, inline helpers.
They use explicit structs and classes rather than records or unions, whose
generated `ToString` is printf-based and pulls FSharp.Core's formatting code
into the Wasm. They don't use printf either.

## Build and run

You need the .NET 10 SDK (10.0.3xx or later), Wasmtime 49, uv, and, for the
Rust baseline, rustup with the `wasm32-wasip2` target.

```sh
cd fsharp
dotnet publish samples/Hello -c Release -o out/hello
wasmtime run out/hello/NetWasmApp.wasm               # 42

dotnet publish workloads/compute -c Release -p:NetWasmOptimization=O3 -o out/compute
wasmtime run out/compute/ComputeKernels.wasm nbody 1000000
```

## Benchmark

```sh
uv run fsharp/bench/bench.py                         # 3 warmups + 15 samples, about 5 min on opt-box
uv run fsharp/bench/bench.py --runs 5 --scenarios ui-churn --langs cs fs
```

The script publishes the C# workloads (upstream's `Program.cs`, linked from
`bench/csharp`), the F# ports and the Rust crates (`bench/rust`). It then runs
every scenario under Wasmtime, rotating the language order each sample, and
fails if any checksum differs from C#'s.

It prints the table and sizes below, plus one line `METRIC fs_vs_cs=<x>`: the
geometric mean of the F#/C# median ratios over all scenarios. Lower is better,
and 1.0 means parity. It also writes `fsharp/results/latest.md` and a JSON file
with every sample.

### Results

opt-box: Intel i5-11400F VM, 4 vCPUs, Ubuntu (Linux 7.0), Wasmtime 49.0.3,
NetWasm 0.10.2, Rust 1.99.1. Builds are NetWasm O3 with the NetWasm
(precise) GC, and Rust `opt-level=3`, LTO, safe code. Each cell is the median
of 15 samples, with min–max in brackets, after 3 warmups.

| scenario | C# ms | F# ms | Rust ms | F#/C# |
|---|---|---|---|---|
| transaction | 24.05 (23.99–24.14) | 24.58 (24.52–25.71) | – | 1.022 |
| transaction-full | 45.62 (45.27–47.43) | 44.64 (44.36–47.14) | – | 0.978 |
| object-graph | 7.12 (7.00–7.34) | 7.14 (7.05–7.28) | – | 1.003 |
| ui-churn | 10.77 (10.63–10.98) | 11.26 (11.04–12.53) | – | 1.045 |
| nbody | 55.26 (55.15–59.25) | 52.89 (52.85–59.31) | 51.97 (51.90–53.29) | 0.957 |
| sort | 56.33 (56.21–56.44) | 56.76 (56.65–56.94) | 56.06 (55.84–56.35) | 1.008 |
| crc32 | 34.65 (34.63–34.74) | 34.65 (34.62–34.80) | 34.89 (34.85–35.16) | 1.000 |

`METRIC fs_vs_cs=1.0015`

The noise is small: two full runs a few minutes apart gave C# medians within
1% of each other (transaction 24.10 and 24.05, nbody 55.19 and 55.26).

| artifact | C# bytes (Brotli) | F# bytes (Brotli) | Rust bytes (Brotli) |
|---|---:|---:|---:|
| print-42, Oz, Compact GC | 43,109 (13,477) | 38,939 (12,477) | 52,569 (16,648); 1.95.0: 53,635 |
| compute, O3 | 119,403 (24,939) | 111,899 (24,217) | 103,658 (32,825) |
| gc, O3 | 114,741 (23,622) | 109,053 (23,159) | – |
| transaction, O3 | 230,775 (46,180) | 212,953 (44,762) | – |

The F# print-42 is smaller only because its entry point takes no `args`. A C#
`static void Main()` with the same body is also exactly 38,939 B.

## Open ends

- ui-churn is still 4.5% slower in F#, and transaction 2%. Allocation counts
  match C#, so the difference is codegen.
- Only the compute kernels have Rust ports. The gc and transaction workloads
  rely on a GC and on `decimal`, so a Rust port would be a different program.
- Only what these workloads use has been tried. `printf`, `Async`, records,
  unions and collections such as `List` and `Map` are untested. If one of them
  needs an API that NetWasm's CoreLib lacks, the NetWasm compile step fails
  with NetWasm's own diagnostic.
