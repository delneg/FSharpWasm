# Choosing a garbage collector

**Compact is the default garbage collector**, starting with NetWasm 0.7.0.
You do not need to configure a collector to build an application.

## Collector choices

Compact is the size-first, precise, non-moving mark-and-sweep collector.
It traces compiler-published roots and reference layouts without conservative
scanning. Choose it when small, self-contained output matters most. The name
describes implementation size, not heap compaction: live objects do not move.

NetWasmGC is the performance-oriented, precise, non-moving collector, selected
with the value `NetWasm`. It trades additional implementation size and memory
for faster allocation and collection paths. It is non-generational and collects
synchronously. It uses the same compiler-published roots and managed lifetime
contracts as Compact. Benchmark your application rather than assuming one
collector is universally faster.

Boehm is the alternative BDWGC backend. NetWasm supplies exact roots and layouts
to it, but Boehm retains conservative pressure paths. Its allocation and
collection tradeoffs differ from Compact and NetWasmGC.

Bump is a production non-collecting allocator for workloads with bounded
lifetimes and memory consumption, such as one-shot tools and plugins whose
entire Wasm instance is discarded after a job. Allocation advances through
lazily acquired chunks; managed objects are never reclaimed. `GC.Collect()`
does not recover their memory. Projects requiring finalization, weak handles
or ephemeron handles are rejected at build time. Internal runtime bookkeeping
does not expand this supported public lifetime surface.

The tracing collectors support NetWasm's managed lifetime profile, including
finalization, resurrection, weak references, dependent handles and pinning.
Collector selection does not enable threading, reflection or other APIs outside
the supported NetWasm profile.

## Measured performance

The NetWasm 0.8 qualification campaign measured complete managed workloads, not
isolated allocator microbenchmarks. The figures below are the lower median from
two accepted cohorts on an Apple M1 Max under Wasmtime 49.0.3. Each cohort used
three warmups followed by seven retained fresh-process samples. Every NetWasm
cell used the same managed program and `O3` compiler profile; lower is better.

| Collector | Full transaction | Retained object graph | UI-style object churn |
| --- | ---: | ---: | ---: |
| Bump | 35.98 ms | 12.97 ms | 12.80 ms |
| NetWasmGC | 39.34 ms | 16.65 ms | 13.27 ms |
| Boehm | 41.59 ms | 18.74 ms | 16.29 ms |
| Compact | 49.83 ms | 30.60 ms | 22.11 ms |

Using those reported medians, NetWasmGC was 5.4% faster than Boehm on the full
transaction workload, 11.1% faster on the retained object graph and 18.5%
faster on UI churn. Bump is useful as a performance ceiling, but the
comparison is not equivalent lifetime behaviour: it never collects or reclaims
managed objects.

The same full-transaction artifacts reached 36 MiB of aggregate Wasm linear
memory capacity with NetWasmGC, 29.625 MiB with Compact, 20.375 MiB with Bump
and 13.1875 MiB with Boehm. This is whole-invocation linear-memory capacity,
not live managed bytes, host RSS or a promised ceiling. It makes the tradeoffs
visible: NetWasmGC's measured throughput gain over Boehm used more linear memory,
while Compact traded throughput for the smallest tracing-collector artifact.

The corresponding complete Console `42` component sizes are 43,794 bytes for
Bump, 47,798 for Compact, 51,300 for NetWasmGC and 74,142 for Boehm. See
[Size and methodology](size-and-methodology.md#collector-selection-and-compression)
for Brotli-11 results and the exact size boundary. Timing and file size measure
different things; neither table is a universal collector ranking.

## Selecting a collector

Set the property on the executable application, worker or test project:

```xml
<PropertyGroup>
  <NetWasmGarbageCollector>NetWasm</NetWasmGarbageCollector>
</PropertyGroup>
```

Or select it for a build/run:

```sh
dotnet run -c Release -p:NetWasmGarbageCollector=NetWasm
```

The canonical, case-sensitive values are `Compact`, `NetWasm`, `Boehm` and
`Bump`. Leaving the property unset selects Compact. All four are packaged for
wasm32 and wasm64; only the selected backend is linked into an application.
Class libraries remain collector-neutral.

Selection is a build-time choice, not a runtime switch or a `global.json`
setting. Rebuild when changing it. A caller-supplied runtime-file override is
not supported. To test a locally built runtime, use matching packages from a
local NuGet feed.

## Collection policy

Compact and NetWasmGC collect synchronously at allocation boundaries, using
adaptive allocation-pressure budgets. Compact's minimum budget is 1 MiB;
NetWasmGC's is 4 MiB. These are pacing floors, not permanently reserved heap
sizes, exact memory-use limits or promises of pause-free allocation. Budgets
adapt to retained memory after collection. Most applications should leave
collection to the runtime rather than repeatedly calling `GC.Collect()`.

Bump starts with a 256 KiB allocation chunk, acquired lazily. Subsequent normal
chunks grow geometrically up to 4 MiB; individual large requests can require
larger backing allocations. Memory grows until the instance is discarded or
the Wasm memory limit is reached. This makes Bump useful as both a bounded-
lifetime production option and a no-collection diagnostic control.

## Prebuilt runtimes and application builds

Runtime packs contain prebuilt Release native archives. Application builds
select and link them, including link-time optimization; they do not recompile
the collector's C source whenever application code changes. Both Debug and
Release C# builds use these prebuilt archives.

`O1`, `O2` and `O3` select speed-oriented native archives. `None`, `O0`, `Os`
and `Oz` select the size-oriented archives. Collector choice and optimization
mode are separate settings.

## Boehm diagnostic-enabled native runtimes

`GC_DONT_GC` is a maintainer diagnostic for a Boehm native runtime compiled with
`NETWASM_GC_DIAGNOSTICS`. The native runtime build script enables this only in
its debug configuration. Published runtime packs do not enable it:
**`dotnet build -c Debug` neither enables the hook nor rebuilds the collector.**

With that diagnostic-enabled runtime, providing `GC_DONT_GC` through the module's
WASI Preview 2 environment before initialization disables collection while
retaining the allocator. Presence alone enables it, including an empty value
or `0`. It cannot be switched after initialization, and explicit collection
requests do not override it. Native Release builds preprocess the hook out.
It does not apply to Compact, NetWasmGC or Bump.
