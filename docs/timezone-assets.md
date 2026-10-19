# Timezone assets

NetWasm defaults to UTC and does not embed timezone data in CoreLib or the
application Wasm module. Applications that expose reachable local-time APIs and
set `TZ` to an IANA identifier must explicitly deploy a matching timezone asset.

## Choose the zones a deployment ships

List the zones in the project file:

```xml
<ItemGroup>
  <NetWasmTimeZone Include="Australia/Melbourne;America/New_York" />
</ItemGroup>
```

Aliases resolve to their zones automatically, so `Asia/Calcutta` ships with
`Asia/Kolkata`. To ship every zone instead, set
`<NetWasmTimeZoneMode>Full</NetWasmTimeZoneMode>` and list none.

Build, `dotnet run` and every publish (local, browser, `JsModule` and WIT
workers) then write `<program>.wasm.tz-info` beside the primary Wasm and record
it in the deployment manifest. The zones come from the IANA release pinned by
NetWasm.Toolchain, so every machine produces the same sidecar. The sidecar is
written only when the application reaches a local-time API. Otherwise the build
warns with NWSDK080 and ships nothing. An unknown zone, or `Full` combined with
listed zones, fails with NWSDK079. Any `NetWasmTimeZoneMode` other than
`Selected` or `Full` fails with NWSDK083, and `Selected` without a
`NetWasmTimeZone` item fails with NWSDK084.

`TZ` remains an explicit execution setting; NetWasm never infers a host or
browser zone. Set it with a `NetWasmEnvironmentVariable` item, in the execution
request's environment, or per instance with `open({ environment: { TZ } })` for a
page-hosted `JsModule`. A page that wants the user's zone passes
`Intl.DateTimeFormat().resolvedOptions().timeZone`, so its sidecar must include
that zone. A `TZ` outside the sidecar fails before managed code starts.

One zone with daylight saving time is about 210 KB (about 51 KB with Brotli).
Each alias is stored as another full copy of its zone, which Brotli removes
almost entirely: `Australia/Melbourne` and `America/New_York` ship with three
aliases (`Australia/Victoria`, `US/Eastern` and `EST5EDT`) as about 1 MB, or
about 100 KB with Brotli. `Full` is about 40.7 MB, or 1.8 MB with Brotli. Static
hosts that cannot serve the Brotli form send the full size.

## Generate an asset directly

The SDK uses the same generator as the standalone tool, which remains available
for custom hosts:

```sh
dotnet run --project tools/NetWasm.TimeZones -- generate \
  --source /path/to/pinned/zoneinfo \
  --asset dist/netwasm-timezones.nwtz \
  --brotli dist/netwasm-timezones.nwtz.br \
  --manifest dist/netwasm-timezones.json \
  --browser-loader dist/netwasm-timezones.loader.mjs \
  --zone Australia/Melbourne
```

Repeat `--zone` for an explicit application-specific selection. Required IANA
aliases are included automatically. Use `--all` instead of `--zone` to publish
the convenient official full-database sidecar. There is intentionally no
subjective “common zones” preset.

The source directory must contain compiled TZif files and an authoritative IANA
version in `version`, `+VERSION`, or the first line of `tzdata.zi`. The generator
retains historical transitions and materializes the TZif future rule through the
full `DateTime` range. Its outputs are deterministic for the same source:

- `netwasm-timezones.nwtz`: the uncompressed indexed payload mounted through
  WASI;
- `netwasm-timezones.nwtz.br`: the transfer/deployment form;
- `netwasm-timezones.json`: the IANA version, payload identity, exact sizes, and
  included zone names;
- `netwasm-timezones.loader.mjs`: browser configuration for the standard
  Preview 2 WASI environment and filesystem capabilities.

For browsers, serve the Brotli representation at the asset URL with
`Content-Encoding: br`. `fetch()` receives the decompressed bytes, and the
generated loader mounts those bytes as
`/netwasm-timezones/netwasm-timezones.nwtz` before jco instantiates the
component. The loader uses `@bytecodealliance/preview2-shim`; it does not add a
framework `JSImport`. It was qualified against jco 1.29.0 and Preview 2 shim
0.20.2. Browser support in that shim is currently described upstream as
experimental, so deployments should retain the NetWasm browser qualification
gate when upgrading it.

Invoke the generated loader only for a component whose imports show reachable
local-time support. Passing no `timeZone`, `UTC`, or `Etc/UTC` clears timezone
preopens and performs no asset fetch. Any other identifier configures `TZ`,
fetches the asset, and fails immediately if the response is unsuccessful.

Native hosts decompress the `.br` deployment artifact before startup and
preopen a read-only directory at `/netwasm-timezones` containing the
uncompressed payload. Both native and browser lanes therefore expose the same
standard `wasi:filesystem` contract to managed code.

Bootstrap validates the requested identifier, payload format, embedded SHA-256
digest, pinned IANA version, and selected-zone membership before the managed
entry point runs. Invalid `TZ`, missing capabilities, missing files, corrupt or
incompatible data, and absent zones fail loudly with a deterministic diagnostic;
they never fall back to UTC.
