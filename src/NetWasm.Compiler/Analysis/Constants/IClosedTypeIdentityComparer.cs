using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal interface IClosedTypeIdentityComparer
{
    bool? Compare(CliTypeIdentity left, CliTypeIdentity right);
}
