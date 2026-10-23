using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

/// <summary>One rewrite of a method body before its emission; the compiler runs them in order.</summary>
public interface IMethodRewriteRule
{
    CilMethodBody Rewrite(CilMethodBody body);
}
