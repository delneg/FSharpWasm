using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

/// <summary>
/// Sends FSharp.Core's printf calls (sprintf, printfn, failwithf, ksprintf, interpolated
/// strings, ...) to NetWasm.FSharp.Core's Printf, whose methods take the same arguments.
/// FSharp.Core builds its printers with runtime reflection, which NetWasm does not deploy;
/// NetWasm.FSharp.Core has one generic method per family and arity, so the closed world
/// sees each printer's argument types statically. A call of more arguments than the
/// library covers, or a program without the library, keeps FSharp.Core's method.
/// </summary>
internal sealed class FSharpPrintfCallRewriter(
    ITypeFinder types,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols) : IMethodRewriteRule
{
    private const string LibraryType = "NetWasm.FSharp.Core.Printf";
    private const string FunctionType = "Microsoft.FSharp.Core.FSharpFunc`2";

    /// FSharp.Core method (owner, name, generic parameter count) → the library family, and
    /// whether the generic parameters are [Result, T] (else [T] or [T, Result]).
    private static readonly Dictionary<(string Owner, string Name, int Generics), (string Family, bool ResultFirst)> s_entryPoints = new()
    {
        [("Microsoft.FSharp.Core.ExtraTopLevelOperators", "PrintFormatToString", 1)] = ("Sprintf", false),
        [("Microsoft.FSharp.Core.PrintfModule", "PrintFormatToStringThen", 1)] = ("Sprintf", false),
        [("Microsoft.FSharp.Core.ExtraTopLevelOperators", "PrintFormatToStringThenFail", 2)] = ("Failwithf", false),
        [("Microsoft.FSharp.Core.PrintfModule", "PrintFormatToStringThenFail", 2)] = ("Failwithf", false),
        [("Microsoft.FSharp.Core.PrintfModule", "PrintFormatToStringThen", 2)] = ("Ksprintf", true),
        [("Microsoft.FSharp.Core.PrintfModule", "PrintFormatThen", 2)] = ("Ksprintf", true),
        [("Microsoft.FSharp.Core.PrintfModule", "PrintFormatToStringBuilderThen", 2)] = ("Kbprintf", true),
        [("Microsoft.FSharp.Core.PrintfModule", "PrintFormatToTextWriterThen", 2)] = ("Kfprintf", true),
        [("Microsoft.FSharp.Core.PrintfModule", "PrintFormatToStringBuilder", 1)] = ("Bprintf", false),
        [("Microsoft.FSharp.Core.PrintfModule", "PrintFormatToTextWriter", 1)] = ("Fprintf", false),
        [("Microsoft.FSharp.Core.ExtraTopLevelOperators", "PrintFormatToTextWriter", 1)] = ("Fprintf", false),
        [("Microsoft.FSharp.Core.PrintfModule", "PrintFormatLineToTextWriter", 1)] = ("Fprintfn", false),
        [("Microsoft.FSharp.Core.ExtraTopLevelOperators", "PrintFormatLineToTextWriter", 1)] = ("Fprintfn", false),
        [("Microsoft.FSharp.Core.PrintfModule", "PrintFormat", 1)] = ("Printf", false),
        [("Microsoft.FSharp.Core.ExtraTopLevelOperators", "PrintFormat", 1)] = ("Printf", false),
        [("Microsoft.FSharp.Core.PrintfModule", "PrintFormatLine", 1)] = ("Printfn", false),
        [("Microsoft.FSharp.Core.ExtraTopLevelOperators", "PrintFormatLine", 1)] = ("Printfn", false),
        [("Microsoft.FSharp.Core.PrintfModule", "PrintFormatToError", 1)] = ("Eprintf", false),
        [("Microsoft.FSharp.Core.ExtraTopLevelOperators", "PrintFormatToError", 1)] = ("Eprintf", false),
        [("Microsoft.FSharp.Core.PrintfModule", "PrintFormatLineToError", 1)] = ("Eprintfn", false),
        [("Microsoft.FSharp.Core.ExtraTopLevelOperators", "PrintFormatLineToError", 1)] = ("Eprintfn", false),
    };

    private readonly Lazy<Dictionary<string, MethodDefinitionModel>?> _library = new(() => FindLibrary(types, methods));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ImmutableArray<CilInstruction>.Builder? rewritten = null;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var call = body.Instructions[index];
            if (call.Operation != CilOperation.Call ||
                calledMethods.Resolve(call) is not { } target ||
                target.Definition.Key.Assembly.Name != "FSharp.Core" ||
                !s_entryPoints.TryGetValue(
                    (symbols.Format(target.Definition.DeclaringType), target.Definition.Name, target.MethodArguments.Length),
                    out var entry) ||
                _library.Value is not { } library ||
                Replacement(library, entry.Family, entry.ResultFirst, target.MethodArguments) is not { } replacement)
            {
                continue;
            }
            rewritten ??= body.Instructions.ToBuilder();
            rewritten[index] = call with { Operand = new CilOperand.MethodInstance(replacement) };
        }
        return rewritten is null ? body : body with { Instructions = rewritten.ToImmutable() };
    }

    private MethodInstanceModel? Replacement(
        Dictionary<string, MethodDefinitionModel> library,
        string family,
        bool resultFirst,
        ImmutableArray<CliTypeIdentity> arguments)
    {
        if (arguments.Any(argument => argument.ContainsGenericParameters))
        {
            return null;
        }
        var printer = resultFirst || arguments.Length == 1 ? arguments[^1] : arguments[0];
        CliTypeIdentity? result = arguments.Length == 2 ? (resultFirst ? arguments[0] : arguments[1]) : null;
        var parameters = ImmutableArray.CreateBuilder<CliTypeIdentity>();
        // Peel 'A1 -> 'A2 -> ... -> 'Result; with an explicit 'Result, stop at it, since
        // a continuation's result may itself be a function.
        while (!printer.Equals(result) && IsFunction(printer))
        {
            parameters.Add(printer.TypeArguments[0]);
            printer = printer.TypeArguments[1];
        }
        if (result is not null && !printer.Equals(result) ||
            !library.TryGetValue(family + parameters.Count, out var method))
        {
            return null;
        }
        if (result is not null)
        {
            parameters.Add(result);
        }
        else if (parameters.Count == 0 && family is not "Sprintf")
        {
            // The arity-0 printers of unit take FSharp.Core's 'T (unit) to return it.
            parameters.Add(printer);
        }
        return methodInstances.ResolveMethodInstance(
            method.Key.Assembly,
            method.Key.MetadataToken,
            symbols.Format(method),
            0,
            new CliGenericContext([], parameters.ToImmutable()));
    }

    private static bool IsFunction(CliTypeIdentity type) =>
        type.Shape == CliTypeShape.GenericInstantiation &&
        type.ElementType?.FullName == FunctionType &&
        type.TypeArguments.Length == 2;

    private static Dictionary<string, MethodDefinitionModel>? FindLibrary(ITypeFinder types, IMethodRepository methods)
    {
        TypeDefinitionModel printf;
        try
        {
            printf = types.FindType(LibraryType);
        }
        catch (CompilerException)
        {
            return null;
        }
        var byName = new Dictionary<string, MethodDefinitionModel>(StringComparer.Ordinal);
        foreach (var method in printf.Methods.Select(methods.GetMethod))
        {
            if (method.IsStatic)
            {
                byName[method.Name] = method;
            }
        }
        return byName;
    }
}
