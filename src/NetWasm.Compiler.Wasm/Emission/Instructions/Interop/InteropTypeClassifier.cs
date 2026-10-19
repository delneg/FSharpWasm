using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Instructions.Interop;

internal static class InteropTypeClassifier
{
    public static bool IsString(CliTypeIdentity type) =>
        type.CanonicalName == "primitive:string";

    public static bool IsValueArray(CliTypeIdentity type) =>
        JavaScriptValueArray.IsSupported(type);

    /// <summary>A string, number array, or JSObject: a value that crosses an export as a host handle.</summary>
    public static bool IsJavaScriptReference(CliTypeIdentity type) =>
        IsString(type) || IsValueArray(type) || IsJSObject(type);

    public static bool IsJSObject(CliTypeIdentity type) =>
        type.FullName == "System.Runtime.InteropServices.JavaScript.JSObject";

    public static bool IsSubscription(CliTypeIdentity type) =>
        type.FullName == "System.Runtime.InteropServices.JavaScript.JSSubscription";

    public static bool IsHostObject(CliTypeIdentity type) =>
        IsJSObject(type) || IsSubscription(type);
}
