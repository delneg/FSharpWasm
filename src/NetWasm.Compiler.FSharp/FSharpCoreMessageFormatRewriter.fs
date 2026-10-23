namespace NetWasm.Compiler.Analysis

open System
open NetWasm.Compiler.Core
open NetWasm.Compiler.Metadata

/// In FSharp.Core, sends a String.Format call that shortly precedes a throw to
/// NetWasm.FSharp.Core's Messages.Format, which fills the message's holes without
/// composite formatting. FSharp.Core builds its argument-error messages this way, and
/// String.Format alone is about 220 KB of Wasm that most F# programs reach only
/// through such an error path (Array.zeroCreate is one). Programs without the
/// library keep String.Format.
[<Sealed>]
type internal FSharpCoreMessageFormatRewriter
    (
        types: ITypeFinder,
        methods: IMethodRepository,
        methodInstances: IMethodInstanceResolver,
        calledMethods: ICalledMethodResolver,
        symbols: ISymbolFormatter
    ) =
    static let throwWindow = 8

    static let signatureKey (parameters: CliTypeIdentity seq) =
        String.Join(",", parameters |> Seq.map (fun p -> p.CanonicalName))

    let helpers =
        lazy
            (Library.staticMethods types methods "NetWasm.FSharp.Core.Messages"
             |> Option.map (fun found ->
                 found
                 |> Seq.filter (fun m -> m.Name = "Format")
                 |> Seq.map (fun m -> signatureKey m.Signature.ParameterSignatureTypes, m)
                 |> dict))

    member _.Rewrite(body: CilMethodBody) : CilMethodBody =
        if body.Method.Key.Assembly.Name <> "FSharp.Core" then
            body
        else
            let instructions = body.Instructions
            let throwsSoon index =
                seq { index + 1 .. min (instructions.Length - 1) (index + throwWindow) }
                |> Seq.exists (fun next -> instructions[next].Operation = CilOperation.Throw)
            body
            |> Library.rewriteCalls (fun index call ->
                if not (throwsSoon index) then
                    None
                else
                    match calledMethods.Resolve call with
                    | null -> None
                    | target when
                        target.Definition.Name = "Format"
                        && symbols.Format target.Definition.DeclaringType = "System.String"
                        ->
                        match helpers.Value with
                        | Some helpers ->
                            match helpers.TryGetValue(signatureKey target.Signature.ParameterSignatureTypes) with
                            | true, helper -> Some(Library.instance methodInstances symbols helper [])
                            | _ -> None
                        | None -> None
                    | _ -> None)

    interface IMethodRewriteRule with
        member this.Rewrite body = this.Rewrite body
