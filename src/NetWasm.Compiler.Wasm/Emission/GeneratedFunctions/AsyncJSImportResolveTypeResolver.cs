using NetWasm.Compiler.Core;
using NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

namespace NetWasm.Compiler.Wasm.Emission.GeneratedFunctions;

internal sealed class AsyncJSImportResolveTypeResolver(ITargetLayout layouts) :
    IAsyncJSImportResolveTypeResolver
{
    public WasmFunctionType Resolve(JavaScriptAsyncMethodBinding binding)
    {
        var result = binding.Return.ResultType;
        if (result is null)
        {
            return WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4);
        }

        if (InteropTypeClassifier.IsString(result) || InteropTypeClassifier.IsValueArray(result)
            || InteropTypeClassifier.IsJSObject(result))
        {
            return WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4, CliValueKind.I4);
        }

        var resultType = result.StackKind;
        if (resultType == CliValueKind.NativeInt)
        {
            resultType = layouts.Target.UsesMemory64
                ? CliValueKind.I8
                : CliValueKind.I4;
        }
        return WasmFunctionType.Create(CliValueKind.Void, CliValueKind.I4, resultType);
    }
}
