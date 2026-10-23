namespace NetWasm.Compiler.Analysis

open System
open System.Collections.Generic
open NetWasm.Compiler.Core
open NetWasm.Compiler.Metadata

/// The static methods of one NetWasm.FSharp.Core type, when the program has the library.
module internal Library =
    /// None when the program does not reference NetWasm.FSharp.Core (a C# program).
    let staticMethods (types: ITypeFinder) (methods: IMethodRepository) (typeName: string) =
        let found =
            try
                Some(types.FindType typeName)
            with :? CompilerException ->
                None
        found
        |> Option.map (fun library ->
            library.Methods |> Seq.map methods.GetMethod |> Seq.filter (fun m -> m.IsStatic) |> Seq.toArray)

    /// The method instance of a non-generic or closed generic library method.
    let instance
        (methodInstances: IMethodInstanceResolver)
        (symbols: ISymbolFormatter)
        (method: MethodDefinitionModel)
        (methodArguments: CliTypeIdentity seq)
        =
        methodInstances.ResolveMethodInstance(
            method.Key.Assembly,
            method.Key.MetadataToken,
            symbols.Format method,
            0,
            CliGenericContext(Collections.Immutable.ImmutableArray.Empty, Collections.Immutable.ImmutableArray.CreateRange methodArguments)
        )

    /// Rewrites the call instructions for which replace answers, keeping the body when none does.
    let rewriteCalls (replace: int -> CilInstruction -> MethodInstanceModel option) (body: CilMethodBody) =
        let instructions = body.Instructions
        let mutable rewritten: CilInstruction[] = null
        for index in 0 .. instructions.Length - 1 do
            let call = instructions[index]
            if call.Operation = CilOperation.Call then
                match replace index call with
                | Some target ->
                    if isNull rewritten then rewritten <- instructions.AsSpan().ToArray()
                    rewritten[index] <- call.WithOperand(CilOperand.MethodInstance target)
                | None -> ()
        if isNull rewritten then body
        else body.WithInstructions(Collections.Immutable.ImmutableArray.Create<CilInstruction> rewritten)
