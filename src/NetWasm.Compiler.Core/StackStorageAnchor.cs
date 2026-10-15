namespace NetWasm.Compiler.Core;

public readonly record struct StackStorageAnchor(StackStorageLocation Location, CliTypeIdentity BackingType);
