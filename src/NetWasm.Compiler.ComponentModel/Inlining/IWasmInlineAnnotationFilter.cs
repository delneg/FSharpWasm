namespace NetWasm.Compiler.ComponentModel.Inlining;

public interface IWasmInlineAnnotationFilter
{
    byte[] Filter(byte[] source, WasmInlineAnnotationSelection selection);
}
