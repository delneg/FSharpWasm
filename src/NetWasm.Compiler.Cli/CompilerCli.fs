namespace NetWasm.Compiler.Cli

open System
open System.Collections.Generic
open System.Linq
open System.Runtime.CompilerServices
open Microsoft.Extensions.DependencyInjection
open NetWasm.Compiler.Wasm.Emission.NativeInterop

type internal ICompilerCli =
    abstract Run: arguments: string[] -> int

/// Dispatches on the first argument when it names a command, else runs the compile.
[<Sealed>]
type internal CompilerCli(commands: IEnumerable<ICompilerCliCommand>) =
    let commands =
        commands.ToDictionary((fun (command: ICompilerCliCommand) -> command.Name), StringComparer.Ordinal)

    member _.Run(arguments: string[]) : int =
        if isNull arguments then nullArg "arguments"
        let name =
            if arguments.Length <> 0 && commands.ContainsKey arguments[0] then arguments[0]
            else CompileCliCommand.CommandName
        let commandArguments = if name = CompileCliCommand.CommandName then arguments else arguments[1..]
        commands[name].Run commandArguments

    interface ICompilerCli with
        member this.Run arguments = this.Run arguments

[<Extension; AbstractClass; Sealed>]
type internal CompilerCliServiceCollectionExtensions =
    [<Extension>]
    static member AddNetWasmCompilerCli(services: IServiceCollection) : IServiceCollection =
        if isNull services then nullArg "services"
        services
            .AddSingleton<ICompilerCliCommand, CompileCliCommand>()
            .AddSingleton<ICompilerCliCommand, ComponentizeCliCommand>()
            .AddSingleton<ITextFileReader, SystemTextFileReader>()
            .AddSingleton<ITextFileWriter, SystemTextFileWriter>()
            .AddSingleton<IBinaryFileWriter, SystemBinaryFileWriter>()
            .AddSingleton<IFileDeleter, SystemFileDeleter>()
            .AddSingleton<INativeCallbackSupportArtifactValidator, NativeCallbackSupportArtifactValidator>()
            .AddSingleton<ICompileCliArtifactPlanner, CompileCliArtifactPlanner>()
            .AddSingleton<IComponentManifestInputReader, ComponentManifestInputReader>()
            .AddSingleton<ICompilerCli, CompilerCli>()
