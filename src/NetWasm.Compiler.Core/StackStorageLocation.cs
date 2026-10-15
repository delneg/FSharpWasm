namespace NetWasm.Compiler.Core;

public enum StackStorageLocationKind
{
    EvaluationStack,
    Local,
}

public readonly record struct StackStorageLocation(StackStorageLocationKind Kind, int Index);
