namespace NetWasm.Compiler.ComponentModel.Inlining;

public interface IWasmCoreModuleInlineAnnotationEditor
{
    void Rewrite(string inputPath, string outputPath, WasmInlineAnnotationSelection selection);
}
