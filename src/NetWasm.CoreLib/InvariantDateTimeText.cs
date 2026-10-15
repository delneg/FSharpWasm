// Adapted from dotnet/runtime System.Private.CoreLib date/time formatting rules.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    internal static class InvariantDateTimeText
    {
        internal const string Rfc3339Format = "yyyy-MM-ddTHH:mm:ss.FFFFFFFK";

        internal static bool TryReadDigits(string value, int offset, int count, out int result)
        {
            result = 0;
            if (offset < 0 || count < 1 || offset > value.Length - count)
            {
                return false;
            }

            for (var index = 0; index < count; index++)
            {
                var digit = value[offset + index] - '0';
                if ((uint)digit > 9)
                {
                    result = 0;
                    return false;
                }
                result = result * 10 + digit;
            }
            return true;
        }

        internal static bool TryReadFraction(
            string value,
            int offset,
            int end,
            out long fractionTicks)
        {
            fractionTicks = 0;
            var count = end - offset;
            if (count < 1 || count > 7)
            {
                return false;
            }

            for (var index = offset; index < end; index++)
            {
                var digit = value[index] - '0';
                if ((uint)digit > 9)
                {
                    fractionTicks = 0;
                    return false;
                }
                fractionTicks = fractionTicks * 10 + digit;
            }
            for (var index = count; index < 7; index++)
            {
                fractionTicks *= 10;
            }
            return true;
        }

        internal static string TwoDigits(int value) => value.ToString().PadLeft(2, '0');

        internal static string FourDigits(int value) => value.ToString().PadLeft(4, '0');

        internal static string Fraction(long ticks)
        {
            var fraction = ticks % TimeSpan.TicksPerSecond;
            if (fraction == 0)
            {
                return string.Empty;
            }
            if (fraction < 0)
            {
                fraction = -fraction;
            }
            var digits = fraction.ToString().PadLeft(7, '0');
            var length = digits.Length;
            while (length > 0 && digits[length - 1] == '0')
            {
                length--;
            }
            return "." + digits.Substring(0, length);
        }

        internal static string FixedFraction(long ticks)
        {
            var fraction = ticks % TimeSpan.TicksPerSecond;
            if (fraction == 0)
            {
                return string.Empty;
            }
            if (fraction < 0)
            {
                fraction = -fraction;
            }
            return "." + fraction.ToString().PadLeft(7, '0');
        }

        internal static string FormatRfc3339(DateTime value, string suffix) =>
            FourDigits(value.Year) + "-"
            + TwoDigits(value.Month) + "-"
            + TwoDigits(value.Day) + "T"
            + TwoDigits(value.Hour) + ":"
            + TwoDigits(value.Minute) + ":"
            + TwoDigits(value.Second)
            + Fraction(value.Ticks)
            + suffix;

        internal static bool TryParseIso(
            string value,
            char dateTimeSeparator,
            bool includeSeconds,
            out DateTime result)
        {
            result = default;
            var expectedLength = includeSeconds ? 19 : 16;
            if (value.Length != expectedLength ||
                !TryReadDigits(value, 0, 4, out var year) ||
                !HasSeparator(value, 4, '-') ||
                !TryReadDigits(value, 5, 2, out var month) ||
                !HasSeparator(value, 7, '-') ||
                !TryReadDigits(value, 8, 2, out var day) ||
                !HasSeparator(value, 10, dateTimeSeparator) ||
                !TryReadDigits(value, 11, 2, out var hour) ||
                !HasSeparator(value, 13, ':') ||
                !TryReadDigits(value, 14, 2, out var minute) ||
                includeSeconds &&
                (!HasSeparator(value, 16, ':') ||
                 !TryReadDigits(value, 17, 2, out _)))
            {
                return false;
            }
            var second = 0;
            if (includeSeconds && !TryReadDigits(value, 17, 2, out second))
            {
                return false;
            }
            return TryCreateDateTime(year, month, day, hour, minute, second, out result);
        }

        internal static bool TryParseGeneral(
            string value,
            bool includeSeconds,
            out DateTime result)
        {
            result = default;
            var expectedLength = includeSeconds ? 19 : 16;
            if (value.Length != expectedLength ||
                !TryReadDigits(value, 0, 2, out var month) ||
                !HasSeparator(value, 2, '/') ||
                !TryReadDigits(value, 3, 2, out var day) ||
                !HasSeparator(value, 5, '/') ||
                !TryReadDigits(value, 6, 4, out var year) ||
                !HasSeparator(value, 10, ' ') ||
                !TryReadDigits(value, 11, 2, out var hour) ||
                !HasSeparator(value, 13, ':') ||
                !TryReadDigits(value, 14, 2, out var minute) ||
                includeSeconds &&
                (!HasSeparator(value, 16, ':') ||
                 !TryReadDigits(value, 17, 2, out _)))
            {
                return false;
            }
            var second = 0;
            if (includeSeconds && !TryReadDigits(value, 17, 2, out second))
            {
                return false;
            }
            return TryCreateDateTime(year, month, day, hour, minute, second, out result);
        }

        private static bool TryCreateDateTime(
            int year,
            int month,
            int day,
            int hour,
            int minute,
            int second,
            out DateTime result)
        {
            result = default;
            try
            {
                result = new DateTime(
                    year,
                    month,
                    day,
                    hour,
                    minute,
                    second,
                    DateTimeKind.Unspecified);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        internal static bool HasSeparator(string value, int offset, char separator) =>
            (uint)offset < (uint)value.Length && value[offset] == separator;
    }
}
