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
