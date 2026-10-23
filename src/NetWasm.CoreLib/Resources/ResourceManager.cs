// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;
using System.Reflection;

namespace System.Resources;

// NetWasm deploys no manifest resources. Libraries that look their messages up
// through a ResourceManager (FSharp.Core's SR does) get the resource name back,
// which keeps their error paths working with a terse message.
public class ResourceManager
{
    public ResourceManager(string baseName, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(baseName);
        ArgumentNullException.ThrowIfNull(assembly);
        BaseName = baseName;
    }

    public virtual string BaseName { get; }

    public virtual string? GetString(string name) => GetString(name, null);

    public virtual string? GetString(string name, CultureInfo? culture)
    {
        ArgumentNullException.ThrowIfNull(name);
        return name;
    }

    public virtual object? GetObject(string name) => GetObject(name, null);

    public virtual object? GetObject(string name, CultureInfo? culture)
    {
        ArgumentNullException.ThrowIfNull(name);
        return null;
    }
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class NeutralResourcesLanguageAttribute : Attribute
{
    public NeutralResourcesLanguageAttribute(string cultureName)
    {
        ArgumentNullException.ThrowIfNull(cultureName);
        CultureName = cultureName;
    }

    public string CultureName { get; }
}
