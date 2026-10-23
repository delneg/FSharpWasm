# NetWasm for F#

**F# to small, standalone WebAssembly and WASI.**<br>
Write F#. Deploy a Wasm component with no CLR, no Mono, no `dotnet.js` and no DLLs.

This is a fork of [zion-sati/NetWasm](https://github.com/zion-sati/NetWasm), an
independent compiler, CoreLib, runtime and SDK that compiles .NET CIL directly
to WebAssembly. The fork makes F# a first-class input: an ordinary `.fsproj`
builds, runs, publishes and tests like a C# project, with no IL rewriting step.
The compiler's own command line and its F#-specific rules are written in F#.
C# still compiles exactly as upstream, and upstream's test suites still pass.

```fsharp
type Shape =
    | Circle of radius: float
    | Rect of width: float * height: float

type Order = { Id: int; Items: Shape list }

let area shape =
    match shape with
    | Circle r -> System.Math.PI * r * r
    | Rect (w, h) -> w * h

let total order = order.Items |> List.sumBy area

let fetch id =
    task {
        do! System.Threading.Tasks.Task.Delay 10
        return { Id = id; Items = [ Circle 1.0; Rect (2.0, 3.0) ] }
    }

[<EntryPoint>]
let main _ =
    let order = (fetch 7).Result
    printfn "order %d: %d shapes, area %.2f" order.Id order.Items.Length (total order)
    let big = seq { 1..10 } |> Seq.filter (fun n -> n % 3 = 0) |> Seq.map (sprintf "#%d") |> String.concat " "
    printfn $"multiples of three: {big}"
    try
        failwithf "no shape %d" 42
    with Failure message -> eprintfn "caught: %s" message
    0
```

```sh
$ dotnet publish -c Release -o publish && wasmtime run publish/HelloFs.wasm
order 7: 2 shapes, area 9.14
multiples of three: #3 #6 #9
caught: no shape 42
```

## Try it

The fork is not on nuget.org: build its packages into a local feed. You need
the [.NET 11 SDK](https://dotnet.microsoft.com/download/dotnet/11.0) (the repo
pins 11.0.100-rc.2) and a WASI Preview 2 host such as
[Wasmtime](https://wasmtime.dev/) 49 or newer.

```sh
git clone https://github.com/delneg/FSharpWasm && cd FSharpWasm
eng/build-local-feed.sh                    # packages into artifacts/feed
dotnet new install artifacts/feed/NetWasm.Templates.0.10.2-fs.dev.nupkg
cd ..
dotnet new netwasm-app -lang F# -n HelloFs && cd HelloFs
dotnet new nugetconfig
dotnet nuget add source ../FSharpWasm/artifacts/feed -n netwasm-fsharp --configfile nuget.config
dotnet run                                 # 42
dotnet publish -c Release -o publish
wasmtime run publish/HelloFs.wasm
```

The template's `global.json` names NetWasm.Sdk `0.10.2-fs.dev` and rolls forward to
the newest installed .NET SDK; native tools (Node, LLD, Binaryen) come from the
upstream NetWasm 0.10.2 release through NuGet, as for C#.

An existing F# project needs `<Project Sdk="NetWasm.Sdk">`,
`<TargetFramework>netwasm0.1</TargetFramework>` and the NetWasm.Sdk version
under `msbuild-sdks` in `global.json`; the SDK supplies FSharp.Core.

## What works

Checked by programs compiled with this fork and run under Wasmtime 49.0.3:

- records, unions, pattern matching, tuples, options, lists, arrays, `Map`,
  `Set` and `seq { }`, through the ordinary FSharp.Core package;
- `task { }` and `async { }` on NetWasm's event loop: `.Result`,
  `Task.Delay`, `Async.StartAsTask`, `Async.RunSynchronously`, `Async.Sleep`;
- `printf`, `printfn`, `sprintf`, `eprintfn`, `failwithf`, `ksprintf` and
  interpolated strings. A [printf oracle](tests/fsharp/printf-oracle.fsx)
  checks 385 formats against FSharp.Core on CoreCLR: 383 agree;
- FSharp.Core's own error messages (`Seq.take`, index and count checks), with
  their values;
- several `try ... with` in a row, as fsc compiles them.

## How it works

- **Reference set.** fsc needs a primary assembly named `System.Runtime`;
  NetWasm has one CoreLib with its own name. NetWasm.Ref ships an F# reference
  set ([src/NetWasm.FSharp.Facades](src/NetWasm.FSharp.Facades), in F#): the
  CoreLib contract renamed to `System.Runtime`, plus forwarding facades for
  `NetWasm.CoreLib`, `netstandard` and `System.Runtime.Numerics`. The compiler
  aliases all four names to NetWasm.CoreLib, so the program and the stock
  netstandard2.1 FSharp.Core link against the same CoreLib.
- **Closed world.** Only the reachable part of FSharp.Core enters the Wasm.
  FSharp.Core methods that need APIs outside the profile (reflection-driven
  printing, threads, web requests) compile to bodies that throw
  `PlatformNotSupportedException`, so the rest of FSharp.Core works
  (`NetWasmFSharpCoreStrict=true` turns this off).
- **Printf without reflection.** [src/NetWasm.FSharp.Core](src/NetWasm.FSharp.Core)
  (F#) is a reflection-free printf and message formatter. The compiler rules in
  [src/NetWasm.Compiler.FSharp](src/NetWasm.Compiler.FSharp) (F#) send
  FSharp.Core's printf calls and its resource-string error messages to it.
- **SDK.** [NetWasm.FSharp.targets](src/NetWasm.Sdk/Sdk/NetWasm.FSharp.targets)
  is imported for every `.fsproj`: the reference set, FSharp.Core, the aliases
  and an F# app template (`dotnet new netwasm-app -lang F#`).
- **.NET 11.** The repository builds with the .NET 11 RC2 SDK
  (`global.json`); F# projects use the F# compiler of the SDK that builds them.

## Benchmarks: F# vs C# vs Rust

The F# ports of upstream's C# benchmark workloads, plus Rust where a port
exists, built from this checkout and run under the same Wasmtime:

```sh
eng/build-local-feed.sh
uv run benchmarks/bench.py         # or: --runs 5 --scenarios nbody sort
```

Median ms of 15 samples (min–max) after 3 warmups; NetWasm O3 with the NetWasm
GC; Rust `opt-level = 3` with LTO; Wasmtime 49.0.3 on an Intel i5-11400F (4 CPUs, Linux). Every
sample checks each language's checksum against C#'s. Lower is better. The
benchmarks roll forward to the newest .NET SDK; the .NET 10.0.401 and 11 RC2
SDKs give byte-identical Wasm (checked: compute in C# and F#, F# transaction).

| scenario | C# | F# | Rust | F#/C# |
|---|---|---|---|---|
| transaction | 24.01 (23.92–24.11) | 24.72 (24.65–24.77) | – | 1.030 |
| transaction-full | 45.42 (45.23–47.33) | 44.86 (44.44–45.16) | – | 0.988 |
| object-graph | 7.07 (6.98–7.24) | 7.05 (6.90–7.27) | – | 0.997 |
| ui-churn | 10.68 (10.56–17.01) | 11.30 (11.12–15.79) | – | 1.058 |
| nbody | 55.25 (55.15–56.11) | 52.95 (52.87–54.57) | 51.97 (51.90–52.18) | 0.958 |
| sort | 56.35 (56.15–56.57) | 56.76 (56.62–56.92) | 56.03 (55.78–56.43) | 1.007 |
| crc32 | 34.65 (34.62–34.85) | 34.64 (34.62–36.63) | 34.91 (34.86–35.05) | 1.000 |

Geometric mean F#/C#: **1.005** (1.003 in the run before). Medians move by
1-3% from run to run (ui-churn was 1.031 in that run), so F# and C# are level.

| artifact (bytes) | C# raw | C# Brotli-11 | F# raw | F# Brotli-11 | Rust raw | Rust Brotli-11 |
|---|---:|---:|---:|---:|---:|---:|
| print-42 (Oz, Compact GC) | 43,109 | 13,477 | **38,939** | **12,477** | 52,569 | 16,648 |
| compute (O3) | 119,469 | 24,957 | 112,859 | 24,316 | 103,658 | 32,825 |
| gc (O3) | 114,801 | 23,599 | 121,559 | 25,376 | – | – |
| transaction (O3) | 230,931 | 46,190 | 226,505 | 47,102 | – | – |

The F# print-42 component is smaller than both C#'s and Rust's. A program that
uses `printf` carries the formatter: the example above publishes at about 800 KB
raw.

Upstream's C# comparisons with Mono AOT, desktop .NET and other languages are in
[docs/performance.md](docs/performance.md) and
[docs/size-and-methodology.md](docs/size-and-methodology.md).

## Limitations

On top of [NetWasm's own boundary](docs/support-status.md) (no runtime
reflection, `dynamic`, assembly loading or managed threads):

- `%A` of a record or union prints its type name, not its fields; `%A` of
  options and maps drops string quotes.
- FSharp.Core error messages show their values when those are strings or
  integers, and `?` for others.
- **Runtime async** (F# 11 preview's `runtimeTask`) is not supported: those
  methods need the runtime to suspend them, and NetWasm has no lowering for them
  yet. `task { }` and `async { }` work.
- **VSTest under the .NET 11 SDK.** VSTest 18.11 discovers runtime providers
  before it loads `/TestAdapterPath` extensions, so `dotnet test` with the
  NetWasm VSTest adapter fails with "No suitable test runtime provider was
  found". The same test project passes with the .NET 10 SDK (VSTest 18.7).
- The packages that need upstream's maintainer-only native toolchain
  (NetWasm.Runtime.Pack, NetWasm.Toolchain, NetWasm.HostTools.*,
  NetWasm.Wit.Bindings.Tool) are not rebuilt here: the SDK takes them from the
  upstream 0.10.2 release.

## C# to F#

The fork ports NetWasm's own code to F# piece by piece, keeping upstream's tests
unchanged and green:

| project | language |
|---|---|
| NetWasm.Compiler.Cli (the `netwasm` compiler command line) | F# |
| NetWasm.Compiler.FSharp (printf and FSharp.Core message rules) | F# |
| NetWasm.FSharp.Core (reflection-free printf) | F# |
| NetWasm.FSharp.Facades (F# reference set) | F# |
| benchmark workloads | F# ports alongside C# |
| compiler core, Wasm emission, GC, CoreLib, SDK tasks | C#, as upstream |

Tracked lines, upstream (ef66b27) → this fork: F# 0 → 2,736 (2,149 of them
in `src`), C# in `src` 358,463 → 358,381. The ports removed about 1,100 lines of
C# (the CLI's 952 and the two F# rewrite rules); the fork's own C# additions
(CoreLib's event loop, threads, F# support in the compiler) fill most of that.
What blocks a faster port: the compiler's model is C# records that the rest of
the compiler builds with `with` expressions, which F# cannot use on C# records;
CoreLib must compile to NetWasm's closed world, where FSharp.Core's own
dependencies would come along.

## License

CoreLib, runtime libraries, templates and generated support code are MIT,
subject to preserved upstream notices. The compiler and developer tooling use
the [NetWasm Community License 1.0](LICENSES/LicenseRef-NetWasm-Community-1.0.txt).
The Compact and NetWasmGC collectors are Apache-2.0.

The Community License is free for individuals, education, open-source projects,
evaluation, and small and medium-sized businesses: any organization with fewer
than 250 employees and less than USD 10,000,000 in annual revenue. Larger
organizations pay for internal commercial use by sponsoring a matching [GitHub
Sponsors tier](https://github.com/sponsors/zion-sati), from USD 149 a month for
five developers up to USD 2,499 a month for unlimited developers. OEM and
Commercial Offering use require a separate agreement. Generated applications
may use their authors' chosen licenses. The sponsorship itself is the license:
there is no license key, sign-in, activation, telemetry or technical
enforcement.

[Licensing guide](docs/licensing.md) ·
[Third-party notices](THIRD-PARTY-NOTICES.md) ·
Commercial licensing: <zion.sati@netwasm.com>

## Upstream

NetWasm is by [zion-sati](https://github.com/zion-sati/NetWasm): its
[documentation](docs/index.md), [supported APIs](docs/support-status.md),
[roadmap](docs/roadmap.md) and C# packages on nuget.org apply here too. This
fork's changes are F#-specific; report problems with them in
[this repository](https://github.com/delneg/FSharpWasm/issues), not upstream.
