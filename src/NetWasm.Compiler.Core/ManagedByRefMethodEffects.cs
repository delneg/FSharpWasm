using System.Collections.Immutable;

namespace NetWasm.Compiler.Core;

public sealed record ManagedByRefMethodEffects(
    ImmutableHashSet<int> WrittenArguments,
    ImmutableHashSet<int> EscapedArguments,
    ImmutableHashSet<int> ReturnedArgumentAliases)
{
    public static ManagedByRefMethodEffects Empty { get; } = new([], [], []);

    public bool RequiresOwnedStorage(int argumentIndex) =>
        WrittenArguments.Contains(argumentIndex) ||
        EscapedArguments.Contains(argumentIndex) ||
        ReturnedArgumentAliases.Contains(argumentIndex);

    public ManagedByRefMethodEffects Union(ManagedByRefMethodEffects other) => new(
        WrittenArguments.Union(other.WrittenArguments),
        EscapedArguments.Union(other.EscapedArguments),
        ReturnedArgumentAliases.Union(other.ReturnedArgumentAliases));
}

public sealed record ManagedByRefEffects(
    ImmutableDictionary<string, ManagedByRefMethodEffects> Methods)
{
    public static ManagedByRefEffects Empty { get; } = new(
        ImmutableDictionary<string, ManagedByRefMethodEffects>.Empty);
}
