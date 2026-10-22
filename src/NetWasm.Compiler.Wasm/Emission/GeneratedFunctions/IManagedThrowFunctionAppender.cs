using System.Collections.Generic;
using NetWasm.Compiler.Wasm.Emission.Planning;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal interface IManagedThrowFunctionAppender
{
    void Append(
        IList<WasmFunctionDefinition> functions,
        OptionalFunctionIndex throwFunctionIndex,
        OptionalFunctionIndex rethrowFunctionIndex);
}
