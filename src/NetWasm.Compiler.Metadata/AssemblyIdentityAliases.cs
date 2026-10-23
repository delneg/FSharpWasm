using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.Metadata;

internal readonly record struct AssemblyIdentityAliases
{
    private readonly ImmutableDictionary<string, string>? _aliases;

    public AssemblyIdentityAliases(ImmutableDictionary<string, string> aliases)
    {
        _aliases = aliases ?? throw new ArgumentNullException(nameof(aliases));
    }

    public static AssemblyIdentityAliases Empty { get; } =
        new(ImmutableDictionary<string, string>.Empty);

    public AssemblyIdentity Canonicalize(AssemblyIdentity identity) =>
        _aliases?.TryGetValue(identity.Name, out var target) == true
            ? new AssemblyIdentity(target)
            : identity;

    // Names that .NET's reference assemblies define as their own types but
    // NetWasm.CoreLib folds into another. Only references through an aliased
    // assembly (netstandard, System.Runtime) are mapped. TypeInfo derives from
    // Type in .NET, and CoreLib's one Type object serves as both.
    private static readonly ImmutableDictionary<string, string> s_typeAliases =
        ImmutableDictionary.CreateRange(
            StringComparer.Ordinal,
            [new KeyValuePair<string, string>("System.Reflection.TypeInfo", "System.Type")]);

    // An alias entry "tolerate:<assembly>" marks a library whose methods that
    // use members NetWasm lacks compile to throwing bodies (see CilDecoder).
    public const string TolerantPrefix = "tolerate:";

    public bool IsTolerant(AssemblyIdentity identity) =>
        _aliases?.ContainsKey(TolerantPrefix + identity.Name) == true;

    public string CanonicalizeTypeName(AssemblyIdentity identity, string fullName) =>
        _aliases?.ContainsKey(identity.Name) == true &&
        s_typeAliases.TryGetValue(fullName, out var target)
            ? target
            : fullName;
}
