using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

/// <summary>
/// In FSharp.Core, sends a String.Format call that shortly precedes a throw to
/// NetWasm.FSharp.Core's Messages.Format, which fills the message's holes without
/// composite formatting. FSharp.Core builds its argument-error messages this way, and
/// String.Format alone is about 220 KB of Wasm that most F# programs reach only
/// through such an error path (Array.zeroCreate is one). Programs without the
/// library keep String.Format.
/// </summary>
internal sealed class FSharpCoreMessageFormatRewriter(
    ITypeFinder types,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols) : IMethodRewriteRule
{
    private const string LibraryType = "NetWasm.FSharp.Core.Messages";
    private const int ThrowWindow = 8;

    private readonly Lazy<Dictionary<string, MethodDefinitionModel>?> _helpers = new(() => FindHelpers(types, methods));

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        if (body.Method.Key.Assembly.Name != "FSharp.Core")
        {
            return body;
        }
        ImmutableArray<CilInstruction>.Builder? rewritten = null;
        for (var index = 0; index < body.Instructions.Length; index++)
        {
            var call = body.Instructions[index];
            if (call.Operation != CilOperation.Call ||
                !ThrowsSoon(body.Instructions, index) ||
                calledMethods.Resolve(call) is not { } target ||
                target.Definition.Name != "Format" ||
                symbols.Format(target.Definition.DeclaringType) != "System.String" ||
                _helpers.Value is not { } helpers ||
                !helpers.TryGetValue(SignatureKey(target.Signature.ParameterSignatureTypes), out var helper))
            {
                continue;
            }
            rewritten ??= body.Instructions.ToBuilder();
            rewritten[index] = call with
            {
                Operand = new CilOperand.MethodInstance(methodInstances.ResolveMethodInstance(
                    helper.Key.Assembly,
                    helper.Key.MetadataToken,
                    symbols.Format(helper),
                    0,
                    new CliGenericContext([], []))),
            };
        }
        return rewritten is null ? body : body with { Instructions = rewritten.ToImmutable() };
    }

    private static bool ThrowsSoon(ImmutableArray<CilInstruction> instructions, int index)
    {
        for (var next = index + 1; next < instructions.Length && next <= index + ThrowWindow; next++)
        {
            if (instructions[next].Operation == CilOperation.Throw)
            {
                return true;
            }
        }
        return false;
    }

    private static string SignatureKey(ImmutableArray<CliTypeIdentity> parameters) =>
        string.Join(",", parameters.Select(parameter => parameter.CanonicalName));

    private static Dictionary<string, MethodDefinitionModel>? FindHelpers(
        ITypeFinder types,
        IMethodRepository methods)
    {
        TypeDefinitionModel messages;
        try
        {
            messages = types.FindType(LibraryType);
        }
        catch (CompilerException)
        {
            return null;
        }
        var helpers = new Dictionary<string, MethodDefinitionModel>(StringComparer.Ordinal);
        foreach (var method in messages.Methods.Select(methods.GetMethod))
        {
            if (method.IsStatic && method.Name == "Format")
            {
                helpers[SignatureKey(method.Signature.ParameterSignatureTypes)] = method;
            }
        }
        return helpers;
    }
}
