using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata.RuntimeProvidedMembers;

internal interface IDelegateAsynchronousCallBodyBuilder
{
    CilMethodBody Build(MethodInstanceModel method);
}
