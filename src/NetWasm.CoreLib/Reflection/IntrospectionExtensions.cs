// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Reflection;

public static class IntrospectionExtensions
{
    // Code compiled against .NET's reference assemblies returns TypeInfo here;
    // the NetWasm compiler maps that name to System.Type.
    public static Type GetTypeInfo(this Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type;
    }
}
