// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Buffers
{
    internal abstract class StringSearchValuesBase : SearchValues<string>
    {
        private readonly HashSet<string> _uniqueValues;

        protected StringSearchValuesBase(HashSet<string> uniqueValues)
        {
            _uniqueValues = uniqueValues;
        }

        internal override bool ContainsCore(string value) =>
            _uniqueValues.Contains(value);

        internal override string[] GetValues()
        {
            var values = new string[_uniqueValues.Count];
            _uniqueValues.CopyTo(values);
            return values;
        }

        internal sealed override int IndexOfAny(ReadOnlySpan<string> span) =>
            Search(span, except: false, reverse: false);

        internal sealed override int IndexOfAnyExcept(ReadOnlySpan<string> span) =>
            Search(span, except: true, reverse: false);

        internal sealed override int LastIndexOfAny(ReadOnlySpan<string> span) =>
            Search(span, except: false, reverse: true);

        internal sealed override int LastIndexOfAnyExcept(ReadOnlySpan<string> span) =>
            Search(span, except: true, reverse: true);

        private int Search(ReadOnlySpan<string> span, bool except, bool reverse)
        {
            var index = reverse ? span.Length - 1 : 0;
            var end = reverse ? -1 : span.Length;
            var step = reverse ? -1 : 1;

            for (; index != end; index += step)
            {
                if (ContainsCore(span[index]) != except)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
