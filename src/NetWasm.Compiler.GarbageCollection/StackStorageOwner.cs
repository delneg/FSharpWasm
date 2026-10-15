using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public readonly record struct StackStorageOwner(bool IsArgument, int Index, CliTypeIdentity Type);
