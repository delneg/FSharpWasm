// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Buffers
{
    public class SearchValues<T> where T : IEquatable<T>
    {
        private protected SearchValues()
        {
        }

        internal virtual T[] GetValues() => throw new UnreachableException();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(T value) => ContainsCore(value);

        internal virtual bool ContainsCore(T value) => throw new UnreachableException();

        internal virtual int IndexOfAny(ReadOnlySpan<T> span) => throw new UnreachableException();

        internal virtual int IndexOfAnyExcept(ReadOnlySpan<T> span) => throw new UnreachableException();

        internal virtual int LastIndexOfAny(ReadOnlySpan<T> span) => throw new UnreachableException();

        internal virtual int LastIndexOfAnyExcept(ReadOnlySpan<T> span) => throw new UnreachableException();

        internal virtual bool ContainsAny(ReadOnlySpan<T> span) => IndexOfAny(span) >= 0;

        internal virtual bool ContainsAnyExcept(ReadOnlySpan<T> span) => IndexOfAnyExcept(span) >= 0;

        internal virtual int IndexOfAnyMultiString(ReadOnlySpan<char> span) => throw new UnreachableException();
    }
}
