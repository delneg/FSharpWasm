# Performance compared with Mono AOT and desktop .NET

NetWasm runs our allocation and garbage-collection benchmarks faster than
Mono AOT, compiled from the same C# source and run by the same Wasmtime. With
NetWasmGC it also takes less time than desktop .NET 11's JIT on all three
workloads, and less than desktop NativeAOT on the retained object graph.
Without allocation, NetWasm takes less time than Mono AOT or matches it on every
workload.

## Results

Median time of 15 runs in milliseconds, with the fastest and slowest run:

| Workload | NetWasm, NetWasmGC | NetWasm, Compact | Mono AOT | Desktop .NET 11 JIT | Desktop .NET 11 NativeAOT |
| --- | ---: | ---: | ---: | ---: | ---: |
| Full transaction | **34.8** (34.4–36.1) | 49.3 (48.7–50.1) | 59.1 (58.2–60.9) | 49.9 (49.3–50.7) | 32.6 (32.2–33.2) |
| Retained object graph | **4.7** (4.6–4.8) | 12.4 (12.3–12.8) | 14.2 (14.1–14.7) | 9.7 (9.7–9.8) | 7.5 (7.4–7.7) |
| UI churn | **7.8** (7.7–8.1) | 20.9 (20.8–21.9) | 29.1 (28.6–30.3) | 8.6 (8.5–8.7) | 6.6 (6.6–6.7) |

NetWasm with NetWasmGC takes 41%, 67% and 73% less time than Mono AOT on the
three workloads and 30%, 52% and 10% less than the desktop JIT. On the object
graph it also takes 38% less than desktop NativeAOT. Compact, the size-first
default, takes less time than Mono AOT on all three workloads.

| Wasm build | Program size (Brotli 11) | Peak linear memory: transaction, graph, churn |
| --- | ---: | ---: |
| NetWasm, NetWasmGC | 114,741–230,775 B (23,622–46,180 B) | 20.9, 15.7 and 7.3 MiB |
| NetWasm, Compact | 110,205–226,083 B (21,914–44,393 B) | 17.4, 11.9 and 1.7 MiB |
| Mono AOT | 8,185,047–8,256,888 B (1,572,917–1,593,051 B) | 50, 50 and 50 MiB |

Mono reserves 50 MiB of linear memory when it starts, so its peak on every
workload is that reservation rather than its live data. Desktop .NET runs
native code, so it has no linear memory or Wasm program size.

## Without allocation

The three workloads above allocate heavily, so they largely measure memory
management. Four more take allocation out of the timed region: each program
fails if that region allocates a byte or triggers a collection.

- **Preallocated transaction** (`transaction`, `preallocated 100000 101`) runs
  the full transaction's aggregation over containers created and warmed before
  timing: library work without allocation.
- **n-body** (`nbody`, 1,000,000 steps) is the Computer Language Benchmarks
  Game simulation of the Sun and the four gas giants in double precision.
- **Quicksort** (`sort`, 10 rounds) sorts 100,000 integers in place, refilled
  from seed data every round.
- **CRC-32** (`crc32`, 16 passes) computes a table-driven CRC-32 over 1 MiB.

The last three use no library code. Median time of 15 runs in milliseconds,
with the fastest and slowest run:

| Workload | NetWasm, NetWasmGC | NetWasm, Compact | Mono AOT | Desktop .NET 11 JIT | Desktop .NET 11 NativeAOT |
| --- | ---: | ---: | ---: | ---: | ---: |
| Preallocated transaction | **18.8** (18.6–20.2) | 19.1 (18.6–21.0) | 23.5 (23.3–24.6) | 11.3 (11.2–11.6) | 7.7 (7.7–7.8) |
| n-body | **52.9** (52.6–53.9) | 53.0 (52.6–53.4) | 53.3 (53.2–54.6) | 64.5 (64.3–64.9) | 63.0 (63.0–64.2) |
| Quicksort | **54.0** (53.8–54.8) | 53.9 (53.7–55.1) | 58.0 (57.5–58.6) | 48.7 (48.6–49.3) | 48.5 (48.2–49.9) |
| CRC-32 | **47.4** (47.3–47.6) | 47.4 (47.3–47.6) | 47.4 (47.3–48.5) | 47.1 (46.9–48.4) | 47.1 (47.0–48.6) |

Without allocation, NetWasm takes 20% less time than Mono AOT on the
transaction's library work and 7% less on the quicksort, and matches it on
n-body and CRC-32. Desktop .NET runs the preallocated transaction
and the quicksort faster than either Wasm build, and every build is within 1.5%
on CRC-32. So much of NetWasm's lead over desktop .NET on the allocating
workloads comes from allocation and collection.

On n-body, desktop .NET 11 RC2 is slower than both Wasm builds. Desktop .NET 10's
JIT runs the same program in about 53.5 ms on this machine, measured separately,
so that difference reflects .NET 11 RC2 rather than Wasm outrunning native code.

## What was measured

The workloads are in `compiler-qualification/runtime-performance/workloads/`:

- **Full transaction** (`full 100000 101`) creates 100,000 transactions,
  normalizes account strings, deduplicates through `HashSet<int>` and sums
  `decimal` balances in a `Dictionary<string, decimal>`. Its containers grow
  from empty, so allocation and collection are part of the time.
- **Retained object graph** (`object-graph 100000`) builds a graph of 100,000
  small objects that stays alive.
- **UI churn** (`ui-churn 500`) keeps an array alive and replaces a slice of it
  every frame for 500 frames, allocating many short-lived objects.

Each program times its own work with `Stopwatch`, so process start, Wasm
compilation and runtime start-up are not included. Every run is a fresh
process. After 3 warmup runs, 15 runs of each build are interleaved, rotating
their order every round, and the median is reported. Every run's checksum must
equal the result desktop .NET 11 computes. Peak linear memory is measured by a
small Wasmtime host that follows every memory's growth.

## Versions and settings

- **NetWasm:** 0.10.2, `NetWasmOptimization=O3`, with `NetWasmGarbageCollector`
  set to `NetWasm` or `Compact`. The figures are the lower median of two
  15-run cohorts.
- **Mono AOT:** .NET SDK 10.0.401 with the `wasi-experimental` workload (Mono
  runtime pack 10.0.12) and wasi-sdk 25.0, building for `wasi-wasm` in Release
  with `RunAOTCompilation`, `WasmSingleFileBundle` and `InvariantGlobalization`.
  In this default configuration Mono compiles the program and CoreLib ahead of
  time, keeps its interpreter for methods it cannot compile, and leaves Wasm
  SIMD off.
- **Desktop .NET:** .NET 11 RC2 (SDK 11.0.100-rc.2.26504.105), with the default
  tiered JIT and with NativeAOT.
- **Comparison figures:** the Mono AOT and desktop .NET figures come from
  earlier runs on the same host and Wasmtime. Nothing on those sides has
  changed since.
- **Engine:** Wasmtime 49.0.3 for every Wasm build, compiling each module on one
  thread (`-C parallel-compilation=n`).
- **Host:** Apple M1 Max, macOS 26.6.2, otherwise idle.

## Reproduce

The harness needs Python 3, the .NET SDKs pinned in `eng/csharp15-toolchain.json`,
Wasmtime 49.0.3 on `PATH`, Node.js for the Brotli sizes, and Rust to build the
memory host. For the Mono row, install a .NET 10 SDK with the
`wasi-experimental` workload and wasi-sdk 25.0. An empty `--feed` directory
restores the released NetWasm packages from NuGet.org:

```sh
python3 compiler-qualification/runtime-performance/collectors.py \
  --feed <empty-directory> 0.10.2 --output <new-directory> \
  --collectors NetWasm Compact \
  --mono-aot <dotnet-root> <wasi-sdk-root> \
  --desktop jit aot
```

For the workloads without allocation, add
`--scenarios transaction nbody sort crc32`. It prints the table and writes
every run's time to `summary.json`.
`compiler-qualification/runtime-performance/README.md` describes the options.
See [Choosing a garbage collector](runtime-garbage-collection.md) for the
collector comparison on its own.
