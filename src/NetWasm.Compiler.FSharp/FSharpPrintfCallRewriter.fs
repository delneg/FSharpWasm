namespace NetWasm.Compiler.Analysis

open System
open System.Collections.Generic
open NetWasm.Compiler.Core
open NetWasm.Compiler.Metadata

/// Sends FSharp.Core's printf calls (sprintf, printfn, failwithf, ksprintf, interpolated
/// strings, ...) to NetWasm.FSharp.Core's Printf, whose methods take the same arguments.
/// FSharp.Core builds its printers with runtime reflection, which NetWasm does not deploy;
/// NetWasm.FSharp.Core has one generic method per family and arity, so the closed world
/// sees each printer's argument types statically. A call of more arguments than the
/// library covers, or a program without the library, keeps FSharp.Core's method.
[<Sealed>]
type internal FSharpPrintfCallRewriter
    (
        types: ITypeFinder,
        methods: IMethodRepository,
        methodInstances: IMethodInstanceResolver,
        calledMethods: ICalledMethodResolver,
        symbols: ISymbolFormatter
    ) =
    static let functionType = "Microsoft.FSharp.Core.FSharpFunc`2"

    /// FSharp.Core method (owner, name, generic parameter count) → the library family, and
    /// whether the generic parameters are [Result, T] (else [T] or [T, Result]).
    static let entryPoints =
        let core = "Microsoft.FSharp.Core."
        let both name generics family = [ ("ExtraTopLevelOperators", name, generics), family; ("PrintfModule", name, generics), family ]
        [ [ ("ExtraTopLevelOperators", "PrintFormatToString", 1), ("Sprintf", false)
            ("PrintfModule", "PrintFormatToStringThen", 1), ("Sprintf", false)
            ("PrintfModule", "PrintFormatToStringThen", 2), ("Ksprintf", true)
            ("PrintfModule", "PrintFormatThen", 2), ("Ksprintf", true)
            ("PrintfModule", "PrintFormatToStringBuilderThen", 2), ("Kbprintf", true)
            ("PrintfModule", "PrintFormatToTextWriterThen", 2), ("Kfprintf", true)
            ("PrintfModule", "PrintFormatToStringBuilder", 1), ("Bprintf", false) ]
          both "PrintFormatToStringThenFail" 2 ("Failwithf", false)
          both "PrintFormatToTextWriter" 1 ("Fprintf", false)
          both "PrintFormatLineToTextWriter" 1 ("Fprintfn", false)
          both "PrintFormat" 1 ("Printf", false)
          both "PrintFormatLine" 1 ("Printfn", false)
          both "PrintFormatToError" 1 ("Eprintf", false)
          both "PrintFormatLineToError" 1 ("Eprintfn", false) ]
        |> List.concat
        |> List.map (fun ((owner, name, generics), family) -> struct (core + owner, name, generics), family)
        |> dict

    static let isFunction (t: CliTypeIdentity) =
        t.Shape = CliTypeShape.GenericInstantiation
        && not (isNull t.ElementType)
        && t.ElementType.FullName = functionType
        && t.TypeArguments.Length = 2

    let library =
        lazy
            (Library.staticMethods types methods "NetWasm.FSharp.Core.Printf"
             |> Option.map (fun found -> found |> Seq.map (fun m -> m.Name, m) |> dict))

    let replacement (library: IDictionary<string, MethodDefinitionModel>) family resultFirst (arguments: CliTypeIdentity[]) =
        if arguments |> Array.exists (fun a -> a.ContainsGenericParameters) then
            None
        else
            let mutable printer = if resultFirst || arguments.Length = 1 then arguments[arguments.Length - 1] else arguments[0]
            let result =
                if arguments.Length = 2 then Some(if resultFirst then arguments[0] else arguments[1]) else None
            let parameters = ResizeArray<CliTypeIdentity>()
            // Peel 'A1 -> 'A2 -> ... -> 'Result; with an explicit 'Result, stop at it, since
            // a continuation's result may itself be a function.
            while result <> Some printer && isFunction printer do
                parameters.Add printer.TypeArguments[0]
                printer <- printer.TypeArguments[1]
            match library.TryGetValue(family + string parameters.Count) with
            | true, method when result.IsNone || result = Some printer ->
                match result with
                | Some result -> parameters.Add result
                // The arity-0 printers of unit take FSharp.Core's 'T (unit) to return it.
                | None when parameters.Count = 0 && family <> "Sprintf" -> parameters.Add printer
                | None -> ()
                Some(Library.instance methodInstances symbols method parameters)
            | _ -> None

    member _.Rewrite(body: CilMethodBody) : CilMethodBody =
        body
        |> Library.rewriteCalls (fun _ call ->
            match calledMethods.Resolve call with
            | null -> None
            | target when target.Definition.Key.Assembly.Name <> "FSharp.Core" -> None
            | target ->
                let key =
                    struct (symbols.Format target.Definition.DeclaringType, target.Definition.Name, target.MethodArguments.Length)
                match entryPoints.TryGetValue key, library.Value with
                | (true, (family, resultFirst)), Some library ->
                    replacement library family resultFirst (Seq.toArray target.MethodArguments)
                | _ -> None)

    interface IMethodRewriteRule with
        member this.Rewrite body = this.Rewrite body
