using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis;

/// <summary>The method instance a call instruction names, when it can be known.</summary>
public interface ICalledMethodResolver
{
    MethodInstanceModel? Resolve(CilInstruction instruction);
}
