# NetWasmProject

Publish the module and its page:

```bash
dotnet publish -c Release
```

Serve `bin/Release/netwasm0.1/publish` with any static HTTP server, then open
its `index.html`. The page opens the C# module, calls its `greet` export
synchronously on the page, and closes it. The export calls back into
`app.host.mjs`, which writes to the page.

A synchronous export blocks the page while it runs. Return `Task` from an
export for longer work, or create the project with `--host worker` to run it
in a Web Worker.
