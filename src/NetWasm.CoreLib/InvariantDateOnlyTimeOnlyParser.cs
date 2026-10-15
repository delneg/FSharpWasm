// Adapted from dotnet/runtime System.Private.CoreLib invariant date/time parsing.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Globalization;

namespace System
{
    internal enum InvariantDateTimeParseResult
    {
        Success,
        InputMismatch,
        BadFormat,
    }

    [Flags]
    internal enum InvariantDateTimeComponents
    {
        None = 0,
        Year = 1 << 0,
        Month = 1 << 1,
        Day = 1 << 2,
        DayOfWeek = 1 << 3,
        Hour = 1 << 4,
        Minute = 1 << 5,
        Second = 1 << 6,
        Fraction = 1 << 7,
        Designator = 1 << 8,
        Zone = 1 << 9,
    }

    internal static class InvariantDateOnlyTimeOnlyParser
    {
        private const InvariantDateTimeComponents DateComponents =
            InvariantDateTimeComponents.Year |
            InvariantDateTimeComponents.Month |
            InvariantDateTimeComponents.Day |
            InvariantDateTimeComponents.DayOfWeek;

        internal static bool TryParseDate(
            ReadOnlySpan<char> s,
            DateTimeFormatInfo dateTimeFormat,
            DateTimeStyles style,
            out DateOnly result)
        {
            _ = dateTimeFormat;
            _ = style;
            return new GeneralDateParser(s).TryParse(out result);
        }

        internal static bool TryParseTime(
            ReadOnlySpan<char> s,
            DateTimeFormatInfo dateTimeFormat,
            DateTimeStyles style,
            out TimeOnly result)
        {
            _ = dateTimeFormat;
            _ = style;
            return new GeneralTimeParser(s).TryParse(out result);
        }

        private ref struct GeneralDateParser
        {
            private readonly ReadOnlySpan<char> _value;
            private int _index;

            internal GeneralDateParser(ReadOnlySpan<char> value)
            {
                _value = TrimGeneralWhite(value);
                _index = 0;
            }

            internal bool TryParse(out DateOnly result)
            {
                result = default;
                if (_value.Length == 0)
                {
                    return false;
                }

                var expectedDayOfWeek = -1;
                if (TryReadDayName(out var dayOfWeek))
                {
                    expectedDayOfWeek = dayOfWeek;
                    var separatorStart = _index;
                    SkipWhite();
                    if (_index < _value.Length && _value[_index] == ',')
                    {
                        _index++;
                        SkipWhite();
                    }
                    else if (_index == separatorStart)
                    {
                        return false;
                    }
                }

                Span<int> values = stackalloc int[3];
                Span<int> widths = stackalloc int[3];
                Span<int> kinds = stackalloc int[3];
                var count = 0;
                while (_index < _value.Length)
                {
                    if (count == values.Length)
                    {
                        return false;
                    }

                    if (TryReadMonthName(out var month))
                    {
                        values[count] = month;
                        kinds[count] = 1;
                        widths[count] = 0;
                    }
                    else if (TryReadNumber(out var number, out var width))
                    {
                        values[count] = number;
                        kinds[count] = 0;
                        widths[count] = width;
                    }
                    else
                    {
                        return false;
                    }
                    count++;

                    if (_index == _value.Length)
                    {
                        break;
                    }

                    if (!TryReadSeparator(out var separator))
                    {
                        return false;
                    }
                    if (_index == _value.Length)
                    {
                        return false;
                    }

                    if (separator is GeneralDateSeparator.Space or GeneralDateSeparator.Comma &&
                        TryReadDayName(out dayOfWeek))
                    {
                        if (expectedDayOfWeek >= 0 || count < 2)
                        {
                            return false;
                        }
                        expectedDayOfWeek = dayOfWeek;
                        SkipWhite();
                        if (_index != _value.Length)
                        {
                            return false;
                        }
                        break;
                    }

                    if (count == values.Length)
                    {
                        return false;
                    }
                }

                if (count == 0 || !TryResolve(values, widths, kinds, count,
                        out var year, out var resolvedMonth, out var day))
                {
                    return false;
                }

                try
                {
                    result = new DateOnly(year, resolvedMonth, day);
                }
                catch (ArgumentOutOfRangeException)
                {
                    result = default;
                    return false;
                }

                if (expectedDayOfWeek >= 0 && (int)result.DayOfWeek != expectedDayOfWeek)
                {
                    result = default;
                    return false;
                }
                return true;
            }

            private static bool TryResolve(
                ReadOnlySpan<int> values,
                ReadOnlySpan<int> widths,
                ReadOnlySpan<int> kinds,
                int count,
                out int year,
                out int month,
                out int day)
            {
                year = 0;
                month = 0;
                day = 0;
                var monthPosition = -1;
                for (var index = 0; index < count; index++)
                {
                    if (kinds[index] == 1)
                    {
                        if (monthPosition >= 0)
                        {
                            return false;
                        }
                        monthPosition = index;
                    }
                }

                if (count != 3)
                {
                    return false;
                }

                if (monthPosition >= 0)
                {
                    month = values[monthPosition];
                    if (monthPosition == 0)
                    {
                        day = values[1];
                        year = ExpandYear(values[2], widths[2]);
                    }
                    else if (monthPosition == 1)
                    {
                        if (widths[0] >= 3)
                        {
                            year = ExpandYear(values[0], widths[0]);
                            day = values[2];
                        }
                        else
                        {
                            day = values[0];
                            year = ExpandYear(values[2], widths[2]);
                        }
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    if (widths[0] >= 3)
                    {
                        year = ExpandYear(values[0], widths[0]);
                        month = values[1];
                        day = values[2];
                    }
                    else
                    {
                        month = values[0];
                        day = values[1];
                        year = ExpandYear(values[2], widths[2]);
                    }
                }

                return year is >= 1 and <= 9999 && month is >= 1 and <= 12 && day >= 1;
            }

            private bool TryReadSeparator(out GeneralDateSeparator separator)
            {
                separator = GeneralDateSeparator.None;
                var start = _index;
                SkipWhite();
                var hadWhite = _index != start;
                if (_index == _value.Length)
                {
                    separator = hadWhite
                        ? GeneralDateSeparator.Space
                        : GeneralDateSeparator.None;
                    return hadWhite;
                }

                var current = _value[_index];
                if (current == ',')
                {
                    separator = GeneralDateSeparator.Comma;
                    _index++;
                    SkipWhite();
                    return true;
                }
                if (current is '/' or '-' or '.')
                {
                    separator = GeneralDateSeparator.Date;
                    _index++;
                    SkipWhite();
                    if (_index < _value.Length &&
                        _value[_index] is '/' or '-' or '.' or ',')
                    {
                        return false;
                    }
                    return true;
                }
                if (hadWhite)
                {
                    separator = GeneralDateSeparator.Space;
                    return true;
                }
                return false;
            }

            private bool TryReadNumber(out int number, out int width)
            {
                number = 0;
                width = 0;
                while (_index < _value.Length)
                {
                    var digit = _value[_index] - '0';
                    if ((uint)digit > 9)
                    {
                        break;
                    }
                    if (number > 9999)
                    {
                        return false;
                    }
                    number = number * 10 + digit;
                    width++;
                    _index++;
                }
                return width > 0;
            }

            private bool TryReadMonthName(out int month)
            {
                for (var candidate = 1; candidate <= 12; candidate++)
                {
                    var full = MonthName(candidate, abbreviated: false);
                    if (TryMatchName(full) ||
                        full.Length > 3 && TryMatchName(MonthName(candidate, abbreviated: true)))
                    {
                        month = candidate;
                        return true;
                    }
                }
                month = 0;
                return false;
            }

            private bool TryReadDayName(out int dayOfWeek)
            {
                for (var candidate = 0; candidate <= 6; candidate++)
                {
                    var full = DayName((DayOfWeek)candidate, abbreviated: false);
                    if (TryMatchName(full) || TryMatchName(DayName((DayOfWeek)candidate, abbreviated: true)))
                    {
                        dayOfWeek = candidate;
                        return true;
                    }
                }
                dayOfWeek = -1;
                return false;
            }

            private bool TryMatchName(string name)
            {
                if (!MatchesAtIgnoreCase(_value, _index, name))
                {
                    return false;
                }
                var end = _index + name.Length;
                if (end < _value.Length && char.IsLetter(_value[end]))
                {
                    return false;
                }
                _index = end;
                return true;
            }

            private void SkipWhite()
            {
                while (_index < _value.Length && char.IsWhiteSpace(_value[_index]))
                {
                    _index++;
                }
            }
        }

        private enum GeneralDateSeparator
        {
            None,
            Space,
            Date,
            Comma,
        }

        private ref struct GeneralTimeParser
        {
            private readonly ReadOnlySpan<char> _value;
            private int _index;

            internal GeneralTimeParser(ReadOnlySpan<char> value)
            {
                _value = TrimGeneralWhite(value);
                _index = 0;
            }

            internal bool TryParse(out TimeOnly result)
            {
                result = default;
                if (!TryReadNumber(1, 2, out var hour))
                {
                    return false;
                }

                SkipWhite();
                var minute = 0;
                var second = 0;
                var fraction = 0d;
                var hasMinute = false;
                if (TryConsume(':'))
                {
                    hasMinute = true;
                    SkipWhite();
                    if (!TryReadNumber(1, 2, out minute))
                    {
                        return false;
                    }
                    SkipWhite();
                    if (TryConsume(':'))
                    {
                        SkipWhite();
                        if (!TryReadNumber(1, 2, out second))
                        {
                            return false;
                        }
                        if (_index < _value.Length &&
                            _value[_index] is '.' or ',')
                        {
                            _index++;
                            ReadFraction(out fraction);
                        }
                    }
                }

                SkipWhite();
                var hasDesignator = TryReadDesignator(out var postMeridiem);
                SkipWhite();
                if (_index != _value.Length ||
                    !hasMinute && !hasDesignator ||
                    minute >= 60 ||
                    second >= 60)
                {
                    return false;
                }

                if (hasDesignator)
                {
                    if (postMeridiem)
                    {
                        if (hour > 23)
                        {
                            return false;
                        }
                        if (hour < 12)
                        {
                            hour += 12;
                        }
                    }
                    else
                    {
                        if (hour > 12)
                        {
                            return false;
                        }
                        if (hour == 12)
                        {
                            hour = 0;
                        }
                    }
                }
                else if (hour > 24)
                {
                    return false;
                }

                if (hour == 24)
                {
                    if (minute != 0 || second != 0 || fraction > 0)
                    {
                        return false;
                    }
                    hour = 0;
                }

                var ticks = (long)hour * TimeSpan.TicksPerHour +
                    minute * TimeSpan.TicksPerMinute +
                    second * TimeSpan.TicksPerSecond;
                if (fraction > 0)
                {
                    ticks += (long)Math.Round(fraction * TimeSpan.TicksPerSecond);
                }
                if (ticks == TimeSpan.TicksPerDay)
                {
                    ticks = 0;
                }
                else if ((ulong)ticks > (ulong)TimeSpan.TicksPerDay)
                {
                    return false;
                }
                result = new TimeOnly(ticks);
                return true;
            }

            private void ReadFraction(out double fraction)
            {
                fraction = 0;
                var decimalBase = 0.1;
                while (_index < _value.Length)
                {
                    var digit = _value[_index] - '0';
                    if ((uint)digit > 9)
                    {
                        break;
                    }
                    fraction += digit * decimalBase;
                    decimalBase *= 0.1;
                    _index++;
                }
            }

            private bool TryReadDesignator(out bool postMeridiem)
            {
                postMeridiem = false;
                if (_index >= _value.Length)
                {
                    return false;
                }
                var designator = FoldAscii(_value[_index]);
                if (designator is not ('a' or 'p'))
                {
                    return false;
                }
                if (_index + 1 >= _value.Length ||
                    FoldAscii(_value[_index + 1]) != 'm')
                {
                    return false;
                }
                _index += 2;
                postMeridiem = designator == 'p';
                return true;
            }

            private bool TryReadNumber(int minimumDigits, int maximumDigits, out int number)
            {
                number = 0;
                var digits = 0;
                while (digits < maximumDigits && _index < _value.Length)
                {
                    var digit = _value[_index] - '0';
                    if ((uint)digit > 9)
                    {
                        break;
                    }
                    number = number * 10 + digit;
                    digits++;
                    _index++;
                }
                return digits >= minimumDigits;
            }

            private bool TryConsume(char value)
            {
                if (_index >= _value.Length || _value[_index] != value)
                {
                    return false;
                }
                _index++;
                return true;
            }

            private void SkipWhite()
            {
                while (_index < _value.Length && char.IsWhiteSpace(_value[_index]))
                {
                    _index++;
                }
            }
        }

        internal static InvariantDateTimeParseResult TryParseDateExact(
            ReadOnlySpan<char> s,
            ReadOnlySpan<char> format,
            DateTimeFormatInfo dateTimeFormat,
            DateTimeStyles style,
            out DateOnly result)
        {
            result = default;
            if (format.Length == 0)
            {
                return InvariantDateTimeParseResult.InputMismatch;
            }

            if (format.Length == 1)
            {
                switch (format[0])
                {
                    case 'd':
                        format = dateTimeFormat.ShortDatePattern;
                        break;
                    case 'D':
                        format = dateTimeFormat.LongDatePattern;
                        break;
                    case 'M':
                    case 'm':
                        format = dateTimeFormat.MonthDayPattern;
                        break;
                    case 'Y':
                    case 'y':
                        format = dateTimeFormat.YearMonthPattern;
                        break;
                    case 'O':
                    case 'o':
                        format = "yyyy'-'MM'-'dd";
                        break;
                    case 'R':
                    case 'r':
                        format = "ddd, dd MMM yyyy";
                        break;
                    default:
                        return InvariantDateTimeParseResult.BadFormat;
                }
            }

            var parser = new PatternParser(s, format, style);
            var parseResult = parser.TryParse(out var parts);
            if (parseResult != InvariantDateTimeParseResult.Success ||
                (parts.Components & InvariantDateTimeComponents.Zone) != 0 ||
                !parts.TryCreateDate(out result) ||
                !parts.TryCreateTime(out _))
            {
                result = default;
                return parseResult == InvariantDateTimeParseResult.BadFormat
                    ? parseResult
                    : InvariantDateTimeParseResult.InputMismatch;
            }

            return InvariantDateTimeParseResult.Success;
        }

        internal static InvariantDateTimeParseResult TryParseTimeExact(
            ReadOnlySpan<char> s,
            ReadOnlySpan<char> format,
            DateTimeFormatInfo dateTimeFormat,
            DateTimeStyles style,
            out TimeOnly result)
        {
            result = default;
            if (format.Length == 0)
            {
                return InvariantDateTimeParseResult.InputMismatch;
            }

            if (format.Length == 1)
            {
                switch (format[0])
                {
                    case 't':
                        format = dateTimeFormat.ShortTimePattern;
                        break;
                    case 'T':
                        format = dateTimeFormat.LongTimePattern;
                        break;
                    case 'O':
                    case 'o':
                        format = "HH':'mm':'ss'.'fffffff";
                        break;
                    case 'R':
                    case 'r':
                        format = "HH':'mm':'ss";
                        break;
                    case 'g':
                    case 'G':
                        return InvariantDateTimeParseResult.InputMismatch;
                    default:
                        return InvariantDateTimeParseResult.BadFormat;
                }
            }

            var parser = new PatternParser(s, format, style);
            var parseResult = parser.TryParse(out var parts);
            if (parseResult != InvariantDateTimeParseResult.Success ||
                (parts.Components & InvariantDateTimeComponents.Zone) != 0 ||
                !parts.TryCreateTime(out result) ||
                ((parts.Components & DateComponents) != 0 &&
                    !parts.TryCreateDate(out _)))
            {
                result = default;
                return parseResult == InvariantDateTimeParseResult.BadFormat
                    ? parseResult
                    : InvariantDateTimeParseResult.InputMismatch;
            }

            return InvariantDateTimeParseResult.Success;
        }

        private static bool TryParseDatePattern(
            ReadOnlySpan<char> s,
            ReadOnlySpan<char> format,
            DateTimeStyles style,
            out DateOnly result) =>
            TryParseDateExact(s, format, DateTimeFormatInfo.InvariantInfo, style, out result) ==
                InvariantDateTimeParseResult.Success;

        private static bool TryParseTimePattern(
            ReadOnlySpan<char> s,
            ReadOnlySpan<char> format,
            DateTimeStyles style,
            out TimeOnly result) =>
            TryParseTimeExact(s, format, DateTimeFormatInfo.InvariantInfo, style, out result) ==
                InvariantDateTimeParseResult.Success;

        private struct ParsedParts
        {
            internal InvariantDateTimeComponents Components;
            internal int Year;
            internal int Month;
            internal int Day;
            internal int DayOfWeek;
            internal int Hour;
            internal int Minute;
            internal int Second;
            internal long Fraction;
            internal bool IsTwelveHour;
            internal bool HasDesignator;
            internal bool IsPostMeridiem;

            internal bool TrySet(InvariantDateTimeComponents component, int value, ref int target)
            {
                if ((Components & component) != 0)
                {
                    return target == value;
                }

                target = value;
                Components |= component;
                return true;
            }

            internal bool TryCreateDate(out DateOnly result)
            {
                var hasYear = (Components & InvariantDateTimeComponents.Year) != 0;
                var hasMonth = (Components & InvariantDateTimeComponents.Month) != 0;
                var hasDay = (Components & InvariantDateTimeComponents.Day) != 0;
                var now = hasYear ? default : DateTime.Now;
                var year = hasYear ? Year : now.Year;
                var month = hasMonth ? Month : hasYear ? 1 : now.Month;
                var day = hasDay ? Day : hasMonth || hasYear ? 1 : now.Day;

                try
                {
                    result = new DateOnly(year, month, day);
                }
                catch (ArgumentOutOfRangeException)
                {
                    result = default;
                    return false;
                }

                if ((Components & InvariantDateTimeComponents.DayOfWeek) != 0 &&
                    (int)result.DayOfWeek != DayOfWeek)
                {
                    result = default;
                    return false;
                }

                return true;
            }

            internal bool TryCreateTime(out TimeOnly result)
            {
                var hour = Hour;
                if (IsTwelveHour)
                {
                    if (hour == 12)
                    {
                        hour = 0;
                    }
                    if (HasDesignator && IsPostMeridiem)
                    {
                        hour += 12;
                    }
                }

                if (hour == 24)
                {
                    if (Minute != 0 || Second != 0 || Fraction != 0)
                    {
                        result = default;
                        return false;
                    }
                    hour = 0;
                }

                if ((uint)hour >= 24 || (uint)Minute >= 60 || (uint)Second >= 60)
                {
                    result = default;
                    return false;
                }

                result = new TimeOnly(
                    hour * TimeSpan.TicksPerHour +
                    Minute * TimeSpan.TicksPerMinute +
                    Second * TimeSpan.TicksPerSecond +
                    Fraction);
                return true;
            }
        }

        private ref struct PatternParser
        {
            private readonly ReadOnlySpan<char> _value;
            private readonly ReadOnlySpan<char> _format;
            private readonly bool _allowInnerWhite;
            private int _valueIndex;
            private ParsedParts _parts;

            internal PatternParser(
                ReadOnlySpan<char> value,
                ReadOnlySpan<char> format,
                DateTimeStyles style)
            {
                _value = TrimOuterWhite(value, style);
                _format = TrimOuterWhite(format, style);
                _allowInnerWhite = (style & DateTimeStyles.AllowInnerWhite) != 0;
                _valueIndex = 0;
                _parts = default;
            }

            internal InvariantDateTimeParseResult TryParse(out ParsedParts parts)
            {
                if (_value.Length == 0)
                {
                    parts = default;
                    return InvariantDateTimeParseResult.InputMismatch;
                }

                for (var formatIndex = 0; formatIndex < _format.Length; formatIndex++)
                {
                    var token = _format[formatIndex];
                    if (token is '\'' or '"')
                    {
                        SkipInnerWhite();
                        if (!TryMatchQuotedLiteral(ref formatIndex, token))
                        {
                            parts = default;
                            return InvariantDateTimeParseResult.InputMismatch;
                        }
                        continue;
                    }

                    if (token == '\\')
                    {
                        SkipInnerWhite();
                        if (++formatIndex >= _format.Length)
                        {
                            parts = default;
                            return InvariantDateTimeParseResult.BadFormat;
                        }
                        if (!Match(_format[formatIndex]))
                        {
                            parts = default;
                            return InvariantDateTimeParseResult.InputMismatch;
                        }
                        continue;
                    }

                    if (token == '%')
                    {
                        if (++formatIndex >= _format.Length || _format[formatIndex] == '%')
                        {
                            parts = default;
                            return InvariantDateTimeParseResult.BadFormat;
                        }

                        var percentResult = TryParseToken(_format[formatIndex], 1);
                        if (percentResult != InvariantDateTimeParseResult.Success)
                        {
                            parts = default;
                            return percentResult;
                        }
                        continue;
                    }

                    if (char.IsWhiteSpace(token))
                    {
                        var formatWhiteStart = formatIndex;
                        while (formatIndex + 1 < _format.Length &&
                            char.IsWhiteSpace(_format[formatIndex + 1]))
                        {
                            formatIndex++;
                        }

                        if (_allowInnerWhite)
                        {
                            SkipInnerWhite();
                            continue;
                        }

                        if (!MatchWhiteSpace(formatIndex - formatWhiteStart + 1))
                        {
                            parts = default;
                            return InvariantDateTimeParseResult.InputMismatch;
                        }
                        continue;
                    }

                    if (!IsPatternToken(token))
                    {
                        SkipInnerWhite();
                        if (token == '.' &&
                            formatIndex + 1 < _format.Length &&
                            _format[formatIndex + 1] == 'F' &&
                            (_valueIndex >= _value.Length || _value[_valueIndex] != '.'))
                        {
                            continue;
                        }

                        if (!Match(token))
                        {
                            parts = default;
                            return InvariantDateTimeParseResult.InputMismatch;
                        }
                        continue;
                    }

                    var count = token is ':' or '/' or 'K'
                        ? 1
                        : CountRepeat(_format, formatIndex, token);
                    var tokenResult = TryParseToken(token, count);
                    if (tokenResult != InvariantDateTimeParseResult.Success)
                    {
                        parts = default;
                        return tokenResult;
                    }
                    formatIndex += count - 1;
                }

                if (_valueIndex != _value.Length)
                {
                    parts = default;
                    return InvariantDateTimeParseResult.InputMismatch;
                }

                parts = _parts;
                return InvariantDateTimeParseResult.Success;
            }

            private InvariantDateTimeParseResult TryParseToken(char token, int count)
            {
                SkipInnerWhite();
                return token switch
                {
                    'd' => TryParseDay(count),
                    'M' => TryParseMonth(count),
                    'y' => TryParseYear(count),
                    'h' => TryParseHour(count, twelveHour: true),
                    'H' => TryParseHour(count, twelveHour: false),
                    'm' => TryParseComponent(
                        count,
                        0,
                        59,
                        InvariantDateTimeComponents.Minute,
                        ref _parts.Minute),
                    's' => TryParseComponent(
                        count,
                        0,
                        59,
                        InvariantDateTimeComponents.Second,
                        ref _parts.Second),
                    'f' => TryParseFraction(count, optional: false),
                    'F' => TryParseFraction(count, optional: true),
                    't' => TryParseDesignator(count),
                    'g' => TryParseEra(count),
                    'z' => TryParseZone(count),
                    'K' => count == 1 ? TryParseKind() : InvariantDateTimeParseResult.BadFormat,
                    ':' => Match(':') ? InvariantDateTimeParseResult.Success : InvariantDateTimeParseResult.InputMismatch,
                    '/' => Match('/') ? InvariantDateTimeParseResult.Success : InvariantDateTimeParseResult.InputMismatch,
                    _ => InvariantDateTimeParseResult.BadFormat,
                };
            }

            private InvariantDateTimeParseResult TryParseDay(int count)
            {
                if (count < 1)
                {
                    return InvariantDateTimeParseResult.BadFormat;
                }
                if (count <= 2)
                {
                    return TryParseComponent(
                        count,
                        1,
                        31,
                        InvariantDateTimeComponents.Day,
                        ref _parts.Day);
                }
                if (!TryMatchName(count == 3, month: false, out var dayOfWeek) ||
                    !_parts.TrySet(
                        InvariantDateTimeComponents.DayOfWeek,
                        dayOfWeek,
                        ref _parts.DayOfWeek))
                {
                    return InvariantDateTimeParseResult.InputMismatch;
                }
                return InvariantDateTimeParseResult.Success;
            }

            private InvariantDateTimeParseResult TryParseMonth(int count)
            {
                if (count < 1)
                {
                    return InvariantDateTimeParseResult.BadFormat;
                }
                if (count <= 2)
                {
                    return TryParseComponent(
                        count,
                        1,
                        12,
                        InvariantDateTimeComponents.Month,
                        ref _parts.Month);
                }
                if (!TryMatchName(count == 3, month: true, out var month) ||
                    !_parts.TrySet(
                        InvariantDateTimeComponents.Month,
                        month,
                        ref _parts.Month))
                {
                    return InvariantDateTimeParseResult.InputMismatch;
                }
                return InvariantDateTimeParseResult.Success;
            }

            private InvariantDateTimeParseResult TryParseYear(int count)
            {
                if (count < 1)
                {
                    return InvariantDateTimeParseResult.BadFormat;
                }

                var minimumDigits = count == 1 ? 1 : count;
                var maximumDigits = count == 1 ? 2 : count;
                if (!TryReadYear(minimumDigits, maximumDigits, out var year))
                {
                    return InvariantDateTimeParseResult.InputMismatch;
                }
                if (count <= 2)
                {
                    year = year <= 49 ? 2000 + year : 1900 + year;
                }
                if (year is < 1 or > 9999 ||
                    !_parts.TrySet(
                        InvariantDateTimeComponents.Year,
                        year,
                        ref _parts.Year))
                {
                    return InvariantDateTimeParseResult.InputMismatch;
                }
                return InvariantDateTimeParseResult.Success;
            }

            private InvariantDateTimeParseResult TryParseHour(int count, bool twelveHour)
            {
                var effectiveCount = Math.Min(count, 2);
                if (count < 1 ||
                    !TryReadNumber(effectiveCount == 1 ? 1 : 2, 2, out var hour) ||
                    hour < 0 || hour > (twelveHour ? 12 : 24))
                {
                    return count < 1
                        ? InvariantDateTimeParseResult.BadFormat
                        : InvariantDateTimeParseResult.InputMismatch;
                }
                if ((_parts.Components & InvariantDateTimeComponents.Hour) != 0 &&
                    (_parts.Hour != hour || _parts.IsTwelveHour != twelveHour))
                {
                    return InvariantDateTimeParseResult.InputMismatch;
                }
                _parts.Hour = hour;
                _parts.IsTwelveHour = twelveHour;
                _parts.Components |= InvariantDateTimeComponents.Hour;
                return InvariantDateTimeParseResult.Success;
            }

            private InvariantDateTimeParseResult TryParseComponent(
                int count,
                int minimum,
                int maximum,
                InvariantDateTimeComponents component,
                ref int target)
            {
                var effectiveCount = Math.Min(count, 2);
                if (count < 1)
                {
                    return InvariantDateTimeParseResult.BadFormat;
                }
                if (!TryReadNumber(effectiveCount == 1 ? 1 : 2, 2, out var value) ||
                    value < minimum || value > maximum ||
                    !_parts.TrySet(component, value, ref target))
                {
                    return InvariantDateTimeParseResult.InputMismatch;
                }
                return InvariantDateTimeParseResult.Success;
            }

            private InvariantDateTimeParseResult TryParseFraction(int count, bool optional)
            {
                if (count is < 1 or > 7)
                {
                    return InvariantDateTimeParseResult.BadFormat;
                }

                var start = _valueIndex;
                var digits = 0;
                while (digits < count && _valueIndex + digits < _value.Length &&
                    (uint)(_value[_valueIndex + digits] - '0') <= 9)
                {
                    digits++;
                }
                if (!optional && digits != count)
                {
                    return InvariantDateTimeParseResult.InputMismatch;
                }

                var fraction = 0L;
                for (var index = 0; index < digits; index++)
                {
                    fraction = fraction * 10 + (_value[start + index] - '0');
                }
                for (var index = digits; index < 7; index++)
                {
                    fraction *= 10;
                }

                if ((_parts.Components & InvariantDateTimeComponents.Fraction) != 0 &&
                    _parts.Fraction != fraction)
                {
                    return InvariantDateTimeParseResult.InputMismatch;
                }

                _valueIndex += digits;
                _parts.Fraction = fraction;
                _parts.Components |= InvariantDateTimeComponents.Fraction;
                return InvariantDateTimeParseResult.Success;
            }

            private InvariantDateTimeParseResult TryParseDesignator(int count)
            {
                if (count < 1 || _valueIndex >= _value.Length)
                {
                    return count < 1
                        ? InvariantDateTimeParseResult.BadFormat
                        : InvariantDateTimeParseResult.InputMismatch;
                }

                var value = FoldAscii(_value[_valueIndex]);
                if (value is not ('a' or 'p'))
                {
                    return InvariantDateTimeParseResult.InputMismatch;
                }
                _valueIndex++;
                if (count != 1 && !MatchIgnoreCase('m'))
                {
                    return InvariantDateTimeParseResult.InputMismatch;
                }

                var isPostMeridiem = value == 'p';
                if (_parts.HasDesignator && _parts.IsPostMeridiem != isPostMeridiem)
                {
                    return InvariantDateTimeParseResult.InputMismatch;
                }
                _parts.HasDesignator = true;
                _parts.IsPostMeridiem = isPostMeridiem;
                _parts.Components |= InvariantDateTimeComponents.Designator;
                return InvariantDateTimeParseResult.Success;
            }

            private InvariantDateTimeParseResult TryParseEra(int count)
            {
                if (count < 1)
                {
                    return InvariantDateTimeParseResult.BadFormat;
                }
                if (!MatchIgnoreCase("A.D."))
                {
                    return InvariantDateTimeParseResult.InputMismatch;
                }
                return InvariantDateTimeParseResult.Success;
            }

            private InvariantDateTimeParseResult TryParseZone(int count)
            {
                if (count is < 1 or > 3)
                {
                    return InvariantDateTimeParseResult.BadFormat;
                }
                if (_valueIndex >= _value.Length || _value[_valueIndex] is not ('+' or '-'))
                {
                    return InvariantDateTimeParseResult.InputMismatch;
                }
                _valueIndex++;
                if (!TryReadNumber(count == 1 ? 1 : 2, 2, out var hours))
                {
                    return InvariantDateTimeParseResult.InputMismatch;
                }
                var minutes = 0;
                if (count == 3 && (!Match(':') || !TryReadNumber(2, 2, out minutes)))
                {
                    return InvariantDateTimeParseResult.InputMismatch;
                }
                if (hours > 14 || minutes >= 60 || hours == 14 && minutes != 0)
                {
                    return InvariantDateTimeParseResult.InputMismatch;
                }
                _parts.Components |= InvariantDateTimeComponents.Zone;
                return InvariantDateTimeParseResult.Success;
            }

            private InvariantDateTimeParseResult TryParseKind()
            {
                if (_valueIndex == _value.Length)
                {
                    return InvariantDateTimeParseResult.Success;
                }
                if (FoldAscii(_value[_valueIndex]) == 'z')
                {
                    _valueIndex++;
                    _parts.Components |= InvariantDateTimeComponents.Zone;
                    return InvariantDateTimeParseResult.Success;
                }
                if (_value[_valueIndex] is not ('+' or '-'))
                {
                    return InvariantDateTimeParseResult.Success;
                }
                return TryParseZone(3);
            }

            private bool TryMatchName(bool abbreviated, bool month, out int matched)
            {
                var first = month ? 1 : 0;
                var last = month ? 12 : 6;
                for (var candidate = first; candidate <= last; candidate++)
                {
                    var name = month
                        ? MonthName(candidate, abbreviated)
                        : DayName((DayOfWeek)candidate, abbreviated);
                    if (MatchesAtIgnoreCase(name))
                    {
                        _valueIndex += name.Length;
                        matched = candidate;
                        return true;
                    }
                }
                matched = 0;
                return false;
            }

            private bool TryReadNumber(int minimumDigits, int maximumDigits, out int number)
            {
                number = 0;
                var digits = 0;
                while (digits < maximumDigits && _valueIndex < _value.Length)
                {
                    var digit = _value[_valueIndex] - '0';
                    if ((uint)digit > 9)
                    {
                        break;
                    }
                    number = number * 10 + digit;
                    digits++;
                    _valueIndex++;
                }
                return digits >= minimumDigits;
            }

            private bool TryReadYear(int minimumDigits, int maximumDigits, out int year)
            {
                year = 0;
                var digits = 0;
                while (digits < maximumDigits && _valueIndex < _value.Length)
                {
                    var digit = _value[_valueIndex] - '0';
                    if ((uint)digit > 9)
                    {
                        break;
                    }
                    if (year > 999 || year == 999 && digit > 9)
                    {
                        return false;
                    }
                    year = year * 10 + digit;
                    digits++;
                    _valueIndex++;
                }
                return digits >= minimumDigits;
            }

            private bool TryMatchQuotedLiteral(ref int formatIndex, char quote)
            {
                while (++formatIndex < _format.Length)
                {
                    var current = _format[formatIndex];
                    if (current == quote)
                    {
                        return true;
                    }
                    if (current == '\\')
                    {
                        if (++formatIndex >= _format.Length)
                        {
                            return false;
                        }
                        current = _format[formatIndex];
                    }
                    if (!Match(current))
                    {
                        return false;
                    }
                }
                return false;
            }

            private bool MatchWhiteSpace(int exactCount)
            {
                var start = _valueIndex;
                while (_valueIndex < _value.Length && char.IsWhiteSpace(_value[_valueIndex]))
                {
                    _valueIndex++;
                    if (!_allowInnerWhite && _valueIndex - start == exactCount)
                    {
                        break;
                    }
                }
                return _allowInnerWhite
                    ? _valueIndex > start
                    : _valueIndex - start == exactCount;
            }

            private void SkipInnerWhite()
            {
                if (!_allowInnerWhite || _valueIndex == 0)
                {
                    return;
                }
                while (_valueIndex < _value.Length && char.IsWhiteSpace(_value[_valueIndex]))
                {
                    _valueIndex++;
                }
            }

            private bool Match(char expected)
            {
                if (_valueIndex >= _value.Length || _value[_valueIndex] != expected)
                {
                    return false;
                }
                _valueIndex++;
                return true;
            }

            private bool MatchIgnoreCase(char expected)
            {
                if (_valueIndex >= _value.Length ||
                    FoldAscii(_value[_valueIndex]) != FoldAscii(expected))
                {
                    return false;
                }
                _valueIndex++;
                return true;
            }

            private bool MatchIgnoreCase(string expected)
            {
                if (!MatchesAtIgnoreCase(expected))
                {
                    return false;
                }
                _valueIndex += expected.Length;
                return true;
            }

            private bool MatchesAtIgnoreCase(string expected)
            {
                if (_valueIndex > _value.Length - expected.Length)
                {
                    return false;
                }
                for (var index = 0; index < expected.Length; index++)
                {
                    if (FoldAscii(_value[_valueIndex + index]) != FoldAscii(expected[index]))
                    {
                        return false;
                    }
                }
                return true;
            }
        }

        private static ReadOnlySpan<char> TrimOuterWhite(
            ReadOnlySpan<char> value,
            DateTimeStyles style)
        {
            var start = 0;
            var end = value.Length;
            if ((style & DateTimeStyles.AllowLeadingWhite) != 0)
            {
                while (start < end && char.IsWhiteSpace(value[start]))
                {
                    start++;
                }
            }
            if ((style & DateTimeStyles.AllowTrailingWhite) != 0)
            {
                while (end > start && char.IsWhiteSpace(value[end - 1]))
                {
                    end--;
                }
            }
            return value.Slice(start, end - start);
        }

        private static ReadOnlySpan<char> TrimGeneralWhite(ReadOnlySpan<char> value)
        {
            var start = 0;
            var end = value.Length;
            while (start < end && char.IsWhiteSpace(value[start]))
            {
                start++;
            }
            if (end > start && value[end - 1] == '\0')
            {
                while (end > start && value[end - 1] == '\0')
                {
                    end--;
                }
                while (end > start && char.IsWhiteSpace(value[end - 1]))
                {
                    end--;
                }
            }
            else
            {
                while (end > start && char.IsWhiteSpace(value[end - 1]))
                {
                    end--;
                }
            }
            return value.Slice(start, end - start);
        }

        private static int ExpandYear(int year, int width) => width <= 2
            ? year <= 49 ? 2000 + year : 1900 + year
            : year;

        private static bool MatchesAtIgnoreCase(
            ReadOnlySpan<char> value,
            int offset,
            string expected)
        {
            if (offset > value.Length - expected.Length)
            {
                return false;
            }
            for (var index = 0; index < expected.Length; index++)
            {
                if (FoldAscii(value[offset + index]) != FoldAscii(expected[index]))
                {
                    return false;
                }
            }
            return true;
        }

        private static int CountRepeat(ReadOnlySpan<char> format, int offset, char token)
        {
            var count = 1;
            while (offset + count < format.Length && format[offset + count] == token)
            {
                count++;
            }
            return count;
        }

        private static bool IsPatternToken(char value) =>
            value is 'd' or 'M' or 'y' or 'h' or 'H' or 'm' or 's' or 'f' or 'F' or
                't' or 'z' or 'K' or 'g' or ':' or '/';

        private static char FoldAscii(char value) => value is >= 'A' and <= 'Z'
            ? (char)(value | 0x20)
            : value;

        private static string DayName(DayOfWeek day, bool abbreviated) => day switch
        {
            DayOfWeek.Sunday => abbreviated ? "Sun" : "Sunday",
            DayOfWeek.Monday => abbreviated ? "Mon" : "Monday",
            DayOfWeek.Tuesday => abbreviated ? "Tue" : "Tuesday",
            DayOfWeek.Wednesday => abbreviated ? "Wed" : "Wednesday",
            DayOfWeek.Thursday => abbreviated ? "Thu" : "Thursday",
            DayOfWeek.Friday => abbreviated ? "Fri" : "Friday",
            _ => abbreviated ? "Sat" : "Saturday",
        };

        private static string MonthName(int month, bool abbreviated) => month switch
        {
            1 => abbreviated ? "Jan" : "January",
            2 => abbreviated ? "Feb" : "February",
            3 => abbreviated ? "Mar" : "March",
            4 => abbreviated ? "Apr" : "April",
            5 => "May",
            6 => abbreviated ? "Jun" : "June",
            7 => abbreviated ? "Jul" : "July",
            8 => abbreviated ? "Aug" : "August",
            9 => abbreviated ? "Sep" : "September",
            10 => abbreviated ? "Oct" : "October",
            11 => abbreviated ? "Nov" : "November",
            _ => abbreviated ? "Dec" : "December",
        };
    }
}
