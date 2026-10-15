using System.Collections.Generic;
using NetWasm.Compiler.Wasm.Encoding;

namespace NetWasm.Compiler.Wasm.ModuleEncoding;

public interface IFunctionInlineAnnotationWriter
{
    void Write(IWasmBinaryWriter output, IReadOnlyList<WasmFunctionDefinition> functions, int functionImportCount);
}
