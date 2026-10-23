using System;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

// Replaces calls to CoreLib's PlatformWorld switches with the selected world's values, so the
// constant-branch rules that follow remove the composition a world cannot bind before
// reachability, whatever the optimization settings.
internal sealed class PlatformWorldCallRewriter(
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols,
    PlatformWorldCapabilities world,
    ITypeFinder? types = null) : IMethodRewriteRule
{
    private const string PlatformWorldType = "System.Runtime.InteropServices.PlatformWorld";
    private readonly Lazy<bool> _fsharpProgram = new(() => IsFSharpProgram(types));

    // An F# program links FSharp.Core, whose Unit type every F# assembly uses.
    private static bool IsFSharpProgram(ITypeFinder? types)
    {
        if (types is null)
        {
            return false;
        }
        try
        {
            return types.FindType("Microsoft.FSharp.Core.Unit").Key.Assembly.Name == "FSharp.Core";
        }
        catch (CompilerException)
        {
            return false;
        }
    }

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        ArgumentNullException.ThrowIfNull(body);

        var instructions = body.Instructions.ToArray();
        var changed = false;
        for (var index = 0; index < instructions.Length; index++)
        {
            var call = instructions[index];
            if (call.Operation != CilOperation.Call ||
                calledMethods.Resolve(call) is not { } target ||
                !target.Definition.IsStatic ||
                symbols.Format(target.Definition.DeclaringType) != PlatformWorldType)
            {
                continue;
            }
            var value = target.Definition.Name switch
            {
                "ImportsReactorHost" => world.ReactorHost,
                "ImportsEventLoop" => world.EventLoop,
                "BlockingWaitDrivesEventLoop" => _fsharpProgram.Value,
                _ => (bool?)null,
            };
            if (value is not { } known)
            {
                continue;
            }
            instructions[index] = call with
            {
                Operation = CilOperation.LoadInt32,
                Operand = new CilOperand.ConstantI4(known ? 1 : 0),
            };
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }
}
