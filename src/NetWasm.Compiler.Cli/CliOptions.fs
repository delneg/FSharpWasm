namespace NetWasm.Compiler.Cli

open System
open System.Collections.Generic
open System.Collections.Immutable
open NetWasm.Compiler
open NetWasm.Compiler.ComponentModel
open NetWasm.Compiler.ComponentModel.Optimization
open NetWasm.Compiler.Core

[<AbstractClass; Sealed>]
type internal CliOptionException =
    static member Create(message: string) : CompilerException =
        CompilerException(CompilerDiagnostic(DiagnosticCode.InvalidCommandLine, message))

[<AbstractClass; Sealed>]
type internal CliOptionReader =
    /// Reads "--option value" pairs, in order.
    static member Read(arguments: string[], read: Action<string, string>) : unit =
        if isNull arguments then nullArg "arguments"
        if isNull read then nullArg "read"
        let mutable index = 0
        while index < arguments.Length do
            let option = arguments[index]
            if index + 1 >= arguments.Length then
                raise (CliOptionException.Create $"option '{option}' requires a value")
            read.Invoke(option, arguments[index + 1])
            index <- index + 2

/// The options that take one value, read once each; a second one is an error.
type private SingleValues(known: string list, unknown: string -> string) =
    let values = Dictionary<string, string>(StringComparer.Ordinal)
    let known = HashSet<string>(known, StringComparer.Ordinal)

    member _.Set(option: string, value: string) =
        if not (known.Contains option) then
            raise (CliOptionException.Create(unknown option))
        if not (values.TryAdd(option, value)) then
            raise (CliOptionException.Create $"option '{option}' was specified more than once")

    member _.Item
        with get (option: string) : string =
            match values.TryGetValue option with
            | true, value -> value
            | _ -> null

    member this.Required(option: string) : string =
        match this[option] with
        | null -> raise (CliOptionException.Create $"missing required option '{option}'")
        | value -> value

module private Parse =
    let target (value: string) =
        match value with
        | null
        | "wasm32" -> WasmTarget.Wasm32
        | "wasm64" -> WasmTarget.Wasm64
        | _ -> raise (CliOptionException.Create "target must be 'wasm32' or 'wasm64'")

    let optimization (value: string) =
        match value with
        | null
        | "Oz" -> FinalWasmOptimization.Oz
        | "None"
        | "none" -> FinalWasmOptimization.None
        | "O0" -> FinalWasmOptimization.O0
        | "O1" -> FinalWasmOptimization.O1
        | "O2" -> FinalWasmOptimization.O2
        | "O3" -> FinalWasmOptimization.O3
        | "Os" -> FinalWasmOptimization.Os
        | _ -> raise (CliOptionException.Create "optimization must be 'none', 'O0', 'O1', 'O2', 'O3', 'Os' or 'Oz'")

    let methodName (value: string) =
        match value.Split("::", 2, StringSplitOptions.None) with
        | [| typeName; methodName |] when typeName.Length <> 0 && methodName.Length <> 0 -> typeName, methodName
        | _ -> raise (CliOptionException.Create "method must use 'Namespace.Type::Method' syntax")

    let export (value: string) =
        let separator = value.IndexOf('=', StringComparison.Ordinal)
        if separator <= 0 || separator = value.Length - 1 then
            raise (CliOptionException.Create "export must use 'name=Namespace.Type::Method' syntax")
        let typeName, methodName = methodName value[separator + 1 ..]
        RequestedExport(value[.. separator - 1], typeName, methodName)

/// The componentize command's options; the C# record's positional shape, which callers build directly.
[<Sealed>]
type internal ComponentizeCliOptions
    (
        coreModule: string,
        runtimeModule: string,
        wit: string,
        world: string,
        output: string,
        manifest: string,
        target: WasmTarget,
        interopManifest: string,
        jcoVersion: string,
        preview2ShimVersion: string,
        optimization: FinalWasmOptimization
    ) =
    member _.CoreModule = coreModule
    member _.RuntimeModule = runtimeModule
    member _.Wit = wit
    member _.World = world
    member _.Output = output
    member _.Manifest = manifest
    member _.Target = target
    member _.InteropManifest = interopManifest
    member _.JcoVersion = jcoVersion
    member _.Preview2ShimVersion = preview2ShimVersion
    member _.Optimization = optimization
    member val ManagedInliningHints: string = null with get, set

    static member Parse(arguments: string[]) : ComponentizeCliOptions =
        let values =
            SingleValues(
                [ "--core-module"; "--runtime-module"; "--wit"; "--world"; "--output"; "--manifest"; "--target"
                  "--interop-manifest"; "--jco-version"; "--preview2-shim-version"; "--optimization"
                  "--managed-inlining-hints" ],
                fun option -> $"unknown componentize option '{option}'"
            )
        CliOptionReader.Read(arguments, fun option value -> values.Set(option, value))
        let coreModule = values.Required "--core-module"
        let wit = values.Required "--wit"
        let output = values.Required "--output"
        let manifest = values.Required "--manifest"
        ComponentizeCliOptions(
            coreModule,
            values["--runtime-module"],
            wit,
            values["--world"],
            output,
            manifest,
            Parse.target values["--target"],
            values["--interop-manifest"],
            values["--jco-version"],
            values["--preview2-shim-version"],
            Parse.optimization values["--optimization"],
            ManagedInliningHints = values["--managed-inlining-hints"]
        )

type internal CompileCliOptions =
    { Input: string
      Output: string
      EntryType: string
      EntryMethod: string
      References: ImmutableArray<string>
      Exports: ImmutableArray<RequestedExport>
      Target: WasmTarget
      InteropManifest: string
      RuntimeLayout: string
      DiagnosticTrace: string
      DiagnosticLog: string
      StackTraceSymbols: string
      Wit: string
      World: string
      Sources: ImmutableArray<string>
      EntryPointKind: CompilerEntryPointKind
      UseJavaScriptExportBoundary: bool
      Optimization: FinalWasmOptimization
      ManagedLoopInvariantFieldHoisting: string
      ManagedStaticDataViewElimination: string
      ManagedAddressTakenValuePromotion: string
      ManagedMethodInlining: string
      ManagedBranchPrefixDuplication: string
      ManagedScalarCallPreservation: string
      ManagedArrayBoundsCheckElimination: string
      ManagedLoopUnrolling: string
      ManagedConstantArgumentPropagation: string
      ManagedStaticArrayPopulationElimination: string
      ManagedConstantCallFolding: string
      ManagedSwitchBitTests: string
      ManagedDevirtualization: string
      ManagedAllocationElimination: string
      ManagedLoopRotation: string
      ManagedArrayStoreCheckElimination: string }

    static member Parse(arguments: string[]) : CompileCliOptions =
        if isNull arguments then nullArg "arguments"
        let values =
            SingleValues(
                [ "--input"; "--output"; "--entry"; "--entry-kind"; "--target"; "--interop-manifest"
                  "--runtime-layout"; "--diagnostic-trace"; "--diagnostic-log"; "--stack-trace-symbols"; "--wit"
                  "--world"; "--javascript-export-boundary"; "--optimization"
                  "--managed-loop-invariant-field-hoisting"; "--managed-static-data-view-elimination"
                  "--managed-address-taken-value-promotion"; "--managed-method-inlining"
                  "--managed-branch-prefix-duplication"; "--managed-scalar-call-preservation"
                  "--managed-array-bounds-check-elimination"; "--managed-loop-unrolling"
                  "--managed-constant-argument-propagation"; "--managed-static-array-population-elimination"
                  "--managed-constant-call-folding"; "--managed-switch-bit-tests"; "--managed-devirtualization"
                  "--managed-allocation-elimination"; "--managed-loop-rotation"
                  "--managed-array-store-check-elimination" ],
                fun option -> $"unknown option '{option}'"
            )
        let references = ImmutableArray.CreateBuilder<string>()
        let exports = ImmutableArray.CreateBuilder<RequestedExport>()
        let sources = ImmutableArray.CreateBuilder<string>()
        CliOptionReader.Read(
            arguments,
            fun option value ->
                match option with
                | "--reference" -> references.Add value
                | "--export" -> exports.Add(Parse.export value)
                | "--source" -> sources.Add value
                | _ -> values.Set(option, value)
        )
        let entryKind =
            match values["--entry-kind"] with
            | null
            | "raw-function" -> CompilerEntryPointKind.RawFunction
            | "managed-executable" -> CompilerEntryPointKind.ManagedExecutable
            | "library" -> CompilerEntryPointKind.Library
            | _ -> raise (CliOptionException.Create "entry kind must be 'raw-function', 'managed-executable' or 'library'")
        let entryType, entryMethod =
            if entryKind = CompilerEntryPointKind.Library then
                if not (isNull values["--entry"]) then
                    raise (CliOptionException.Create "library compilation cannot specify '--entry'")
                String.Empty, String.Empty
            else
                Parse.methodName (values.Required "--entry")
        let input = values.Required "--input"
        let output = values.Required "--output"
        let target = Parse.target values["--target"]
        let boundary =
            match values["--javascript-export-boundary"] with
            | null
            | "false" -> false
            | "true" -> true
            | _ -> raise (CliOptionException.Create "javascript export boundary must be 'true' or 'false'")
        { Input = input
          Output = output
          EntryType = entryType
          EntryMethod = entryMethod
          References = references.ToImmutable()
          Exports = exports.ToImmutable()
          Target = target
          InteropManifest = values["--interop-manifest"]
          RuntimeLayout = values["--runtime-layout"]
          DiagnosticTrace = values["--diagnostic-trace"]
          DiagnosticLog = values["--diagnostic-log"]
          StackTraceSymbols = values["--stack-trace-symbols"]
          Wit = values["--wit"]
          World = values["--world"]
          Sources = sources.ToImmutable()
          EntryPointKind = entryKind
          UseJavaScriptExportBoundary = boundary
          Optimization = Parse.optimization values["--optimization"]
          ManagedLoopInvariantFieldHoisting = values["--managed-loop-invariant-field-hoisting"]
          ManagedStaticDataViewElimination = values["--managed-static-data-view-elimination"]
          ManagedAddressTakenValuePromotion = values["--managed-address-taken-value-promotion"]
          ManagedMethodInlining = values["--managed-method-inlining"]
          ManagedBranchPrefixDuplication = values["--managed-branch-prefix-duplication"]
          ManagedScalarCallPreservation = values["--managed-scalar-call-preservation"]
          ManagedArrayBoundsCheckElimination = values["--managed-array-bounds-check-elimination"]
          ManagedLoopUnrolling = values["--managed-loop-unrolling"]
          ManagedConstantArgumentPropagation = values["--managed-constant-argument-propagation"]
          ManagedStaticArrayPopulationElimination = values["--managed-static-array-population-elimination"]
          ManagedConstantCallFolding = values["--managed-constant-call-folding"]
          ManagedSwitchBitTests = values["--managed-switch-bit-tests"]
          ManagedDevirtualization = values["--managed-devirtualization"]
          ManagedAllocationElimination = values["--managed-allocation-elimination"]
          ManagedLoopRotation = values["--managed-loop-rotation"]
          ManagedArrayStoreCheckElimination = values["--managed-array-store-check-elimination"] }
