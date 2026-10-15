// Adapted from dotnet/runtime System.Private.CoreLib DateOnly.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace System
{
    public readonly struct DateOnly : IComparable, IComparable<DateOnly>, IEquatable<DateOnly>,
        IFormattable, IParsable<DateOnly>, ISpanFormattable, ISpanParsable<DateOnly>,
        IUtf8SpanFormattable
    {
        private readonly uint _dayNumber;

        public DateOnly(int year, int month, int day) =>
            _dayNumber = (uint)GregorianDateMath.DateToDayNumber(year, month, day);

        private DateOnly(uint dayNumber) => _dayNumber = dayNumber;

        public static DateOnly MinValue { get { return new(0); } }
        public static DateOnly MaxValue { get { return new((uint)GregorianDateMath.DaysTo10000 - 1); } }
        public int DayNumber { get { return (int)_dayNumber; } }
        public int Year { get { GetParts(out var year, out _, out _); return year; } }
        public int Month { get { GetParts(out _, out var month, out _); return month; } }
        public int Day { get { GetParts(out _, out _, out var day); return day; } }
        public DayOfWeek DayOfWeek { get { return (DayOfWeek)((_dayNumber + 1) % 7); } }
        public int DayOfYear
        {
            get
            {
                return (int)(_dayNumber - (uint)GregorianDateMath.DateToDayNumber(Year, 1, 1) + 1);
            }
        }

        public DateOnly AddDays(int value)
        {
            var dayNumber = _dayNumber + (uint)value;
            if (dayNumber >= GregorianDateMath.DaysTo10000)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            return new DateOnly(dayNumber);
        }

        public DateOnly AddMonths(int value)
        {
            if (value < -120000 || value > 120000)
            {
                throw new ArgumentOutOfRangeException("months");
            }
            var year = Year;
            var month = Month;
            var totalMonths = (year - 1) * 12 + month - 1 + value;
            if (totalMonths < 0 || totalMonths >= 9999 * 12)
            {
                throw new ArgumentOutOfRangeException("months");
            }
            year = totalMonths / 12 + 1;
            month = totalMonths % 12 + 1;
            var day = Day;
            var daysInMonth = DaysInMonth(year, month);
            if (day > daysInMonth)
            {
                day = daysInMonth;
            }
            return new DateOnly(year, month, day);
        }

        public DateOnly AddYears(int value)
        {
            if (value < -10000 || value > 10000)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
            try
            {
                return AddMonths(value * 12);
            }
            catch (ArgumentOutOfRangeException)
            {
                throw new ArgumentOutOfRangeException(nameof(value));
            }
        }

        public static DateOnly FromDayNumber(int dayNumber)
        {
            if ((uint)dayNumber >= GregorianDateMath.DaysTo10000)
            {
                throw new ArgumentOutOfRangeException(nameof(dayNumber), dayNumber, null);
            }
            return new DateOnly((uint)dayNumber);
        }

        public static int DaysInMonth(int year, int month)
        {
            var first = GregorianDateMath.DateToDayNumber(year, month, 1);
            var next = month == 12 && year == 9999
                ? GregorianDateMath.DaysTo10000
                : month == 12
                ? GregorianDateMath.DateToDayNumber(year + 1, 1, 1)
                : GregorianDateMath.DateToDayNumber(year, month + 1, 1);
            return next - first;
        }

        public static bool IsLeapYear(int year) => GregorianDateMath.IsLeapYear(year);
        public static DateOnly FromDateTime(DateTime dateTime) =>
            new((uint)(dateTime.Ticks / TimeSpan.TicksPerDay));
        public DateTime ToDateTime(TimeOnly time) => new(
            checked((long)_dayNumber * TimeSpan.TicksPerDay + time.Ticks));
        public DateTime ToDateTime(TimeOnly time, DateTimeKind kind)
        {
            if ((uint)kind > (uint)DateTimeKind.Local)
            {
                throw new ArgumentException(null, nameof(kind));
            }

            return new DateTime(
                checked((long)_dayNumber * TimeSpan.TicksPerDay + time.Ticks), kind);
        }
        public int CompareTo(DateOnly value) => _dayNumber.CompareTo(value._dayNumber);
        public int CompareTo(object? value) => value is null
            ? 1
            : value is DateOnly other
                ? CompareTo(other)
                : throw new ArgumentException();
        public bool Equals(DateOnly value) => _dayNumber == value._dayNumber;
        public override bool Equals([NotNullWhen(true)] object? value) =>
            value is DateOnly other && Equals(other);
        public override int GetHashCode() => (int)_dayNumber;

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Deconstruct(out int year, out int month, out int day) =>
            GetParts(out year, out month, out day);
        public string ToLongDateString() => ToString("D");
        public string ToShortDateString() => ToString();
        public string ToString(IFormatProvider? provider) => FormatInvariant(null, provider);
        public string ToString([StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string? format) =>
            FormatInvariant(format, null);
        public string ToString(
            [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string? format,
            IFormatProvider? provider) => FormatInvariant(format, provider);
        public override string ToString() => FormatInvariant(null, null);

        public bool TryFormat(Span<char> destination, out int charsWritten,
            [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)]
            ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
            TryFormatCore(
                MemoryMarshal.Cast<char, Utf16Char>(destination),
                out charsWritten,
                format,
                provider);

        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten,
            [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)]
            ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
            TryFormatCore(
                MemoryMarshal.Cast<byte, Utf8Char>(utf8Destination),
                out bytesWritten,
                format,
                provider);

        public static DateOnly Parse(string s) =>
            Parse(s, null, System.Globalization.DateTimeStyles.None);

        public static DateOnly Parse(string s, IFormatProvider? provider) =>
            Parse(s, provider, System.Globalization.DateTimeStyles.None);

        public static DateOnly Parse(string s, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            ArgumentNullException.ThrowIfNull(s);
            return Parse(s.AsSpan(), provider, style);
        }

        public static DateOnly Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
            Parse(s, provider, System.Globalization.DateTimeStyles.None);

        public static DateOnly Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            ValidateStyles(style);
            return InvariantDateOnlyTimeOnlyParser.TryParseDate(
                s,
                System.Globalization.DateTimeFormatInfo.GetInstance(provider),
                style,
                out var result)
                ? result
                : throw new FormatException();
        }

        public static bool TryParse([NotNullWhen(true)] string? s, out DateOnly result) =>
            TryParse(s, null, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out DateOnly result) =>
            TryParse(s, provider, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out DateOnly result)
        {
            if (s is null)
            {
                result = default;
                return false;
            }
            return TryParse(s.AsSpan(), provider, style, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, out DateOnly result) =>
            TryParse(s, null, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider,
            out DateOnly result) => TryParse(s, provider, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out DateOnly result)
        {
            ValidateStyles(style);
            return InvariantDateOnlyTimeOnlyParser.TryParseDate(
                s,
                System.Globalization.DateTimeFormatInfo.GetInstance(provider),
                style,
                out result);
        }

        public static DateOnly ParseExact(
            string s,
            [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string format) =>
            ParseExact(s, format, null, System.Globalization.DateTimeStyles.None);

        public static DateOnly ParseExact(
            string s,
            [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string format,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            ArgumentNullException.ThrowIfNull(s);
            ArgumentNullException.ThrowIfNull(format);
            return ParseExact(s.AsSpan(), format.AsSpan(), provider, style);
        }

        public static DateOnly ParseExact(
            string s,
            [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string[] formats) =>
            ParseExact(s, formats, null, System.Globalization.DateTimeStyles.None);

        public static DateOnly ParseExact(
            string s,
            [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string[] formats,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            ArgumentNullException.ThrowIfNull(s);
            return ParseExact(s.AsSpan(), formats, provider, style);
        }

        public static DateOnly ParseExact(
            ReadOnlySpan<char> s,
            [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] ReadOnlySpan<char> format,
            IFormatProvider? provider = null,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            ValidateStyles(style);
            var parseResult = TryParseExactCore(s, format, provider, style, out var result);
            return parseResult switch
            {
                InvariantDateTimeParseResult.Success => result,
                _ => throw new FormatException(),
            };
        }

        public static DateOnly ParseExact(
            ReadOnlySpan<char> s,
            [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string[] formats) =>
            ParseExact(s, formats, null, System.Globalization.DateTimeStyles.None);

        public static DateOnly ParseExact(
            ReadOnlySpan<char> s,
            [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string[] formats,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            return TryParseExact(s, formats, provider, style, out var result)
                ? result
                : throw new FormatException();
        }

        public static bool TryParseExact(
            [NotNullWhen(true)] string? s,
            [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string? format,
            out DateOnly result) =>
            TryParseExact(s, format, null, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(
            [NotNullWhen(true)] string? s,
            [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string? format,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out DateOnly result)
        {
            if (s is null || format is null)
            {
                result = default;
                return false;
            }
            return TryParseExact(s.AsSpan(), format.AsSpan(), provider, style, out result);
        }

        public static bool TryParseExact(
            [NotNullWhen(true)] string? s,
            [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string?[]? formats,
            out DateOnly result) =>
            TryParseExact(s, formats, null, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(
            [NotNullWhen(true)] string? s,
            [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string?[]? formats,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out DateOnly result)
        {
            if (s is null)
            {
                result = default;
                return false;
            }
            return TryParseExact(s.AsSpan(), formats, provider, style, out result);
        }

        public static bool TryParseExact(
            ReadOnlySpan<char> s,
            [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] ReadOnlySpan<char> format,
            out DateOnly result) => TryParseExact(s, format, null,
                System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(
            ReadOnlySpan<char> s,
            [StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] ReadOnlySpan<char> format,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style,
            out DateOnly result)
        {
            ValidateStyles(style);
            var parseResult = TryParseExactCore(s, format, provider, style, out result);
            if (parseResult == InvariantDateTimeParseResult.BadFormat)
            {
                throw new FormatException();
            }
            return parseResult == InvariantDateTimeParseResult.Success;
        }

        public static bool TryParseExact(
            ReadOnlySpan<char> s,
            [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string?[]? formats,
            out DateOnly result) => TryParseExact(s, formats, null,
                System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(
            ReadOnlySpan<char> s,
            [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string?[]? formats,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style,
            out DateOnly result)
        {
            ValidateStyles(style);
            if (formats is null)
            {
                throw new ArgumentException(null, nameof(style));
            }
            for (var index = 0; index < formats.Length; index++)
            {
                if (string.IsNullOrEmpty(formats[index]))
                {
                    throw new FormatException();
                }
            }

            var dateTimeFormat = System.Globalization.DateTimeFormatInfo.GetInstance(provider);
            for (var index = 0; index < formats.Length; index++)
            {
                var format = formats[index]!.AsSpan();
                var formatInfo = IsInvariantStandardFormat(format)
                    ? System.Globalization.DateTimeFormatInfo.InvariantInfo
                    : dateTimeFormat;
                var parseResult = InvariantDateOnlyTimeOnlyParser.TryParseDateExact(
                    s,
                    format,
                    formatInfo,
                    style,
                    out result);
                if (parseResult == InvariantDateTimeParseResult.Success)
                {
                    return true;
                }
                if (parseResult == InvariantDateTimeParseResult.BadFormat)
                {
                    break;
                }
            }

            result = default;
            return false;
        }

        public static bool operator ==(DateOnly left, DateOnly right) => left.Equals(right);
        public static bool operator !=(DateOnly left, DateOnly right) => !left.Equals(right);
        public static bool operator <(DateOnly left, DateOnly right) => left._dayNumber < right._dayNumber;
        public static bool operator <=(DateOnly left, DateOnly right) => left._dayNumber <= right._dayNumber;
        public static bool operator >(DateOnly left, DateOnly right) => left._dayNumber > right._dayNumber;
        public static bool operator >=(DateOnly left, DateOnly right) => left._dayNumber >= right._dayNumber;

        private void GetParts(out int year, out int month, out int day) =>
            GregorianDateMath.GetDateParts((int)_dayNumber, out year, out month, out day);

        private string FormatInvariant(string? format, IFormatProvider? provider)
        {
            if (string.IsNullOrEmpty(format))
            {
                format = "d";
            }

            if (format.Length == 1)
            {
                switch (format[0])
                {
                    case 'O':
                    case 'o':
                        return InvariantDateTimeText.FourDigits(Year) + "-" +
                            InvariantDateTimeText.TwoDigits(Month) + "-" +
                            InvariantDateTimeText.TwoDigits(Day);
                    case 'R':
                    case 'r':
                        return FormatCustom("ddd, dd MMM yyyy");
                    case 'd':
                        {
                            var dateTimeFormat =
                                System.Globalization.DateTimeFormatInfo.GetInstance(provider);
                            return FormatCustom(dateTimeFormat.ShortDatePattern);
                        }
                    case 'D':
                        {
                            var dateTimeFormat =
                                System.Globalization.DateTimeFormatInfo.GetInstance(provider);
                            return FormatCustom(dateTimeFormat.LongDatePattern);
                        }
                    case 'M':
                    case 'm':
                        {
                            var dateTimeFormat =
                                System.Globalization.DateTimeFormatInfo.GetInstance(provider);
                            return FormatCustom(dateTimeFormat.MonthDayPattern);
                        }
                    case 'Y':
                    case 'y':
                        {
                            var dateTimeFormat =
                                System.Globalization.DateTimeFormatInfo.GetInstance(provider);
                            return FormatCustom(dateTimeFormat.YearMonthPattern);
                        }
                    default:
                        throw new FormatException();
                }
            }

            if (!IsValidCustomDateFormat(format, throwOnError: true))
            {
                throw new FormatException();
            }
            System.Globalization.DateTimeFormatInfo.GetInstance(provider);
            return FormatCustom(format);
        }

        private string FormatCustom(string format) => InvariantDateTimePattern.Format(
            new DateTime(checked((long)_dayNumber * TimeSpan.TicksPerDay)),
            format,
            hasOffset: false,
            default,
            explicitOffset: false);

        private bool TryFormatCore<TChar>(
            Span<TChar> destination,
            out int charsWritten,
            ReadOnlySpan<char> format,
            IFormatProvider? provider)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            if (format.Length == 0)
            {
                format = "d";
            }

            if (format.Length == 1)
            {
                switch (format[0])
                {
                    case 'O':
                    case 'o':
                        return TryFormatO(destination, out charsWritten);
                    case 'R':
                    case 'r':
                        return TryFormatR(destination, out charsWritten);
                    case 'd':
                        format = System.Globalization.DateTimeFormatInfo
                            .GetInstance(provider).ShortDatePattern;
                        break;
                    case 'D':
                        format = System.Globalization.DateTimeFormatInfo
                            .GetInstance(provider).LongDatePattern;
                        break;
                    case 'M':
                    case 'm':
                        format = System.Globalization.DateTimeFormatInfo
                            .GetInstance(provider).MonthDayPattern;
                        break;
                    case 'Y':
                    case 'y':
                        format = System.Globalization.DateTimeFormatInfo
                            .GetInstance(provider).YearMonthPattern;
                        break;
                    default:
                        throw new FormatException();
                }
            }
            else
            {
                if (!IsValidCustomDateFormat(format, throwOnError: false))
                {
                    throw new FormatException();
                }
                System.Globalization.DateTimeFormatInfo.GetInstance(provider);
            }

            Span<char> initialBuffer = stackalloc char[64];
            var output = new Text.ValueStringBuilder(initialBuffer);
            try
            {
                InvariantDateTimePattern.AppendFormat(
                    ref output,
                    new DateTime(checked((long)_dayNumber * TimeSpan.TicksPerDay)),
                    format,
                    hasOffset: false,
                    default,
                    explicitOffset: false);
                return TryCopyOutput(ref output, destination, out charsWritten);
            }
            finally
            {
                output.Dispose();
            }
        }

        private bool TryFormatO<TChar>(Span<TChar> destination, out int charsWritten)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            if (destination.Length < 10)
            {
                charsWritten = 0;
                return false;
            }

            GetParts(out var year, out var month, out var day);
            WriteFourDigits(destination, 0, year);
            destination[4] = TChar.CastFrom('-');
            WriteTwoDigits(destination, 5, month);
            destination[7] = TChar.CastFrom('-');
            WriteTwoDigits(destination, 8, day);
            charsWritten = 10;
            return true;
        }

        private bool TryFormatR<TChar>(Span<TChar> destination, out int charsWritten)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            if (destination.Length < 16)
            {
                charsWritten = 0;
                return false;
            }

            GetParts(out var year, out var month, out var day);
            WriteAscii(destination, 0,
                InvariantDateTimePattern.GetDayName(DayOfWeek, abbreviated: true));
            destination[3] = TChar.CastFrom(',');
            destination[4] = TChar.CastFrom(' ');
            WriteTwoDigits(destination, 5, day);
            destination[7] = TChar.CastFrom(' ');
            WriteAscii(destination, 8,
                InvariantDateTimePattern.GetMonthName(month, abbreviated: true));
            destination[11] = TChar.CastFrom(' ');
            WriteFourDigits(destination, 12, year);
            charsWritten = 16;
            return true;
        }

        private static bool TryCopyOutput<TChar>(
            ref Text.ValueStringBuilder output,
            Span<TChar> destination,
            out int charsWritten)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            var source = output.AsSpan();
            if (typeof(TChar) == typeof(Utf8Char))
            {
                var bytes = MemoryMarshal.Cast<TChar, byte>(destination);
                var byteCount = Text.Encoding.UTF8.GetByteCount(source);
                if (bytes.Length < byteCount)
                {
                    charsWritten = 0;
                    return false;
                }

                charsWritten = Text.Encoding.UTF8.GetBytes(source, bytes);
                return true;
            }

            if (destination.Length < source.Length)
            {
                charsWritten = 0;
                return false;
            }

            for (var index = 0; index < source.Length; index++)
            {
                destination[index] = TChar.CastFrom(source[index]);
            }
            charsWritten = source.Length;
            return true;
        }

        private static void WriteAscii<TChar>(
            Span<TChar> destination,
            int offset,
            string value)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            for (var index = 0; index < value.Length; index++)
            {
                destination[offset + index] = TChar.CastFrom(value[index]);
            }
        }

        private static void WriteTwoDigits<TChar>(
            Span<TChar> destination,
            int offset,
            int value)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            destination[offset] = TChar.CastFrom('0' + value / 10);
            destination[offset + 1] = TChar.CastFrom('0' + value % 10);
        }

        private static void WriteFourDigits<TChar>(
            Span<TChar> destination,
            int offset,
            int value)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            destination[offset] = TChar.CastFrom('0' + value / 1000);
            destination[offset + 1] = TChar.CastFrom('0' + value / 100 % 10);
            destination[offset + 2] = TChar.CastFrom('0' + value / 10 % 10);
            destination[offset + 3] = TChar.CastFrom('0' + value % 10);
        }

        private static bool IsValidCustomDateFormat(ReadOnlySpan<char> format, bool throwOnError)
        {
            for (var index = 0; index < format.Length; index++)
            {
                switch (format[index])
                {
                    case '\\':
                        if (++index < format.Length) continue;
                        break;
                    case '\'':
                    case '"':
                        {
                            var quote = format[index++];
                            while (index < format.Length && format[index] != quote) index++;
                            if (index < format.Length) continue;
                            break;
                        }
                    case ':':
                    case 't':
                    case 'f':
                    case 'F':
                    case 'h':
                    case 'H':
                    case 'm':
                    case 's':
                    case 'z':
                    case 'K':
                        if (!throwOnError) return false;
                        throw new FormatException();
                    default:
                        continue;
                }

                if (!throwOnError) return false;
                throw new FormatException();
            }

            return true;
        }

        private static InvariantDateTimeParseResult TryParseExactCore(
            ReadOnlySpan<char> s,
            ReadOnlySpan<char> format,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style,
            out DateOnly result)
        {
            var formatInfo = IsInvariantStandardFormat(format)
                ? System.Globalization.DateTimeFormatInfo.InvariantInfo
                : System.Globalization.DateTimeFormatInfo.GetInstance(provider);
            return InvariantDateOnlyTimeOnlyParser.TryParseDateExact(
                s,
                format,
                formatInfo,
                style,
                out result);
        }

        private static bool IsInvariantStandardFormat(ReadOnlySpan<char> format) =>
            format.Length == 1 && (format[0] | 0x20) is 'o' or 'r';

        private static void ValidateStyles(System.Globalization.DateTimeStyles style)
        {
            if ((style & ~System.Globalization.DateTimeStyles.AllowWhiteSpaces) != 0)
            {
                throw new ArgumentException(null, nameof(style));
            }
        }

    }

    public enum DayOfWeek
    {
        Sunday,
        Monday,
        Tuesday,
        Wednesday,
        Thursday,
        Friday,
        Saturday,
    }
}
