using System.Collections.Generic;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Analysis.Constants;

internal interface IConstantOperandReader
{
    // The longest producer sequence the reader recognizes.
    int MaximumLength { get; }

    ScalarConstant? Read(IReadOnlyList<CilInstruction> producers);
}
