// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

namespace System.Buffers
{
    internal static class StringSearchValues
    {
        public static SearchValues<string> Create(
            ReadOnlySpan<string> values,
            bool ignoreCase)
        {
            if (values.IsEmpty)
            {
                return new EmptySearchValues<string>();
            }

            var uniqueValues = new HashSet<string>(
                values.Length,
                ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

            foreach (string value in values)
            {
                ArgumentNullException.ThrowIfNull(value, nameof(values));
                uniqueValues.Add(value);
            }

            var normalizedValues = new string[uniqueValues.Count];
            uniqueValues.CopyTo(normalizedValues);

            if (normalizedValues.Length == 1)
            {
                return new SingleStringSearchValuesFallback(
                    normalizedValues[0],
                    uniqueValues,
                    ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            }

            if (uniqueValues.Contains(string.Empty))
            {
                return new SingleStringSearchValuesFallback(
                    string.Empty,
                    uniqueValues,
                    ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            }

            if (ignoreCase)
            {
                return new MultiStringIgnoreCaseSearchValuesFallback(uniqueValues);
            }

            Array.Sort(normalizedValues, static (left, right) => left.Length.CompareTo(right.Length));

            HashSet<string>? unreachableValues = null;
            var ahoCorasickBuilder = new AhoCorasickBuilder(
                normalizedValues,
                ref unreachableValues);

            try
            {
                var normalizedValueCount = normalizedValues.Length;
                if (unreachableValues is not null)
                {
                    normalizedValueCount = RemoveUnreachableValues(
                        normalizedValues,
                        unreachableValues);
                }

                if (normalizedValueCount == 1)
                {
                    return new SingleStringSearchValuesFallback(
                        normalizedValues[0],
                        uniqueValues,
                        StringComparison.Ordinal);
                }

                // TODO(netwasm-simd)
                return new StringSearchValuesAhoCorasick(
                    ahoCorasickBuilder.Build(),
                    uniqueValues);
            }
            finally
            {
                ahoCorasickBuilder.Dispose();
            }
        }

        private static int RemoveUnreachableValues(
            string[] values,
            HashSet<string> unreachableValues)
        {
            var newCount = 0;
            foreach (string value in values)
            {
                if (!unreachableValues.Contains(value))
                {
                    values[newCount++] = value;
                }
            }

            return newCount;
        }
    }
}
