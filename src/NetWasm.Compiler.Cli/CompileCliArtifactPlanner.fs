namespace NetWasm.Compiler.Cli

open System
open System.Collections.Generic
open System.Collections.Immutable
open System.IO
open System.Text.Json
open System.Text.Json.Nodes
open NetWasm.Compiler
open NetWasm.Compiler.Core
open NetWasm.Compiler.Wasm
open NetWasm.Compiler.Wasm.Emission
open NetWasm.Compiler.Wasm.Emission.NativeInterop

[<AbstractClass; Sealed>]
type internal CliJson =
    static member val internal WriteOptions =
        JsonSerializerOptions(PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true, NewLine = "\n")

    static member Serialize<'T>(value: 'T) : string =
        JsonSerializer.Serialize(value, CliJson.WriteOptions)

[<Sealed>]
type internal BinaryCliArtifact(path: string, content: byte[]) =
    member _.Path = path
    member _.Content = content

[<Sealed>]
type internal TextCliArtifact(path: string, content: string) =
    member _.Path = path
    member _.Content = content

/// What one compile writes: the module, its sidecars, and whether a stale
/// native-callback object from an earlier compile goes away.
type internal CompileCliArtifactPlan =
    { OutputPath: string
      ApplicationModule: byte[]
      NativeCallbackObjectPath: string
      DeleteNativeCallbackObject: bool
      BinarySidecars: ImmutableArray<BinaryCliArtifact>
      TextSidecars: ImmutableArray<TextCliArtifact> }

type internal ICompileCliArtifactPlanner =
    abstract Plan: options: CompileCliOptions * result: CompilationResult -> CompileCliArtifactPlan

module private RuntimeLayout =
    let private text (value: string) : JsonNode = JsonValue.Create value
    let private number (value: int) : JsonNode = JsonValue.Create value
    let private valueType (value: WasmValueType) = text (value.ToString().ToLowerInvariant())

    let private optionalType (value: Nullable<WasmValueType>) : JsonNode =
        if value.HasValue then valueType value.Value else null

    let private array (values: JsonNode seq) : JsonNode = JsonArray(Seq.toArray values)

    let private obj (properties: (string * JsonNode) list) : JsonNode =
        let node = JsonObject()
        for name, value in properties do
            node[name] <- value
        node

    let create (options: CompileCliOptions) (result: CompilationResult) (callbackObjectPath: string) =
        let target = if options.Target = WasmTarget.Wasm64 then "wasm64" else "wasm32"
        let nativeImports () =
            result.NativeImports
            |> Seq.map (fun import ->
                obj
                    [ "libraryName", text import.LibraryName
                      "entryPoint", text import.EntryPoint
                      "parameters", array (Seq.map valueType import.Parameters)
                      "returnType", optionalType import.ReturnType ])
            |> array
        let common () =
            [ "target", text target
              "applicationStaticDataEnd", number result.StaticDataEnd
              "runtimeFeatures", array (result.RuntimeFeatures |> Seq.sortWith (fun a b -> String.CompareOrdinal(a, b)) |> Seq.map text)
              "nativeImports", nativeImports () ]
        match result.NativeCallbackSupport with
        | null -> CliJson.Serialize(obj (("schemaVersion", number 3) :: common ()))
        | support ->
            let callbacks =
                support.Callbacks
                |> Seq.map (fun callback ->
                    obj
                        [ "nativeSymbol", text callback.NativeSymbol
                          "runtimeImportSymbol", text callback.RuntimeImportSymbol
                          "applicationExportName", text callback.ApplicationExportName
                          "runtimeGetterExportName", text callback.RuntimeGetterExportName
                          "parameters", array (Seq.map valueType callback.Parameters)
                          "returnType", optionalType callback.ReturnType ])
                |> array
            let nativeCallbackSupport =
                obj
                    [ "fileName", text (Path.GetFileName callbackObjectPath)
                      "sha256", text support.Sha256
                      "callbacks", callbacks
                      "temporaryApplicationExports", array (Seq.map text support.TemporaryApplicationExports)
                      "temporaryRuntimeExports", array (Seq.map text support.TemporaryRuntimeExports) ]
            CliJson.Serialize(
                obj (("schemaVersion", number 4) :: common () @ [ "nativeCallbackSupport", nativeCallbackSupport ])
            )

[<Sealed>]
type internal CompileCliArtifactPlanner(callbackSupport: INativeCallbackSupportArtifactValidator) =
    do if isNull (box callbackSupport) then nullArg "callbackSupport"

    static let validateDistinctPaths (options: CompileCliOptions) (callbackObjectPath: string) =
        // Sidecars may be copied between case-sensitive and case-insensitive hosts.
        // Reject case-only aliases everywhere so one artifact can never overwrite
        // another after such a move.
        let distinct = HashSet<string>(StringComparer.OrdinalIgnoreCase)
        for path in
            [ options.Output
              callbackObjectPath
              options.RuntimeLayout
              options.InteropManifest
              options.StackTraceSymbols ] do
            if not (isNull path) && not (distinct.Add(Path.GetFullPath path)) then
                raise (CliOptionException.Create "compiler output and sidecar paths must be distinct")

    member _.Plan(options: CompileCliOptions, result: CompilationResult) : CompileCliArtifactPlan =
        if isNull (box options) then nullArg "options"
        if isNull result then nullArg "result"
        if result.NativeImports.IsDefault then
            invalidOp "The compiler native import facts are uninitialized."
        callbackSupport.Validate result.NativeCallbackSupport
        if not (isNull result.NativeCallbackSupport) && isNull options.RuntimeLayout then
            raise (CliOptionException.Create "native callbacks require option '--runtime-layout'")
        let stackTraceSymbols =
            if isNull options.StackTraceSymbols then null
            elif isNull result.StackTraceSymbols then
                invalidOp "stack-trace instrumentation did not produce a symbol sidecar"
            else result.StackTraceSymbols
        let callbackObjectPath = Path.ChangeExtension(options.Output, ".callbacks.o")
        validateDistinctPaths options callbackObjectPath
        let binarySidecars = ImmutableArray.CreateBuilder<BinaryCliArtifact>()
        if not (isNull result.NativeCallbackSupport) then
            binarySidecars.Add(BinaryCliArtifact(callbackObjectPath, result.NativeCallbackSupport.ObjectBytes))
        if not (isNull stackTraceSymbols) then
            binarySidecars.Add(BinaryCliArtifact(options.StackTraceSymbols, stackTraceSymbols.Bytes))
        let textSidecars = ImmutableArray.CreateBuilder<TextCliArtifact>()
        if not (isNull options.RuntimeLayout) then
            textSidecars.Add(
                TextCliArtifact(options.RuntimeLayout, RuntimeLayout.create options result callbackObjectPath)
            )
        if not (isNull options.InteropManifest) then
            textSidecars.Add(TextCliArtifact(options.InteropManifest, CliJson.Serialize result.InteropManifest))
        { OutputPath = options.Output
          ApplicationModule = result.ApplicationModule
          NativeCallbackObjectPath = callbackObjectPath
          DeleteNativeCallbackObject = isNull result.NativeCallbackSupport
          BinarySidecars = binarySidecars.ToImmutable()
          TextSidecars = textSidecars.ToImmutable() }

    interface ICompileCliArtifactPlanner with
        member this.Plan(options, result) = this.Plan(options, result)
