namespace NetWasm.Compiler.Cli

open System
open System.Collections.Immutable
open System.Text.Json
open NetWasm.Compiler
open NetWasm.Compiler.ComponentModel
open NetWasm.Compiler.ComponentModel.Optimization
open NetWasm.Compiler.Core

// ComponentModel has file capabilities of the same names; reopening this
// namespace makes the Cli's own win.
open NetWasm.Compiler.Cli

type internal ICompilerCliCommand =
    abstract Name: string
    abstract Run: arguments: string[] -> int

type internal IComponentManifestInputReader =
    abstract Read: options: ComponentizeCliOptions -> ComponentManifestInputs

module private Guard =
    let notNull (name: string) (value: 'T) =
        if isNull (box value) then nullArg name
        value

/// The default command: compiles an assembly to a core Wasm module and its sidecars.
[<Sealed>]
type internal CompileCliCommand
    (
        compiler: INetWasmCompiler,
        artifacts: ICompileCliArtifactPlanner,
        binaryFiles: IBinaryFileWriter,
        textFiles: ITextFileWriter,
        files: IFileDeleter,
        optimizationOverrides: IManagedCompilerOptimizationOverridesParser
    ) =
    let compiler = Guard.notNull "compiler" compiler
    let artifacts = Guard.notNull "artifacts" artifacts
    let binaryFiles = Guard.notNull "binaryFiles" binaryFiles
    let textFiles = Guard.notNull "textFiles" textFiles
    let files = Guard.notNull "files" files
    let optimizationOverrides = Guard.notNull "optimizationOverrides" optimizationOverrides

    static member CommandName = ""

    member _.Name = CompileCliCommand.CommandName

    member _.Run(arguments: string[]) : int =
        let options = CompileCliOptions.Parse arguments
        let overrides =
            optimizationOverrides.Parse(
                options.ManagedLoopInvariantFieldHoisting,
                options.ManagedStaticDataViewElimination,
                options.ManagedAddressTakenValuePromotion,
                options.ManagedMethodInlining,
                options.ManagedBranchPrefixDuplication,
                options.ManagedScalarCallPreservation,
                options.ManagedArrayBoundsCheckElimination,
                options.ManagedLoopUnrolling,
                options.ManagedConstantArgumentPropagation,
                options.ManagedStaticArrayPopulationElimination,
                options.ManagedConstantCallFolding,
                options.ManagedSwitchBitTests,
                options.ManagedDevirtualization,
                options.ManagedAllocationElimination,
                options.ManagedLoopRotation,
                options.ManagedArrayStoreCheckElimination
            )
        let result =
            compiler.Compile(
                CompilerOptions(
                    options.Input,
                    options.References,
                    options.EntryType,
                    options.EntryMethod,
                    options.Exports,
                    options.Target,
                    options.DiagnosticTrace,
                    options.DiagnosticLog,
                    options.Wit,
                    options.World,
                    options.Sources,
                    EmitStackTrace = not (isNull options.StackTraceSymbols),
                    EntryPointKind = options.EntryPointKind,
                    UseJavaScriptExportBoundary = options.UseJavaScriptExportBoundary,
                    Optimization = options.Optimization,
                    OptimizationOverrides = overrides
                )
            )
        let plan = artifacts.Plan(options, result)
        if plan.DeleteNativeCallbackObject then
            files.Delete plan.NativeCallbackObjectPath
        for artifact in plan.BinarySidecars do
            binaryFiles.Write(artifact.Path, artifact.Content)
        for artifact in plan.TextSidecars do
            textFiles.Write(artifact.Path, artifact.Content)
        binaryFiles.Write(plan.OutputPath, plan.ApplicationModule)
        0

    interface ICompilerCliCommand with
        member this.Name = this.Name
        member this.Run arguments = this.Run arguments

/// "componentize": wraps a core module into a WASI 0.2 component and writes its manifest.
[<Sealed>]
type internal ComponentizeCliCommand
    (
        documents: IWitDocumentReader,
        validator: IWitWorldValidator,
        manifests: IComponentManifestBuilder,
        packager: IComponentPackager,
        manifestInputs: IComponentManifestInputReader,
        textFiles: ITextFileWriter,
        inlineHints: IManagedInliningHintsParser
    ) =
    let documents = Guard.notNull "documents" documents
    let validator = Guard.notNull "validator" validator
    let manifests = Guard.notNull "manifests" manifests
    let packager = Guard.notNull "packager" packager
    let manifestInputs = Guard.notNull "manifestInputs" manifestInputs
    let textFiles = Guard.notNull "textFiles" textFiles
    let inlineHints = Guard.notNull "inlineHints" inlineHints

    member _.Name = "componentize"

    member _.Run(arguments: string[]) : int =
        let options = ComponentizeCliOptions.Parse arguments
        let optimizationOverrides = inlineHints.Parse options.ManagedInliningHints
        let target =
            if options.Target = WasmTarget.Wasm32 then ComponentTarget.Wasm32Wasi02
            else ComponentTarget.Wasm64Wasi02
        let document = documents.Read options.Wit
        let world = document.SelectWorld options.World
        validator.Validate(document, world)
        let manifest = manifests.Build(document, world, target, manifestInputs.Read options)
        packager.Package(
            ComponentPackageRequest(
                options.CoreModule,
                options.Wit,
                options.World,
                options.Output,
                target,
                options.RuntimeModule,
                Optimization = options.Optimization,
                OptimizationOverrides = optimizationOverrides
            )
        )
        textFiles.Write(options.Manifest, CliJson.Serialize manifest)
        0

    interface ICompilerCliCommand with
        member this.Name = this.Name
        member this.Run arguments = this.Run arguments

/// Reads the interop manifest a compile wrote, for the component's JavaScript boundary.
[<Sealed>]
type internal ComponentManifestInputReader(textFiles: ITextFileReader) =
    static let readOptions = JsonSerializerOptions(PropertyNameCaseInsensitive = true)
    let textFiles = Guard.notNull "textFiles" textFiles

    member _.Read(options: ComponentizeCliOptions) : ComponentManifestInputs =
        if isNull (box options) then nullArg "options"
        let interop =
            match options.InteropManifest with
            | null -> null
            | path ->
                match JsonSerializer.Deserialize<HostInteropManifest>(textFiles.Read path, readOptions) with
                | null -> raise (CliOptionException.Create $"interop manifest '{path}' is empty")
                | manifest ->
                    let expected = if options.Target = WasmTarget.Wasm64 then "wasm64" else "wasm32"
                    if manifest.Target <> expected then
                        raise (
                            CliOptionException.Create
                                $"interop manifest target '{manifest.Target}' does not match component target '{expected}'"
                        )
                    manifest
        let boundary =
            match interop with
            | null -> ComponentJavaScriptBoundary.Empty
            | interop ->
                ComponentJavaScriptBoundary(
                    ImmutableArray.CreateRange(
                        interop.Imports
                        |> Seq.map (fun import ->
                            ComponentJavaScriptImport(
                                import.Module,
                                import.Name,
                                import.Parameters,
                                import.Result,
                                import.AsyncReturn
                            ))
                    ),
                    ImmutableArray.CreateRange(
                        interop.Exports
                        |> Seq.map (fun export ->
                            ComponentJavaScriptExport(export.Name, export.Parameters, export.Result, export.AsyncReturn))
                    )
                )
        ComponentManifestInputs(boundary, ComponentAdapterVersions(options.JcoVersion, options.Preview2ShimVersion))

    interface IComponentManifestInputReader with
        member this.Read options = this.Read options
