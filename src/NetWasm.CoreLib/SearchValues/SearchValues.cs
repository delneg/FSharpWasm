// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Buffers
{
    public static class SearchValues
    {
        public static SearchValues<byte> Create(params ReadOnlySpan<byte> values)
        {
            if (values.IsEmpty)
            {
                return new EmptySearchValues<byte>();
            }

            if (values.Length == 1)
            {
                return new Any1SearchValues<byte>(values);
            }

            if (TryGetSingleRange(values, out var minimum, out var maximum))
            {
                return new RangeByteSearchValues(minimum, maximum);
            }

            // TODO(netwasm-simd)
            return values.Length switch
            {
                2 => new Any2SearchValues<byte>(values),
                3 => new Any3SearchValues<byte>(values),
                4 => new Any4SearchValues<byte>(values),
                5 => new Any5SearchValues<byte>(values),
                _ => new AnyByteSearchValues(values),
            };
        }

        public static SearchValues<char> Create(params ReadOnlySpan<char> values)
        {
            if (values.IsEmpty)
            {
                return new EmptySearchValues<char>();
            }

            if (values.Length == 1)
            {
                return new Any1SearchValues<char>(values);
            }

            if (TryGetSingleRange(values, out var minimum, out var maximum))
            {
                return new RangeCharSearchValues(minimum, maximum);
            }

            // TODO(netwasm-simd)
            return values.Length switch
            {
                2 => new Any2SearchValues<char>(values),
                3 => new Any3SearchValues<char>(values),
                4 => new Any4SearchValues<char>(values),
                5 => new Any5SearchValues<char>(values),
                _ => new BitmapCharSearchValues(values, maximum),
            };
        }

        public static SearchValues<string> Create(
            ReadOnlySpan<string> values,
            StringComparison comparisonType)
        {
            if (comparisonType is not StringComparison.Ordinal and not StringComparison.OrdinalIgnoreCase)
            {
                throw new ArgumentException();
            }

            return StringSearchValues.Create(
                values,
                comparisonType == StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryGetSingleRange(
            ReadOnlySpan<byte> values,
            out byte minimum,
            out byte maximum)
        {
            minimum = values[0];
            maximum = values[0];
            for (var index = 1; index < values.Length; index++)
            {
                var value = values[index];
                if (value < minimum) minimum = value;
                if (value > maximum) maximum = value;
            }

            var range = maximum - minimum + 1;
            if (range > values.Length)
            {
                return false;
            }

            Span<bool> seen = stackalloc bool[256];
            seen = seen.Slice(0, range);
            seen.Clear();
            for (var index = 0; index < values.Length; index++)
            {
                seen[values[index] - minimum] = true;
            }

            for (var index = 0; index < seen.Length; index++)
            {
                if (!seen[index]) return false;
            }

            return true;
        }

        private static bool TryGetSingleRange(
            ReadOnlySpan<char> values,
            out char minimum,
            out char maximum)
        {
            minimum = values[0];
            maximum = values[0];
            for (var index = 1; index < values.Length; index++)
            {
                var value = values[index];
                if (value < minimum) minimum = value;
                if (value > maximum) maximum = value;
            }

            var range = maximum - minimum + 1;
            if (range > values.Length)
            {
                return false;
            }

            Span<bool> seen = range <= 256 ? stackalloc bool[256] : new bool[range];
            seen = seen.Slice(0, range);
            seen.Clear();
            for (var index = 0; index < values.Length; index++)
            {
                seen[values[index] - minimum] = true;
            }

            for (var index = 0; index < seen.Length; index++)
            {
                if (!seen[index]) return false;
            }

            return true;
        }
    }
}
