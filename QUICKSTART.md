# NetWasm for F# quickstart

Install the .NET 11 SDK (this repository pins 11.0.100-rc.2; F# apps also build
with .NET 10.0.303 or newer). Build this checkout's packages into a local feed
and install the templates from it:

```sh
eng/build-local-feed.sh
dotnet new install artifacts/feed/NetWasm.Templates.0.10.2-fs.dev.nupkg
```

Create and run an F# application:

```sh
dotnet new netwasm-app -lang F# -n HelloNetWasm
cd HelloNetWasm
dotnet new nugetconfig
dotnet nuget add source /path/to/FSharpWasm/artifacts/feed -n netwasm-fsharp --configfile nuget.config
dotnet run
dotnet publish -c Release -o publish
wasmtime run publish/HelloNetWasm.wasm
```

`dotnet new netwasm-app` without `-lang F#` gives the C# application, as
upstream. The native build tools (Node, LLD, Binaryen) come from the upstream
NetWasm 0.10.2 release through NuGet: no Emscripten, Git or Python setup.

See the [complete SDK quickstart](docs/sdk-quickstart.md) for supported hosts,
capability grants, browser publishing, and library templates. Read the
[support status](docs/support-status.md), [limitations](docs/LIMITATIONS.md)
and the F# limitations in the [README](README.md#limitations) before porting an
existing application.

Contributors rebuilding the runtime packs need the pinned full Emscripten SDK;
see [contributor setup](CONTRIBUTING.md#local-setup).
