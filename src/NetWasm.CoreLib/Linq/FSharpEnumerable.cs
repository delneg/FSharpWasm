// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Linq;

// FSharp.Core implements Seq.empty and Seq.replicate with these two LINQ
// members. LINQ itself is the optional NetWasm.System.Linq package, so CoreLib
// keeps just these, internal, for F# programs that do not reference it. The
// closed-world compiler binds FSharp.Core's [netstandard] reference here.
internal static class Enumerable
{
    public static IEnumerable<TResult> Empty<TResult>() => EmptySequence<TResult>.Instance;

    public static IEnumerable<TResult> Repeat<TResult>(TResult element, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        var items = new TResult[count];
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = element;
        }
        return items;
    }

    private static class EmptySequence<TResult>
    {
        public static readonly TResult[] Instance = [];
    }
}
