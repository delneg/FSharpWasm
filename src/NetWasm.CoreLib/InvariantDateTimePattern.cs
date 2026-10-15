// Adapted from dotnet/runtime System.Private.CoreLib invariant date/time formatting.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    internal static class InvariantDateTimePattern
    {
        internal static string Format(
            DateTime value,
            string format,
            bool hasOffset,
            TimeSpan offset,
            bool explicitOffset)
        {
            Span<char> initialBuffer = stackalloc char[64];
            var output = new Text.ValueStringBuilder(initialBuffer);
            try
            {
                AppendFormat(ref output, value, format, hasOffset, offset, explicitOffset);
                return output.ToString();
            }
            finally
            {
                output.Dispose();
            }
        }

        internal static void AppendFormat(
            ref Text.ValueStringBuilder output,
            DateTime value,
            ReadOnlySpan<char> format,
            bool hasOffset,
            TimeSpan offset,
            bool explicitOffset)
        {
            for (var index = 0; index < format.Length; index++)
            {
                var token = format[index];
                if (token is '\'' or '"')
                {
                    AppendLiteral(ref output, format, ref index, token);
                    continue;
                }
                if (token == '\\')
                {
                    if (++index >= format.Length)
                    {
                        throw new FormatException();
                    }
                    output.Append(format[index]);
                    continue;
                }
                if (token == '%')
                {
                    if (++index >= format.Length || format[index] == '%')
                    {
                        throw new FormatException();
                    }
                    AppendToken(
                        ref output,
                        value,
                        format[index],
                        1,
                        hasOffset,
                        offset,
                        explicitOffset);
                    continue;
                }
                if (!IsPatternToken(token))
                {
                    output.Append(token);
                    continue;
                }

                var count = token is ':' or '/' or 'K'
                    ? 1
                    : CountRepeat(format, index, token);
                AppendToken(
                    ref output,
                    value,
                    token,
                    count,
                    hasOffset,
                    offset,
                    explicitOffset);
                index += count - 1;
            }
        }

        private static void AppendToken(
            ref Text.ValueStringBuilder output,
            DateTime value,
            char token,
            int count,
            bool hasOffset,
            TimeSpan offset,
            bool explicitOffset)
        {
            switch (token)
            {
                case 'd':
                    if (count <= 2)
                    {
                        AppendNumber(ref output, value.Day, count);
                    }
                    else
                    {
                        output.Append(GetDayName(value.DayOfWeek, abbreviated: count == 3));
                    }
                    return;
                case 'M':
                    if (count <= 2)
                    {
                        AppendNumber(ref output, value.Month, count);
                    }
                    else
                    {
                        output.Append(GetMonthName(value.Month, abbreviated: count == 3));
                    }
                    return;
                case 'y':
                    AppendNumber(
                        ref output,
                        count <= 2 ? value.Year % 100 : value.Year,
                        count);
                    return;
                case 'h':
                    AppendNumber(ref output, ToTwelveHour(value.Hour), Math.Min(count, 2));
                    return;
                case 'H':
                    AppendNumber(ref output, value.Hour, Math.Min(count, 2));
                    return;
                case 'm':
                    AppendNumber(ref output, value.Minute, Math.Min(count, 2));
                    return;
                case 's':
                    AppendNumber(ref output, value.Second, Math.Min(count, 2));
                    return;
                case 'f':
                    AppendFraction(ref output, value.Ticks, count, trim: false);
                    return;
                case 'F':
                    AppendFraction(ref output, value.Ticks, count, trim: true);
                    return;
                case 't':
                    output.Append(value.Hour < 12
                        ? count == 1 ? "A" : "AM"
                        : count == 1 ? "P" : "PM");
                    return;
                case 'z':
                    AppendZone(ref output, offset, count, hasOffset);
                    return;
                case 'K':
                    if (count != 1)
                    {
                        throw new FormatException();
                    }
                    if (value.Kind == DateTimeKind.Utc)
                    {
                        output.Append('Z');
                    }
                    else if (value.Kind == DateTimeKind.Local)
                    {
                        AppendZone(ref output, offset, 3, hasOffset);
                    }
                    else if (explicitOffset)
                    {
                        AppendZone(ref output, offset, 3, hasOffset: true);
                    }
                    return;
                case 'g':
                    output.Append("A.D.");
                    return;
                case ':':
                    output.Append(':');
                    return;
                case '/':
                    output.Append('/');
                    return;
                default:
                    throw new FormatException();
            }
        }

        internal static bool TryParse(
            string value,
            string format,
            out DateTime clock,
            out bool hasOffset,
            out TimeSpan offset,
            out bool utcDesignator) =>
            new Parser(value, format).TryParse(
                out clock,
                out hasOffset,
                out offset,
                out utcDesignator);

        private static void AppendNumber(
            ref Text.ValueStringBuilder output,
            int value,
            int minimumWidth)
        {
            Span<char> digits = stackalloc char[10];
            var position = digits.Length;
            do
            {
                digits[--position] = (char)('0' + value % 10);
                value /= 10;
            }
            while (value != 0);

            var digitCount = digits.Length - position;
            if (minimumWidth > digitCount)
            {
                output.Append('0', minimumWidth - digitCount);
            }
            output.Append(digits[position..]);
        }

        private static void AppendFraction(
            ref Text.ValueStringBuilder output,
            long ticks,
            int count,
            bool trim)
        {
            if (count is < 1 or > 7)
            {
                throw new FormatException();
            }

            var fraction = (int)(ticks % TimeSpan.TicksPerSecond);
            var divisor = 1;
            for (var index = count; index < 7; index++)
            {
                divisor *= 10;
            }
            fraction /= divisor;
            var effectiveDigits = count;
            if (trim)
            {
                while (effectiveDigits > 0 && fraction % 10 == 0)
                {
                    fraction /= 10;
                    effectiveDigits--;
                }
                if (effectiveDigits == 0)
                {
                    if (output.Length > 0 && output[output.Length - 1] == '.')
                    {
                        output.Length--;
                    }
                    return;
                }
            }
            AppendNumber(ref output, fraction, effectiveDigits);
        }

        private static void AppendZone(
            ref Text.ValueStringBuilder output,
            TimeSpan offset,
            int count,
            bool hasOffset)
        {
            if (!hasOffset || count is < 1 or > 3)
            {
                throw new FormatException();
            }
            var totalMinutes = (int)(offset.Ticks / TimeSpan.TicksPerMinute);
            var negative = totalMinutes < 0;
            if (negative)
            {
                totalMinutes = -totalMinutes;
            }
            var hours = totalMinutes / 60;
            var minutes = totalMinutes % 60;
            output.Append(negative ? '-' : '+');
            AppendNumber(ref output, hours, count == 1 ? 1 : 2);
            if (count == 3)
            {
                output.Append(':');
                AppendNumber(ref output, minutes, 2);
            }
        }

        private static void AppendLiteral(
            ref Text.ValueStringBuilder output,
            ReadOnlySpan<char> format,
            ref int index,
            char quote)
        {
            while (++index < format.Length)
            {
                var current = format[index];
                if (current == quote)
                {
                    return;
                }
                if (current == '\\')
                {
                    if (++index >= format.Length)
                    {
                        throw new FormatException();
                    }
                    current = format[index];
                }
                output.Append(current);
            }
            throw new FormatException();
        }

        private static string ReadLiteral(string format, ref int index, char quote)
        {
            var output = string.Empty;
            while (++index < format.Length)
            {
                var current = format[index];
                if (current == quote)
                {
                    return output;
                }
                if (current == '\\')
                {
                    if (++index >= format.Length)
                    {
                        throw new FormatException();
                    }
                    current = format[index];
                }
                output += current;
            }
            throw new FormatException();
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

        private static int ToTwelveHour(int hour)
        {
            hour %= 12;
            return hour == 0 ? 12 : hour;
        }

        internal static string GetDayName(DayOfWeek day, bool abbreviated) => day switch
        {
            DayOfWeek.Sunday => abbreviated ? "Sun" : "Sunday",
            DayOfWeek.Monday => abbreviated ? "Mon" : "Monday",
            DayOfWeek.Tuesday => abbreviated ? "Tue" : "Tuesday",
            DayOfWeek.Wednesday => abbreviated ? "Wed" : "Wednesday",
            DayOfWeek.Thursday => abbreviated ? "Thu" : "Thursday",
            DayOfWeek.Friday => abbreviated ? "Fri" : "Friday",
            _ => abbreviated ? "Sat" : "Saturday",
        };

        internal static string GetMonthName(int month, bool abbreviated) => month switch
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

        private sealed class Parser
        {
            private readonly string _value;
            private readonly string _format;
            private int _valueIndex;
            private int _year = 1;
            private int _month = 1;
            private int _day = 1;
            private int _hour;
            private int _minute;
            private int _second;
            private long _fraction;
            private int _expectedDayOfWeek = -1;
            private bool _twelveHour;
            private bool _postMeridiem;
            private bool _hasDesignator;
            private bool _hasOffset;
            private bool _utcDesignator;
            private int _offsetMinutes;

            internal Parser(string value, string format)
            {
                _value = value;
                _format = format;
            }

            internal bool TryParse(
                out DateTime clock,
                out bool hasOffset,
                out TimeSpan offset,
                out bool utcDesignator)
            {
                clock = default;
                hasOffset = false;
                offset = default;
                utcDesignator = false;
                for (var formatIndex = 0; formatIndex < _format.Length; formatIndex++)
                {
                    var token = _format[formatIndex];
                    if (token is '\'' or '"')
                    {
                        var literal = ReadLiteral(_format, ref formatIndex, token);
                        if (!Match(literal))
                        {
                            return false;
                        }
                        continue;
                    }
                    if (token == '\\')
                    {
                        if (++formatIndex >= _format.Length || !Match(_format[formatIndex]))
                        {
                            return false;
                        }
                        continue;
                    }
                    if (token == '%')
                    {
                        if (++formatIndex >= _format.Length || _format[formatIndex] == '%' ||
                            !TryParseToken(_format[formatIndex], 1))
                        {
                            return false;
                        }
                        continue;
                    }
                    if (!IsPatternToken(token))
                    {
                        if (token == '.' &&
                            formatIndex + 1 < _format.Length &&
                            _format[formatIndex + 1] == 'F' &&
                            (_valueIndex >= _value.Length || _value[_valueIndex] != '.'))
                        {
                            continue;
                        }
                        if (!Match(token))
                        {
                            return false;
                        }
                        continue;
                    }

                    var count = CountRepeat(_format, formatIndex, token);
                    if (!TryParseToken(token, count))
                    {
                        return false;
                    }
                    formatIndex += count - 1;
                }

                if (_valueIndex != _value.Length || !ApplyMeridiem())
                {
                    return false;
                }
                try
                {
                    clock = new DateTime(
                        _year,
                        _month,
                        _day,
                        _hour,
                        _minute,
                        _second,
                        DateTimeKind.Unspecified).AddTicks(_fraction);
                    if (_expectedDayOfWeek >= 0 &&
                        (int)clock.DayOfWeek != _expectedDayOfWeek)
                    {
                        clock = default;
                        return false;
                    }
                    hasOffset = _hasOffset;
                    offset = TimeSpan.FromMinutes(_offsetMinutes);
                    utcDesignator = _utcDesignator;
                    return true;
                }
                catch (Exception)
                {
                    clock = default;
                    return false;
                }
            }

            private bool TryParseToken(char token, int count) => token switch
            {
                'd' => TryParseDay(count),
                'M' => TryParseMonth(count),
                'y' => TryParseYear(count),
                'h' => TryParseHour(count, twelveHour: true),
                'H' => TryParseHour(count, twelveHour: false),
                'm' => TryReadComponent(count, 0, 59, out _minute),
                's' => TryReadComponent(count, 0, 59, out _second),
                'f' => TryParseFraction(count, optional: false),
                'F' => TryParseFraction(count, optional: true),
                't' => TryParseDesignator(count),
                'z' => TryParseZone(count),
                'K' => count == 1 && TryParseKind(),
                'g' => count <= 2 && Match("A.D."),
                ':' => count == 1 && Match(':'),
                '/' => count == 1 && Match('/'),
                _ => false,
            };

            private bool TryParseDay(int count)
            {
                if (count <= 2)
                {
                    return TryReadComponent(count, 1, 31, out _day);
                }
                if (count > 4)
                {
                    return false;
                }
                return TryMatchName(
                    count == 3,
                    isMonth: false,
                    out _expectedDayOfWeek);
            }

            private bool TryParseMonth(int count)
            {
                if (count <= 2)
                {
                    return TryReadComponent(count, 1, 12, out _month);
                }
                if (count > 4)
                {
                    return false;
                }
                return TryMatchName(count == 3, isMonth: true, out _month);
            }

            private bool TryParseYear(int count)
            {
                var minimumDigits = count == 1 ? 1 : count;
                var maximumDigits = count == 1 ? 2 : count;
                if (count > 5 || !TryReadNumber(minimumDigits, maximumDigits, out var year))
                {
                    return false;
                }
                _year = count <= 2
                    ? year <= 49 ? 2000 + year : 1900 + year
                    : year;
                return _year is >= 1 and <= 9999;
            }

            private bool TryParseHour(int count, bool twelveHour)
            {
                if (!TryReadComponent(
                    count,
                    twelveHour ? 1 : 0,
                    twelveHour ? 12 : 23,
                    out _hour))
                {
                    return false;
                }
                _twelveHour = twelveHour;
                return true;
            }

            private bool TryReadComponent(
                int count,
                int minimum,
                int maximum,
                out int component)
            {
                component = 0;
                if (count is < 1 or > 2 ||
                    !TryReadNumber(count == 1 ? 1 : 2, 2, out component))
                {
                    return false;
                }
                return component >= minimum && component <= maximum;
            }

            private bool TryParseFraction(int count, bool optional)
            {
                if (count is < 1 or > 7)
                {
                    return false;
                }
                var start = _valueIndex;
                var maximum = Math.Min(count, _value.Length - _valueIndex);
                var digits = 0;
                while (digits < maximum &&
                    (uint)(_value[_valueIndex + digits] - '0') <= 9)
                {
                    digits++;
                }
                if ((!optional && digits != count) || (optional && digits > count))
                {
                    return false;
                }
                _fraction = 0;
                for (var index = 0; index < digits; index++)
                {
                    _fraction = _fraction * 10 + (_value[start + index] - '0');
                }
                for (var index = digits; index < 7; index++)
                {
                    _fraction *= 10;
                }
                _valueIndex += digits;
                return true;
            }

            private bool TryParseDesignator(int count)
            {
                if (count is < 1 or > 2 || _valueIndex >= _value.Length)
                {
                    return false;
                }
                var postMeridiem = _value[_valueIndex] == 'P';
                if (!postMeridiem && _value[_valueIndex] != 'A')
                {
                    return false;
                }
                _valueIndex++;
                if (count == 2 && !Match('M'))
                {
                    return false;
                }
                _postMeridiem = postMeridiem;
                _hasDesignator = true;
                return true;
            }

            private bool TryParseZone(int count)
            {
                if (count is < 1 or > 3 || !TryReadOffsetSign(out var negative))
                {
                    return false;
                }
                if (!TryReadNumber(count == 1 ? 1 : 2, 2, out var hours))
                {
                    return false;
                }
                var minutes = 0;
                if (count == 3 && (!Match(':') || !TryReadNumber(2, 2, out minutes)))
                {
                    return false;
                }
                return SetOffset(hours, minutes, negative, utcDesignator: false);
            }

            private bool TryParseKind()
            {
                if (_valueIndex == _value.Length)
                {
                    return true;
                }
                if (_value[_valueIndex] == 'Z')
                {
                    _valueIndex++;
                    _hasOffset = true;
                    _utcDesignator = true;
                    _offsetMinutes = 0;
                    return true;
                }
                if (_value[_valueIndex] is not ('+' or '-'))
                {
                    return true;
                }
                if (!TryReadOffsetSign(out var negative) ||
                    !TryReadNumber(2, 2, out var hours) ||
                    !Match(':') ||
                    !TryReadNumber(2, 2, out var minutes))
                {
                    return false;
                }
                return SetOffset(hours, minutes, negative, utcDesignator: false);
            }

            private bool SetOffset(
                int hours,
                int minutes,
                bool negative,
                bool utcDesignator)
            {
                if (hours > 14 || minutes >= 60 || hours == 14 && minutes != 0)
                {
                    return false;
                }
                _offsetMinutes = hours * 60 + minutes;
                if (negative)
                {
                    _offsetMinutes = -_offsetMinutes;
                }
                _hasOffset = true;
                _utcDesignator = utcDesignator;
                return true;
            }

            private bool TryReadOffsetSign(out bool negative)
            {
                negative = false;
                if (_valueIndex >= _value.Length ||
                    _value[_valueIndex] is not ('+' or '-'))
                {
                    return false;
                }
                negative = _value[_valueIndex++] == '-';
                return true;
            }

            private bool ApplyMeridiem()
            {
                if (!_twelveHour)
                {
                    return !_hasDesignator;
                }
                if (_hour == 12)
                {
                    _hour = 0;
                }
                if (_postMeridiem)
                {
                    _hour += 12;
                }
                return true;
            }

            private bool TryMatchName(
                bool abbreviated,
                bool isMonth,
                out int matched)
            {
                var first = isMonth ? 1 : 0;
                var last = isMonth ? 12 : 6;
                for (var candidate = first; candidate <= last; candidate++)
                {
                    var name = isMonth
                        ? GetMonthName(candidate, abbreviated)
                        : GetDayName((DayOfWeek)candidate, abbreviated);
                    if (MatchesAt(name))
                    {
                        _valueIndex += name.Length;
                        matched = candidate;
                        return true;
                    }
                }
                matched = 0;
                return false;
            }

            private bool TryReadNumber(
                int minimumDigits,
                int maximumDigits,
                out int number)
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

            private bool Match(char expected)
            {
                if (_valueIndex >= _value.Length || _value[_valueIndex] != expected)
                {
                    return false;
                }
                _valueIndex++;
                return true;
            }

            private bool Match(string expected)
            {
                if (!MatchesAt(expected))
                {
                    return false;
                }
                _valueIndex += expected.Length;
                return true;
            }

            private bool MatchesAt(string expected)
            {
                if (_valueIndex > _value.Length - expected.Length)
                {
                    return false;
                }
                for (var index = 0; index < expected.Length; index++)
                {
                    if (_value[_valueIndex + index] != expected[index])
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }
}
