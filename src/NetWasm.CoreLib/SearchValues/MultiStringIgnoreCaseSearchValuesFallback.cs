// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Buffers
{
    internal sealed class MultiStringIgnoreCaseSearchValuesFallback : StringSearchValuesBase
    {
        private readonly string[] _values;

        public MultiStringIgnoreCaseSearchValuesFallback(HashSet<string> uniqueValues)
            : base(uniqueValues)
        {
            _values = new string[uniqueValues.Count];
            uniqueValues.CopyTo(_values);
        }

        internal override int IndexOfAnyMultiString(ReadOnlySpan<char> span)
        {
            for (var index = 0; index < span.Length; index++)
            {
                var remaining = span.Slice(index);
                foreach (string value in _values)
                {
                    if (remaining.StartsWith(value, StringComparison.OrdinalIgnoreCase))
                    {
                        return index;
                    }
                }
            }

            return -1;
        }
    }
}
