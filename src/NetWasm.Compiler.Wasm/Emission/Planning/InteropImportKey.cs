using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Wasm.Emission.Planning;

// Names the raw interop-ABI import of a JSImport method. Only the JSImport call
// emitters resolve it; every other caller of a JSImport uses the managed-ABI entry.
internal readonly record struct InteropImportKey(EntityKey Method);
