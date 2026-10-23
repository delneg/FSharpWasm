module internal NetWasm.Compiler.Cli.Program

open System
open Microsoft.Extensions.DependencyInjection
open NetWasm.Compiler
open NetWasm.Compiler.Core

[<EntryPoint>]
let Main (args: string[]) : int =
    try
        use services =
            ServiceCollection()
                .AddNetWasmCompiler()
                .AddNetWasmCompilerCli()
                .BuildServiceProvider(ServiceProviderOptions(ValidateOnBuild = true, ValidateScopes = true))
        services.GetRequiredService<ICompilerCli>().Run args
    with :? CompilerException as error ->
        Console.Error.WriteLine(error.Diagnostic)
        1
