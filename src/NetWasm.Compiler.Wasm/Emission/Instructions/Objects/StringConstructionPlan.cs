namespace NetWasm.Compiler.Wasm.Emission.Instructions.Objects;

internal enum StringConstructionSourceKind
{
    RepeatedCharacter,
    CharacterArray,
    CharacterSpan,
}

internal sealed record StringConstructionPlan(
    StringConstructionSourceKind SourceKind,
    int? StartArgumentIndex,
    int? LengthArgumentIndex);
