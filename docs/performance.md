# Performance compared with Mono AOT and desktop .NET

NetWasm runs our allocation and garbage-collection benchmarks faster than
Mono AOT, compiled from the same C# source and run by the same Wasmtime. With
NetWasmGC it also takes less time than desktop .NET 11's JIT on all three
workloads, and less than desktop NativeAOT on the retained object graph.

## Results

Median time of 15 runs in milliseconds, with the fastest and slowest run:

| Workload | NetWasm, NetWasmGC | NetWasm, Compact | Mono AOT | Desktop .NET 11 JIT | Desktop .NET 11 NativeAOT |
| --- | ---: | ---: | ---: | ---: | ---: |
| Full transaction | **34.9** (34.5–35.6) | 49.2 (48.7–51.2) | 59.1 (58.2–60.9) | 49.9 (49.3–50.7) | 32.6 (32.2–33.2) |
| Retained object graph | **4.6** (4.6–4.8) | 12.5 (12.3–12.7) | 14.2 (14.1–14.7) | 9.7 (9.7–9.8) | 7.5 (7.4–7.7) |
| UI churn | **7.9** (7.7–8.0) | 20.9 (20.8–21.2) | 29.1 (28.6–30.3) | 8.6 (8.5–8.7) | 6.6 (6.6–6.7) |

NetWasm with NetWasmGC takes 41%, 68% and 73% less time than Mono AOT on the
three workloads and 30%, 53% and 8% less than the desktop JIT. On the object
graph it also takes 39% less than desktop NativeAOT. Compact, the size-first
default, takes less time than Mono AOT on all three workloads.

| Wasm build | Program size (Brotli 11) | Peak linear memory: transaction, graph, churn |
| --- | ---: | ---: |
| NetWasm, NetWasmGC | 114,897–231,130 B (23,770–46,200 B) | 20.9, 15.7 and 7.3 MiB |
| NetWasm, Compact | 110,361–226,438 B (22,030–44,696 B) | 17.4, 11.9 and 1.7 MiB |
| Mono AOT | 8,185,047–8,256,888 B (1,572,917–1,593,051 B) | 50, 50 and 50 MiB |

Mono reserves 50 MiB of linear memory when it starts, so its peak on every
workload is that reservation rather than its live data. Desktop .NET runs
native code, so it has no linear memory or Wasm program size.

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

- **NetWasm:** 0.10.1, `NetWasmOptimization=O3`, with `NetWasmGarbageCollector`
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
- **Comparison figures:** the Mono AOT and desktop .NET figures come from an
  earlier run on the same host and Wasmtime. Nothing on those sides has changed
  since.
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
  --feed <empty-directory> 0.10.1 --output <new-directory> \
  --collectors NetWasm Compact \
  --mono-aot <dotnet-root> <wasi-sdk-root> \
  --desktop jit aot
```

It prints the table and writes every run's time to `summary.json`.
`compiler-qualification/runtime-performance/README.md` describes the options.
See [Choosing a garbage collector](runtime-garbage-collection.md) for the
collector comparison on its own.
