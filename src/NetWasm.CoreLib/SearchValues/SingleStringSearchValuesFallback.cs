// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace System.Buffers
{
    internal sealed class SingleStringSearchValuesFallback : StringSearchValuesBase
    {
        private readonly string _value;
        private readonly StringComparison _comparisonType;

        public SingleStringSearchValuesFallback(
            string value,
            HashSet<string> uniqueValues,
            StringComparison comparisonType)
            : base(uniqueValues)
        {
            _value = value;
            _comparisonType = comparisonType;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override int IndexOfAnyMultiString(ReadOnlySpan<char> span) =>
            span.IndexOf(_value.AsSpan(), _comparisonType);
    }
}
