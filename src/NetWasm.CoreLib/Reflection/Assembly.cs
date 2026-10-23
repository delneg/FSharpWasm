// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection;

// A NetWasm program is one closed-world image, so every type reports the same
// assembly. It carries no metadata: only what resource lookups need.
public class Assembly
{
    internal static readonly Assembly ClosedWorld = new();

    private protected Assembly()
    {
    }

    public virtual string? FullName => "NetWasm.ClosedWorld";

    public virtual bool ReflectionOnly => false;

    public virtual string[] GetManifestResourceNames() => [];

    public virtual IO.Stream? GetManifestResourceStream(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        return null;
    }

    public override string ToString() => FullName ?? string.Empty;
}
