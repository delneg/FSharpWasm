// Adapted from dotnet/runtime System.Private.CoreLib TimeOnly.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;

namespace System
{
    public readonly struct TimeOnly : IComparable, IComparable<TimeOnly>, IEquatable<TimeOnly>,
        IFormattable, IParsable<TimeOnly>, ISpanFormattable, ISpanParsable<TimeOnly>,
        IUtf8SpanFormattable
    {
        private readonly long _ticks;

        public TimeOnly(int hour, int minute) : this(hour, minute, 0, 0)
        {
        }
        public TimeOnly(int hour, int minute, int second) : this(hour, minute, second, 0)
        {
        }
        public TimeOnly(int hour, int minute, int second, int millisecond)
        {
            if ((uint)hour >= 24)
            {
                throw new ArgumentOutOfRangeException(nameof(hour));
            }
            if ((uint)minute >= 60)
            {
                throw new ArgumentOutOfRangeException(nameof(minute));
            }
            if ((uint)second >= 60)
            {
                throw new ArgumentOutOfRangeException(nameof(second));
            }
            if ((uint)millisecond >= 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(millisecond));
            }
            _ticks = hour * TimeSpan.TicksPerHour + minute * TimeSpan.TicksPerMinute
                + second * TimeSpan.TicksPerSecond
                + millisecond * TimeSpan.TicksPerMillisecond;
        }

        public TimeOnly(int hour, int minute, int second, int millisecond, int microsecond)
        {
            if ((uint)hour >= 24)
            {
                throw new ArgumentOutOfRangeException(nameof(hour));
            }
            if ((uint)minute >= 60)
            {
                throw new ArgumentOutOfRangeException(nameof(minute));
            }
            if ((uint)second >= 60)
            {
                throw new ArgumentOutOfRangeException(nameof(second));
            }
            if ((uint)millisecond >= 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(millisecond));
            }
            if ((uint)microsecond >= 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(microsecond));
            }
            _ticks = hour * TimeSpan.TicksPerHour + minute * TimeSpan.TicksPerMinute
                + second * TimeSpan.TicksPerSecond
                + millisecond * TimeSpan.TicksPerMillisecond
                + microsecond * TimeSpan.TicksPerMicrosecond;
        }

        public TimeOnly(long ticks)
        {
            if ((ulong)ticks >= (ulong)TimeSpan.TicksPerDay)
            {
                throw new ArgumentOutOfRangeException(nameof(ticks));
            }
            _ticks = ticks;
        }
        public static TimeOnly MinValue { get { return new(0); } }
        public static TimeOnly MaxValue { get { return new(TimeSpan.TicksPerDay - 1); } }
        public long Ticks { get { return _ticks; } }
        public int Hour { get { return (int)(_ticks / TimeSpan.TicksPerHour); } }
        public int Minute { get { return (int)(_ticks / TimeSpan.TicksPerMinute % 60); } }
        public int Second { get { return (int)(_ticks / TimeSpan.TicksPerSecond % 60); } }
        public int Millisecond { get { return (int)(_ticks / TimeSpan.TicksPerMillisecond % 1000); } }
        public int Microsecond { get { return (int)(_ticks / TimeSpan.TicksPerMicrosecond % 1000); } }
        public int Nanosecond { get { return (int)(_ticks % TimeSpan.TicksPerMicrosecond) * 100; } }

        public TimeOnly Add(TimeSpan value) => AddTicks(value.Ticks);
        public TimeOnly Add(TimeSpan value, out int wrappedDays) =>
            AddTicks(value.Ticks, out wrappedDays);
        public TimeOnly AddHours(double value) => AddTicks(
            (long)(value * TimeSpan.TicksPerHour));
        public TimeOnly AddHours(double value, out int wrappedDays) => AddTicks(
            (long)(value * TimeSpan.TicksPerHour), out wrappedDays);
        public TimeOnly AddMinutes(double value) => AddTicks(
            (long)(value * TimeSpan.TicksPerMinute));
        public TimeOnly AddMinutes(double value, out int wrappedDays) => AddTicks(
            (long)(value * TimeSpan.TicksPerMinute), out wrappedDays);

        public bool IsBetween(TimeOnly start, TimeOnly end)
        {
            var time = (ulong)_ticks;
            var startTicks = (ulong)start._ticks;
            var endTicks = (ulong)end._ticks;

            return startTicks <= endTicks
                ? time - startTicks < endTicks - startTicks
                : time - endTicks >= startTicks - endTicks;
        }

        public static TimeOnly FromTicks(long ticks)
        {
            if ((ulong)ticks >= (ulong)TimeSpan.TicksPerDay)
            {
                throw new ArgumentOutOfRangeException();
            }
            return new TimeOnly(ticks);
        }
        public static TimeOnly FromTimeSpan(TimeSpan timeSpan) => new(timeSpan.Ticks);
        public static TimeOnly FromDateTime(DateTime dateTime) =>
            FromTicks(dateTime.TimeOfDay.Ticks);
        public TimeSpan ToTimeSpan() => new(_ticks);

        public int CompareTo(TimeOnly value) => _ticks.CompareTo(value._ticks);
        public int CompareTo(object? value) => value is null
            ? 1
            : value is TimeOnly other
                ? CompareTo(other)
                : throw new ArgumentException();
        public bool Equals(TimeOnly value) => _ticks == value._ticks;
        public override bool Equals([NotNullWhen(true)] object? value) =>
            value is TimeOnly other && Equals(other);
        public override int GetHashCode() => unchecked((int)_ticks) ^ (int)(_ticks >> 32);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Deconstruct(out int hour, out int minute)
        {
            hour = Hour;
            minute = Minute;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Deconstruct(out int hour, out int minute, out int second)
        {
            hour = Hour;
            minute = Minute;
            second = Second;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Deconstruct(out int hour, out int minute, out int second, out int millisecond)
        {
            hour = Hour;
            minute = Minute;
            second = Second;
            millisecond = Millisecond;
        }

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void Deconstruct(
            out int hour, out int minute, out int second, out int millisecond, out int microsecond)
        {
            hour = Hour;
            minute = Minute;
            second = Second;
            millisecond = Millisecond;
            microsecond = Microsecond;
        }
        public string ToLongTimeString() => ToString("T");
        public string ToShortTimeString() => ToString();
        public string ToString(IFormatProvider? provider) => FormatInvariant(null, provider);
        public string ToString([StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string? format) =>
            FormatInvariant(format, null);
        public string ToString(
            [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string? format,
            IFormatProvider? provider) => FormatInvariant(format, provider);
        public override string ToString() => FormatInvariant(null, null);

        public bool TryFormat(Span<char> destination, out int charsWritten,
            [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)]
            ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
            TryFormatCore(
                MemoryMarshal.Cast<char, Utf16Char>(destination),
                out charsWritten,
                format,
                provider);

        public bool TryFormat(Span<byte> utf8Destination, out int bytesWritten,
            [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)]
            ReadOnlySpan<char> format = default(System.ReadOnlySpan<char>), IFormatProvider? provider = null) =>
            TryFormatCore(
                MemoryMarshal.Cast<byte, Utf8Char>(utf8Destination),
                out bytesWritten,
                format,
                provider);

        public static TimeOnly Parse(string s) =>
            Parse(s, null, System.Globalization.DateTimeStyles.None);

        public static TimeOnly Parse(string s, IFormatProvider? provider) =>
            Parse(s, provider, System.Globalization.DateTimeStyles.None);

        public static TimeOnly Parse(string s, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            ArgumentNullException.ThrowIfNull(s);
            return Parse(s.AsSpan(), provider, style);
        }

        public static TimeOnly Parse(ReadOnlySpan<char> s, IFormatProvider? provider) =>
            Parse(s, provider, System.Globalization.DateTimeStyles.None);

        public static TimeOnly Parse(ReadOnlySpan<char> s, IFormatProvider? provider = null,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            ValidateStyles(style);
            return InvariantDateOnlyTimeOnlyParser.TryParseTime(
                s,
                System.Globalization.DateTimeFormatInfo.GetInstance(provider),
                style,
                out var result)
                ? result
                : throw new FormatException();
        }

        public static bool TryParse([NotNullWhen(true)] string? s, out TimeOnly result) =>
            TryParse(s, null, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out TimeOnly result) =>
            TryParse(s, provider, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out TimeOnly result)
        {
            if (s is null)
            {
                result = default;
                return false;
            }
            return TryParse(s.AsSpan(), provider, style, out result);
        }

        public static bool TryParse(ReadOnlySpan<char> s, out TimeOnly result) =>
            TryParse(s, null, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider,
            out TimeOnly result) => TryParse(s, provider, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParse(ReadOnlySpan<char> s, IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out TimeOnly result)
        {
            ValidateStyles(style);
            return InvariantDateOnlyTimeOnlyParser.TryParseTime(
                s,
                System.Globalization.DateTimeFormatInfo.GetInstance(provider),
                style,
                out result);
        }

        public static TimeOnly ParseExact(
            string s,
            [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string format) =>
            ParseExact(s, format, null, System.Globalization.DateTimeStyles.None);

        public static TimeOnly ParseExact(
            string s,
            [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string format,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            ArgumentNullException.ThrowIfNull(s);
            ArgumentNullException.ThrowIfNull(format);
            return ParseExact(s.AsSpan(), format.AsSpan(), provider, style);
        }

        public static TimeOnly ParseExact(
            string s,
            [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string[] formats) =>
            ParseExact(s, formats, null, System.Globalization.DateTimeStyles.None);

        public static TimeOnly ParseExact(
            string s,
            [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string[] formats,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            ArgumentNullException.ThrowIfNull(s);
            return ParseExact(s.AsSpan(), formats, provider, style);
        }

        public static TimeOnly ParseExact(
            ReadOnlySpan<char> s,
            [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] ReadOnlySpan<char> format,
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

        public static TimeOnly ParseExact(
            ReadOnlySpan<char> s,
            [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string[] formats) =>
            ParseExact(s, formats, null, System.Globalization.DateTimeStyles.None);

        public static TimeOnly ParseExact(
            ReadOnlySpan<char> s,
            [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string[] formats,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style = System.Globalization.DateTimeStyles.None)
        {
            return TryParseExact(s, formats, provider, style, out var result)
                ? result
                : throw new FormatException();
        }

        public static bool TryParseExact(
            [NotNullWhen(true)] string? s,
            [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string? format,
            out TimeOnly result) =>
            TryParseExact(s, format, null, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(
            [NotNullWhen(true)] string? s,
            [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string? format,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out TimeOnly result)
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
            [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string?[]? formats,
            out TimeOnly result) =>
            TryParseExact(s, formats, null, System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(
            [NotNullWhen(true)] string? s,
            [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string?[]? formats,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style, out TimeOnly result)
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
            [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] ReadOnlySpan<char> format,
            out TimeOnly result) => TryParseExact(s, format, null,
                System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(
            ReadOnlySpan<char> s,
            [StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] ReadOnlySpan<char> format,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style,
            out TimeOnly result)
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
            [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string?[]? formats,
            out TimeOnly result) => TryParseExact(s, formats, null,
                System.Globalization.DateTimeStyles.None, out result);

        public static bool TryParseExact(
            ReadOnlySpan<char> s,
            [NotNullWhen(true), StringSyntax(StringSyntaxAttribute.TimeOnlyFormat)] string?[]? formats,
            IFormatProvider? provider,
            System.Globalization.DateTimeStyles style,
            out TimeOnly result)
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
                var parseResult = InvariantDateOnlyTimeOnlyParser.TryParseTimeExact(
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
        public static TimeSpan operator -(TimeOnly left, TimeOnly right)
        {
            var difference = left._ticks - right._ticks;
            return new TimeSpan(difference + ((difference >> 63) & TimeSpan.TicksPerDay));
        }
        public static bool operator ==(TimeOnly left, TimeOnly right) => left.Equals(right);
        public static bool operator !=(TimeOnly left, TimeOnly right) => !left.Equals(right);
        public static bool operator <(TimeOnly left, TimeOnly right) => left._ticks < right._ticks;
        public static bool operator <=(TimeOnly left, TimeOnly right) => left._ticks <= right._ticks;
        public static bool operator >(TimeOnly left, TimeOnly right) => left._ticks > right._ticks;
        public static bool operator >=(TimeOnly left, TimeOnly right) => left._ticks >= right._ticks;

        private TimeOnly AddTicks(long ticks)
        {
            return new TimeOnly(Wrap(_ticks + ticks % TimeSpan.TicksPerDay));
        }

        private TimeOnly AddTicks(long ticks, out int wrappedDays)
        {
            var days = ticks / TimeSpan.TicksPerDay;
            var newTicks = ticks % TimeSpan.TicksPerDay + _ticks;
            if (newTicks < 0)
            {
                days--;
                newTicks += TimeSpan.TicksPerDay;
            }
            else if (newTicks >= TimeSpan.TicksPerDay)
            {
                days++;
                newTicks -= TimeSpan.TicksPerDay;
            }
            wrappedDays = checked((int)days);
            return new TimeOnly(newTicks);
        }

        private static long Wrap(long ticks)
        {
            ticks %= TimeSpan.TicksPerDay;
            return ticks < 0 ? ticks + TimeSpan.TicksPerDay : ticks;
        }

        private string FormatInvariant(string? format, IFormatProvider? provider)
        {
            if (string.IsNullOrEmpty(format))
            {
                format = "t";
            }

            if (format.Length == 1)
            {
                switch (format[0])
                {
                    case 'O':
                    case 'o':
                        return InvariantDateTimeText.TwoDigits(Hour) + ":" +
                            InvariantDateTimeText.TwoDigits(Minute) + ":" +
                            InvariantDateTimeText.TwoDigits(Second) + "." +
                            (_ticks % TimeSpan.TicksPerSecond).ToString().PadLeft(7, '0');
                    case 'R':
                    case 'r':
                        return FormatCustom("HH:mm:ss");
                    case 't':
                        {
                            var dateTimeFormat =
                                System.Globalization.DateTimeFormatInfo.GetInstance(provider);
                            return FormatCustom(dateTimeFormat.ShortTimePattern);
                        }
                    case 'T':
                        {
                            var dateTimeFormat =
                                System.Globalization.DateTimeFormatInfo.GetInstance(provider);
                            return FormatCustom(dateTimeFormat.LongTimePattern);
                        }
                    default:
                        throw new FormatException();
                }
            }

            if (!IsValidCustomTimeFormat(format, throwOnError: true))
            {
                throw new FormatException();
            }
            System.Globalization.DateTimeFormatInfo.GetInstance(provider);
            return FormatCustom(format);
        }

        private string FormatCustom(string format) => InvariantDateTimePattern.Format(
            new DateTime(_ticks),
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
                format = "t";
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
                    case 't':
                        format = System.Globalization.DateTimeFormatInfo
                            .GetInstance(provider).ShortTimePattern;
                        break;
                    case 'T':
                        format = System.Globalization.DateTimeFormatInfo
                            .GetInstance(provider).LongTimePattern;
                        break;
                    default:
                        throw new FormatException();
                }
            }
            else
            {
                if (!IsValidCustomTimeFormat(format, throwOnError: false))
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
                    new DateTime(_ticks),
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
            if (destination.Length < 16)
            {
                charsWritten = 0;
                return false;
            }

            WriteTwoDigits(destination, 0, Hour);
            destination[2] = TChar.CastFrom(':');
            WriteTwoDigits(destination, 3, Minute);
            destination[5] = TChar.CastFrom(':');
            WriteTwoDigits(destination, 6, Second);
            destination[8] = TChar.CastFrom('.');
            WriteSevenDigits(
                destination,
                9,
                (int)(_ticks % TimeSpan.TicksPerSecond));
            charsWritten = 16;
            return true;
        }

        private bool TryFormatR<TChar>(Span<TChar> destination, out int charsWritten)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            if (destination.Length < 8)
            {
                charsWritten = 0;
                return false;
            }

            WriteTwoDigits(destination, 0, Hour);
            destination[2] = TChar.CastFrom(':');
            WriteTwoDigits(destination, 3, Minute);
            destination[5] = TChar.CastFrom(':');
            WriteTwoDigits(destination, 6, Second);
            charsWritten = 8;
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

        private static void WriteTwoDigits<TChar>(
            Span<TChar> destination,
            int offset,
            int value)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            destination[offset] = TChar.CastFrom('0' + value / 10);
            destination[offset + 1] = TChar.CastFrom('0' + value % 10);
        }

        private static void WriteSevenDigits<TChar>(
            Span<TChar> destination,
            int offset,
            int value)
            where TChar : unmanaged, IUtfChar<TChar>
        {
            var divisor = 1_000_000;
            for (var index = 0; index < 7; index++)
            {
                destination[offset + index] = TChar.CastFrom('0' + value / divisor % 10);
                divisor /= 10;
            }
        }

        private static bool IsValidCustomTimeFormat(ReadOnlySpan<char> format, bool throwOnError)
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
                    case 'd':
                    case 'M':
                    case 'y':
                    case '/':
                    case 'z':
                    case 'k':
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
            out TimeOnly result)
        {
            var formatInfo = IsInvariantStandardFormat(format)
                ? System.Globalization.DateTimeFormatInfo.InvariantInfo
                : System.Globalization.DateTimeFormatInfo.GetInstance(provider);
            return InvariantDateOnlyTimeOnlyParser.TryParseTimeExact(
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
}
