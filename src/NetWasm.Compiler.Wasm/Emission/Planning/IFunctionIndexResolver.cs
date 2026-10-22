using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

internal interface IFunctionIndexResolver
{
    // The module's shared function that begins and throws a managed exception.
    OptionalFunctionIndex ManagedThrowFunction { get; }

    int Resolve(EntityKey method);

    int Resolve(string method);

    int Resolve(MethodInstanceModel method);

    int Resolve(InteropImportKey import);
}
