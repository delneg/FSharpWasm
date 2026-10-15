// Decimal's representation and observable contract follow System.Decimal.
// Addition, subtraction, multiplication, division, binary conversion and
// scalar rounding are adapted from dotnet/runtime commit
// 811225a482702af7ecc35d817966bc70b88a3a23, Decimal.DecCalc.cs. The remaining
// arithmetic uses a small managed implementation where the upstream port is
// not yet complete.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses those portions under the MIT license.
using System.Runtime.InteropServices;
using System.Text;

namespace System
{
    public enum MidpointRounding
    {
        ToEven = 0,
        AwayFromZero = 1,
        ToZero = 2,
        ToNegativeInfinity = 3,
        ToPositiveInfinity = 4,
    }

    [StructLayout(LayoutKind.Sequential)]
    public readonly partial struct Decimal : IComparable, IConvertible, IComparable<decimal>, IEquatable<decimal>,
        Numerics.IFloatingPoint<decimal>, Numerics.IMinMaxValue<decimal>
    {
        private const uint SignMask = 0x80000000U;
        private const int MaximumScale = 28;

        // Keep this exact order and these exact field types aligned with
        // dotnet/runtime and the native DECIMAL representation.
        private readonly int _flags;
        private readonly uint _hi32;
        private readonly ulong _lo64;

        internal uint High => _hi32;
        internal uint Low => (uint)_lo64;
        internal uint Mid => (uint)(_lo64 >> 32);
        internal ulong Low64 => _lo64;

        public static readonly decimal Zero = new(0);
        public static readonly decimal One = new(1);
        public static readonly decimal MinusOne = new(-1);
        public static readonly decimal MaxValue =
            new(-1, -1, -1, false, 0);
        public static readonly decimal MinValue =
            new(-1, -1, -1, true, 0);

        public Decimal(int value)
        {
            var negative = value < 0;
            _lo64 = negative ? unchecked((uint)-(long)value) : (uint)value;
            _hi32 = 0;
            _flags = negative ? unchecked((int)SignMask) : 0;
        }

        public Decimal(uint value)
        {
            _lo64 = value;
            _hi32 = 0;
            _flags = 0;
        }

        public Decimal(long value)
        {
            var negative = value < 0;
            var magnitude = negative ? unchecked((ulong)-(value + 1)) + 1 : (ulong)value;
            _lo64 = magnitude;
            _hi32 = 0;
            _flags = negative ? unchecked((int)SignMask) : 0;
        }

        public Decimal(ulong value)
        {
            _lo64 = value;
            _hi32 = 0;
            _flags = 0;
        }

        public Decimal(float value) : this((double)value)
        {
        }

        public Decimal(double value)
        {
            this = DecimalMath.FromDouble(value);
        }

        public Decimal(int low, int middle, int high, bool isNegative, byte scale)
        {
            if (scale > MaximumScale)
            {
                throw new ArgumentOutOfRangeException();
            }
            _lo64 = (uint)low | ((ulong)(uint)middle << 32);
            _hi32 = (uint)high;
            _flags = unchecked((int)(((uint)scale << 16) | (isNegative ? SignMask : 0)));
        }

        public Decimal(int[] bits)
        {
            if (bits == null)
            {
                throw new ArgumentNullException();
            }
            if (bits.Length != 4 ||
                ((uint)bits[3] & ~(SignMask | 0x00ff0000U)) != 0 ||
                ((uint)bits[3] >> 16 & 0xffU) > MaximumScale)
            {
                throw new ArgumentException();
            }
            _lo64 = (uint)bits[0] | ((ulong)(uint)bits[1] << 32);
            _hi32 = (uint)bits[2];
            _flags = bits[3];
        }

        public Decimal(ReadOnlySpan<int> bits)
        {
            if (bits.Length != 4 ||
                ((uint)bits[3] & ~(SignMask | 0x00ff0000U)) != 0 ||
                ((uint)bits[3] >> 16 & 0xffU) > MaximumScale)
            {
                throw new ArgumentException();
            }
            _lo64 = (uint)bits[0] | ((ulong)(uint)bits[1] << 32);
            _hi32 = (uint)bits[2];
            _flags = bits[3];
        }

        private Decimal(uint low, uint middle, uint high, bool negative, int scale)
        {
            _lo64 = low | ((ulong)middle << 32);
            _hi32 = high;
            _flags = unchecked((int)(((uint)scale << 16) | (negative ? SignMask : 0)));
        }

        private bool IsNegativeValue => _flags < 0;
        public byte Scale { get => (byte)(_flags >> 16); }
        private bool IsZero => (_lo64 | _hi32) == 0;

        public static decimal Add(decimal left, decimal right)
        {
            DecCalc.DecAddSub(ref AsMutable(ref left), ref AsMutable(ref right), false);
            return left;
        }

        public static decimal Subtract(decimal left, decimal right)
        {
            DecCalc.DecAddSub(ref AsMutable(ref left), ref AsMutable(ref right), true);
            return left;
        }

        public static decimal Multiply(decimal left, decimal right)
        {
            DecCalc.VarDecMul(ref AsMutable(ref left), ref AsMutable(ref right));
            return left;
        }

        public static decimal Divide(decimal left, decimal right)
        {
            DecCalc.VarDecDiv(ref AsMutable(ref left), ref AsMutable(ref right));
            return left;
        }

        public static decimal Remainder(decimal left, decimal right) =>
            DecimalMath.Remainder(left, right);

        public static decimal Negate(decimal value) => new(
            value.Low,
            value.Mid,
            value.High,
            !value.IsNegativeValue,
            value.Scale);

        public static decimal Plus(decimal value) => value;

        public static int Compare(decimal left, decimal right) =>
            DecimalMath.Compare(left, right);

        public static bool Equals(decimal left, decimal right) => Compare(left, right) == 0;

        public bool Equals(decimal other) => Equals(this, other);

        public override bool Equals(object? value) =>
            value is decimal other && Equals(other);

        public override int GetHashCode()
        {
            var value = DecimalMath.TrimTrailingZeroes(this);
            if (value.IsZero)
            {
                return 0;
            }
            return value._flags ^ (int)value.High ^ (int)value.Mid ^ (int)value.Low;
        }

        public int CompareTo(decimal value) => Compare(this, value);

        public int CompareTo(object? value)
        {
            if (value == null)
            {
                return 1;
            }
            if (value is not decimal other)
            {
                throw new ArgumentException();
            }
            return CompareTo(other);
        }

        public static decimal Abs(decimal value) => value.IsNegativeValue ? Negate(value) : value;

        public static int Sign(decimal value) => value.IsZero ? 0 : value.IsNegativeValue ? -1 : 1;

        public static bool IsNegative(decimal value) => value.IsNegativeValue;

        public static decimal Min(decimal left, decimal right) =>
            Compare(left, right) <= 0 ? left : right;

        public static decimal Max(decimal left, decimal right) =>
            Compare(left, right) >= 0 ? left : right;

        public static decimal Clamp(decimal value, decimal minimum, decimal maximum)
        {
            if (Compare(minimum, maximum) > 0)
            {
                throw new ArgumentException();
            }
            return Compare(value, minimum) < 0
                ? minimum
                : Compare(value, maximum) > 0 ? maximum : value;
        }

        public static decimal Truncate(decimal value) =>
            DecimalMath.Round(value, 0, MidpointRounding.ToZero);

        public static decimal Floor(decimal value) =>
            DecimalMath.Round(value, 0, MidpointRounding.ToNegativeInfinity);

        public static decimal Ceiling(decimal value) =>
            DecimalMath.Round(value, 0, MidpointRounding.ToPositiveInfinity);

        public static decimal Round(decimal value) => Round(value, 0, MidpointRounding.ToEven);

        public static decimal Round(decimal value, int decimals) =>
            Round(value, decimals, MidpointRounding.ToEven);

        public static decimal Round(decimal value, MidpointRounding mode) =>
            Round(value, 0, mode);

        public static decimal Round(decimal value, int decimals, MidpointRounding mode)
        {
            if (decimals < 0 || decimals > MaximumScale)
            {
                throw new ArgumentOutOfRangeException();
            }
            if ((uint)mode > (uint)MidpointRounding.ToPositiveInfinity)
            {
                throw new ArgumentException();
            }
            return DecimalMath.Round(value, decimals, mode);
        }

        public static decimal Parse(string value)
        {
            if (value == null)
            {
                throw new ArgumentNullException();
            }
            return Parse(value, Globalization.NumberStyles.Number, null);
        }

        public static bool TryParse(string? value, out decimal result) =>
            TryParse(value, Globalization.NumberStyles.Number, null, out result);

        public override string ToString() => DecimalMath.Format(this);

        public static int[] GetBits(decimal value) =>
            [(int)value.Low, (int)value.Mid, (int)value.High, value._flags];

        public static int GetBits(decimal value, Span<int> destination)
        {
            if (destination.Length <= 3)
            {
                throw new ArgumentException();
            }
            destination[0] = (int)value.Low;
            destination[1] = (int)value.Mid;
            destination[2] = (int)value.High;
            destination[3] = value._flags;
            return 4;
        }

        public static bool TryGetBits(decimal value, Span<int> destination, out int valuesWritten)
        {
            if (destination.Length <= 3)
            {
                valuesWritten = 0;
                return false;
            }
            GetBits(value, destination);
            valuesWritten = 4;
            return true;
        }

        public static decimal FromOACurrency(long cy)
        {
            var negative = cy < 0;
            var magnitude = negative
                ? unchecked((ulong)(-(cy + 1))) + 1
                : (ulong)cy;
            var scale = 4;
            if (magnitude != 0)
            {
                while (scale != 0 && magnitude % 10 == 0)
                {
                    scale--;
                    magnitude /= 10;
                }
            }
            return new decimal((int)magnitude, (int)(magnitude >> 32), 0, negative, (byte)scale);
        }

        public static long ToOACurrency(decimal value)
        {
            var scaled = DecimalMath.Round(Multiply(value, new decimal(10000)), 0, MidpointRounding.ToEven);
            return DecimalMath.ToInt64(scaled);
        }

        public static byte ToByte(decimal value) => checked((byte)DecimalMath.ToUInt32(value));
        public static double ToDouble(decimal value) => DecimalMath.ToDouble(value);
        public static short ToInt16(decimal value) => checked((short)DecimalMath.ToInt32(value));
        public static int ToInt32(decimal value) => DecimalMath.ToInt32(value);
        public static long ToInt64(decimal value) => DecimalMath.ToInt64(value);
        public static sbyte ToSByte(decimal value) => checked((sbyte)DecimalMath.ToInt32(value));
        public static float ToSingle(decimal value) => DecimalMath.ToSingle(value);
        public static ushort ToUInt16(decimal value) => checked((ushort)DecimalMath.ToUInt32(value));
        public static uint ToUInt32(decimal value) => DecimalMath.ToUInt32(value);
        public static ulong ToUInt64(decimal value) => DecimalMath.ToUInt64(value);

        public TypeCode GetTypeCode() => TypeCode.Decimal;

        public static explicit operator decimal(float value) => new(value);
        public static explicit operator decimal(double value) => new(value);
        public static implicit operator decimal(char value) => new((uint)value);
        public static implicit operator decimal(byte value) => new((uint)value);
        public static implicit operator decimal(sbyte value) => new((int)value);
        public static implicit operator decimal(short value) => new((int)value);
        public static implicit operator decimal(ushort value) => new((uint)value);
        public static implicit operator decimal(int value) => new(value);
        public static implicit operator decimal(uint value) => new(value);
        public static implicit operator decimal(long value) => new(value);
        public static implicit operator decimal(ulong value) => new(value);

        public static explicit operator int(decimal value) => DecimalMath.ToInt32(value);
        public static explicit operator uint(decimal value) => DecimalMath.ToUInt32(value);
        public static explicit operator long(decimal value) => DecimalMath.ToInt64(value);
        public static explicit operator ulong(decimal value) => DecimalMath.ToUInt64(value);
        public static explicit operator byte(decimal value) => checked((byte)(uint)value);
        public static explicit operator sbyte(decimal value) => checked((sbyte)(int)value);
        public static explicit operator short(decimal value) => checked((short)(int)value);
        public static explicit operator ushort(decimal value) => checked((ushort)(uint)value);
        public static explicit operator char(decimal value) => checked((char)(uint)value);
        public static explicit operator float(decimal value) => DecimalMath.ToSingle(value);
        public static explicit operator double(decimal value) => DecimalMath.ToDouble(value);

        public static decimal operator +(decimal left, decimal right)
        {
            DecCalc.DecAddSub(ref AsMutable(ref left), ref AsMutable(ref right), false);
            return left;
        }
        public static decimal operator -(decimal left, decimal right)
        {
            DecCalc.DecAddSub(ref AsMutable(ref left), ref AsMutable(ref right), true);
            return left;
        }
        public static decimal operator *(decimal left, decimal right)
        {
            DecCalc.VarDecMul(ref AsMutable(ref left), ref AsMutable(ref right));
            return left;
        }
        public static decimal operator /(decimal left, decimal right)
        {
            DecCalc.VarDecDiv(ref AsMutable(ref left), ref AsMutable(ref right));
            return left;
        }
        public static decimal operator %(decimal left, decimal right) => Remainder(left, right);
        public static decimal operator +(decimal value) => Plus(value);
        public static decimal operator -(decimal value) => Negate(value);
        public static decimal operator ++(decimal value) => Add(value, One);
        public static decimal operator --(decimal value) => Subtract(value, One);
        public static bool operator ==(decimal left, decimal right) => Equals(left, right);
        public static bool operator !=(decimal left, decimal right) => !Equals(left, right);
        public static bool operator <(decimal left, decimal right) => Compare(left, right) < 0;
        public static bool operator <=(decimal left, decimal right) => Compare(left, right) <= 0;
        public static bool operator >(decimal left, decimal right) => Compare(left, right) > 0;
        public static bool operator >=(decimal left, decimal right) => Compare(left, right) >= 0;

        private static class DecimalMath
        {
            internal static decimal Remainder(decimal left, decimal right)
            {
                if (right.IsZero)
                {
                    throw new DivideByZeroException();
                }
                if (left.IsZero)
                {
                    return left;
                }
                var comparison = Compare(Abs(left), Abs(right));
                if (comparison < 0)
                {
                    return left;
                }
                var scale = left.Scale > right.Scale ? left.Scale : right.Scale;
                if (comparison == 0)
                {
                    return Create(default, scale, left.IsNegativeValue, trim: false);
                }
                var leftValue = Coefficient(left);
                var rightValue = Coefficient(right);
                MultiplyPower10(ref leftValue, scale - left.Scale);
                MultiplyPower10(ref rightValue, scale - right.Scale);
                var division = DivRem(leftValue, rightValue);
                var product = Multiply(division.Quotient, rightValue);
                var remainder = Subtract(leftValue, product);
                return Create(remainder, scale, left.IsNegativeValue, trim: false);
            }

            internal static int Compare(decimal left, decimal right)
            {
                if (left.IsZero && right.IsZero)
                {
                    return 0;
                }
                if (left.IsNegativeValue != right.IsNegativeValue)
                {
                    return left.IsNegativeValue ? -1 : 1;
                }
                var scale = left.Scale > right.Scale ? left.Scale : right.Scale;
                var leftValue = Coefficient(left);
                var rightValue = Coefficient(right);
                MultiplyPower10(ref leftValue, scale - left.Scale);
                MultiplyPower10(ref rightValue, scale - right.Scale);
                var result = Compare(leftValue, rightValue);
                return left.IsNegativeValue ? -result : result;
            }

            internal static decimal Round(decimal value, int decimals, MidpointRounding mode)
            {
                if (value.Scale <= decimals)
                {
                    return value;
                }
                DecCalc.InternalRound(
                    ref AsMutable(ref value),
                    (uint)(value.Scale - decimals),
                    mode);
                return value;
            }

            internal static decimal TrimTrailingZeroes(decimal value)
            {
                if (value.IsZero)
                {
                    return new decimal(0);
                }
                var coefficient = Coefficient(value);
                var scale = value.Scale;
                var canTrim = true;
                for (var index = 0; index < MaximumScale; index++)
                {
                    if (canTrim && scale > 0)
                    {
                        var quotient = DivideWord(coefficient, 10, out var remainder);
                        if (remainder == 0)
                        {
                            coefficient = quotient;
                            scale--;
                        }
                        else
                        {
                            canTrim = false;
                        }
                    }
                }
                return CreateUnchecked(coefficient, scale, value.IsNegativeValue);
            }

            internal static int ToInt32(decimal value)
            {
                var negative = value.IsNegativeValue;
                if (value.Scale == 0)
                {
                    if (value.High != 0 || value.Mid != 0 ||
                        !negative && value.Low > int.MaxValue ||
                        negative && value.Low > 0x80000000U)
                    {
                        throw new OverflowException();
                    }
                    return negative
                        ? value.Low == 0x80000000U ? int.MinValue : -(int)value.Low
                        : (int)value.Low;
                }
                var raw = GetLow32(IntegralMagnitude(value));
                if (negative)
                {
                    if (raw > 0x80000000U)
                    {
                        throw new OverflowException();
                    }
                    return raw == 0x80000000U ? int.MinValue : -(int)raw;
                }
                if (raw > int.MaxValue)
                {
                    throw new OverflowException();
                }
                return (int)raw;
            }

            internal static uint ToUInt32(decimal value)
            {
                var negative = value.IsNegativeValue;
                var magnitude = IntegralMagnitude(value);
                if (negative && !IsZero(magnitude))
                {
                    throw new OverflowException();
                }
                return GetLow32(magnitude);
            }

            internal static long ToInt64(decimal value)
            {
                var negative = value.IsNegativeValue;
                var raw = GetLow64(IntegralMagnitude(value));
                if (negative)
                {
                    if (raw > 0x8000000000000000UL)
                    {
                        throw new OverflowException();
                    }
                    return raw == 0x8000000000000000UL ? long.MinValue : -(long)raw;
                }
                if (raw > long.MaxValue)
                {
                    throw new OverflowException();
                }
                return (long)raw;
            }

            internal static ulong ToUInt64(decimal value)
            {
                var negative = value.IsNegativeValue;
                var magnitude = IntegralMagnitude(value);
                if (negative && !IsZero(magnitude))
                {
                    throw new OverflowException();
                }
                return GetLow64(magnitude);
            }

            internal static double ToDouble(decimal value)
            {
                var result = value.High == 0 &&
                    value.Low64 <= 2UL << 52 &&
                    value.Scale <= 22
                        ? (double)value.Low64 / PowerOfTenDouble(value.Scale)
                        : DecimalToFloatingPointExact(
                            value.Low64, value.High, value.Scale, 53);
                return value.IsNegativeValue ? -result : result;
            }

            internal static float ToSingle(decimal value)
            {
                var result = value.High == 0 &&
                    value.Low64 <= 2UL << 23 &&
                    value.Scale <= 10
                        ? (float)((double)value.Low64 / PowerOfTenDouble(value.Scale))
                        : (float)DecimalToFloatingPointExact(
                            value.Low64, value.High, value.Scale, 24);
                return value.IsNegativeValue ? -result : result;
            }

            private static double PowerOfTenDouble(int exponent) => Pow10DoubleTable[exponent];

            private static ReadOnlySpan<double> Pow10DoubleTable =>
            [
                1d, 1e1, 1e2, 1e3, 1e4, 1e5, 1e6, 1e7,
                1e8, 1e9, 1e10, 1e11, 1e12, 1e13, 1e14, 1e15,
                1e16, 1e17, 1e18, 1e19, 1e20, 1e21, 1e22,
            ];

            private static double DecimalToFloatingPointExact(ulong low64, uint high, int scale, int significandBits)
            {
                var mantissa = new UInt128(high, low64);
                if (mantissa == UInt128.Zero)
                {
                    return 0.0;
                }
                var divisor = PowerOfFive(scale);
                var mantissaBits = 128 - (int)UInt128.LeadingZeroCount(mantissa);
                var divisorBits = 128 - (int)UInt128.LeadingZeroCount(divisor);

                // Port of Decimal.DecCalc's .NET 11 exact path. Divide by 5^scale
                // with one guard bit and at most one extra bit. Both shifted
                // operands fit UInt128 for the full 96-bit/28-scale domain.
                var shift = (significandBits + 1) - (mantissaBits - divisorBits);
                UInt128 numerator, denominator;
                if (shift >= 0)
                {
                    numerator = mantissa << shift;
                    denominator = divisor;
                }
                else
                {
                    numerator = mantissa;
                    denominator = divisor << -shift;
                }
                var (quotient, remainder) = UInt128.DivRem(numerator, denominator);
                var quotientBits = 128 - (int)UInt128.LeadingZeroCount(quotient);
                var drop = quotientBits - significandBits;
                var keep = (ulong)(quotient >> drop);
                var roundBits = (ulong)(quotient & ((UInt128.One << drop) - 1));
                var half = 1UL << (drop - 1);
                var sticky = remainder != UInt128.Zero || (roundBits & (half - 1)) != 0;

                bool roundUp;
                if (roundBits > half)
                {
                    roundUp = true;
                }
                else if (roundBits < half)
                {
                    roundUp = false;
                }
                else
                {
                    roundUp = sticky || (keep & 1) != 0;
                }
                if (roundUp && ++keep == (1UL << significandBits))
                {
                    keep >>= 1;
                    drop++;
                }
                // Decimal's range is normal and finite in both formats. Scaling
                // the already-rounded significand by this power of two is exact.
                return Math.ScaleB((double)keep, drop - shift - scale);
            }

            internal static decimal FromDouble(double value)
            {
                // .NET 11 exact conversion profile. A float widens to double
                // without changing its exact value before entering this method.
                if (value == 0.0)
                {
                    return default;
                }
                if (!double.IsFinite(value))
                {
                    throw new OverflowException();
                }
                var bits = BitConverter.DoubleToUInt64Bits(value);
                var negative = (bits >> 63) != 0;
                var significand = bits & 0x000fffffffffffffUL;
                var biasedExponent = (int)((bits >> 52) & 0x7ff);
                var exponent = 1 - 1023 - 52;
                if (biasedExponent != 0)
                {
                    significand |= 1UL << 52;
                    exponent = biasedExponent - 1023 - 52;
                }

                // An odd significand makes -exponent the exact fractional scale.
                var trailingZeros = Numerics.BitOperations.TrailingZeroCount(significand);
                significand >>= trailingZeros;
                exponent += trailingZeros;
                UInt128 mantissa;
                int scale;
                if (exponent >= 0)
                {
                    var significandBits = 64 - Numerics.BitOperations.LeadingZeroCount(significand);
                    if (significandBits + exponent > 96)
                    {
                        throw new OverflowException();
                    }
                    mantissa = (UInt128)significand << exponent;
                    scale = 0;
                }
                else
                {
                    var fractionalDigits = -exponent;
                    scale = Math.Min(fractionalDigits, MaximumScale);
                    while (true)
                    {
                        // The product is below 2^119. Always round the exact
                        // numerator again when reducing scale, never a rounded value.
                        var numerator = (UInt128)significand * PowerOfFive(scale);
                        mantissa = RoundShiftRightEven(numerator, fractionalDigits - scale);
                        if ((mantissa >> 96) == UInt128.Zero)
                        {
                            break;
                        }
                        scale--;
                    }
                }

                if (mantissa == UInt128.Zero)
                {
                    return default;
                }
                return new decimal((uint)mantissa, (uint)(mantissa >> 32),
                    (uint)(mantissa >> 64), negative, scale);
            }

            private static UInt128 PowerOfFive(int exponent)
            {
                ReadOnlySpan<ulong> powers =
                [
                    1, 5, 25, 125, 625, 3125, 15625, 78125, 390625, 1953125,
                    9765625, 48828125, 244140625, 1220703125, 6103515625, 30517578125,
                    152587890625, 762939453125, 3814697265625, 19073486328125,
                    95367431640625, 476837158203125, 2384185791015625, 11920928955078125,
                    59604644775390625, 298023223876953125, 1490116119384765625,
                    7450580596923828125, 359414837200037393
                ];
                return new UInt128(exponent == MaximumScale ? 2U : 0U, powers[exponent]);
            }

            private static UInt128 RoundShiftRightEven(UInt128 value, int shift)
            {
                if (shift <= 0)
                {
                    return value;
                }
                if (shift >= 128)
                {
                    return shift == 128 && value > (UInt128.One << 127)
                        ? UInt128.One : UInt128.Zero;
                }
                var quotient = value >> shift;
                var remainder = value & ((UInt128.One << shift) - UInt128.One);
                var half = UInt128.One << (shift - 1);
                if (remainder > half || (remainder == half && (quotient & UInt128.One) != UInt128.Zero))
                {
                    quotient++;
                }
                return quotient;
            }

            internal static bool TryParse(
                string? text,
                out decimal result,
                out bool overflow)
            {
                if (text == null)
                {
                    result = Zero;
                    overflow = false;
                    return false;
                }
                return TryParse(text.AsSpan(), out result, out overflow);
            }

            internal static bool TryParse(
                ReadOnlySpan<char> text,
                out decimal result,
                out bool overflow)
            {
                result = Zero;
                overflow = false;
                if (text.Length == 0)
                {
                    return false;
                }
                var start = 0;
                var end = text.Length;
                for (var scan = 0; scan < text.Length; scan++)
                {
                    if (scan == start && text[scan] == ' ')
                    {
                        start++;
                    }
                }
                for (var scan = text.Length - 1; scan >= 0; scan--)
                {
                    if (scan == end - 1 && text[scan] == ' ')
                    {
                        end--;
                    }
                }
                if (start == end)
                {
                    return false;
                }
                var index = start;
                var negative = false;
                if (text[index] == '+' || text[index] == '-')
                {
                    negative = text[index++] == '-';
                    if (index == end)
                    {
                        return false;
                    }
                }
                var coefficient = ZeroValue();
                var digits = 0;
                var fractionalDigits = 0;
                var decimalPoint = false;
                var mantissa = true;
                var coefficientValid = true;
                for (var scan = index; scan < end; scan++)
                {
                    var character = text[scan];
                    if (mantissa && character >= '0' && character <= '9')
                    {
                        if (coefficientValid)
                        {
                            coefficientValid = TryAppendDigit(
                                ref coefficient,
                                (uint)(character - '0'));
                        }
                        digits++;
                        if (decimalPoint)
                        {
                            fractionalDigits++;
                        }
                        index = scan + 1;
                    }
                    else if (mantissa && character == '.' && !decimalPoint)
                    {
                        decimalPoint = true;
                        index = scan + 1;
                    }
                    else
                    {
                        mantissa = false;
                    }
                }
                if (digits == 0)
                {
                    return false;
                }
                if (!coefficientValid)
                {
                    overflow = true;
                    return false;
                }
                var exponent = 0;
                if (index < end && (text[index] == 'e' || text[index] == 'E'))
                {
                    index++;
                    var exponentNegative = false;
                    if (index < end && (text[index] == '+' || text[index] == '-'))
                    {
                        exponentNegative = text[index++] == '-';
                    }
                    if (index == end)
                    {
                        return false;
                    }
                    var exponentDigits = 0;
                    var exponentActive = true;
                    for (var scan = index; scan < end; scan++)
                    {
                        var character = text[scan];
                        if (exponentActive && character >= '0' && character <= '9')
                        {
                            if (exponent < 1_000)
                            {
                                exponent = exponent * 10 + character - '0';
                            }
                            exponentDigits++;
                            index = scan + 1;
                        }
                        else
                        {
                            exponentActive = false;
                        }
                    }
                    if (exponentDigits == 0)
                    {
                        return false;
                    }
                    if (exponentNegative)
                    {
                        exponent = -exponent;
                    }
                }
                if (index != end)
                {
                    return false;
                }
                var scale = fractionalDigits - exponent;
                if (scale < 0)
                {
                    MultiplyPower10(ref coefficient, -scale);
                    scale = 0;
                }
                try
                {
                    result = Create(coefficient, scale, negative, trim: false);
                    return true;
                }
                catch (OverflowException)
                {
                    overflow = true;
                    return false;
                }
            }

            internal struct WideInteger
            {
                internal uint Part0;
                internal uint Part1;
                internal uint Part2;
                internal uint Part3;
                internal uint Part4;
                internal uint Part5;

                internal int Length => 6;

                internal uint this[int index]
                {
                    get
                    {
                        if (index == 0) return Part0;
                        if (index == 1) return Part1;
                        if (index == 2) return Part2;
                        if (index == 3) return Part3;
                        if (index == 4) return Part4;
                        if (index == 5) return Part5;
                        return 0;
                    }
                    set
                    {
                        if (index == 0) Part0 = value;
                        else if (index == 1) Part1 = value;
                        else if (index == 2) Part2 = value;
                        else if (index == 3) Part3 = value;
                        else if (index == 4) Part4 = value;
                        else if (index == 5) Part5 = value;
                    }
                }
            }

            private static bool TryAppendDigit(ref WideInteger coefficient, uint digit)
            {
                try
                {
                    MultiplyWordInPlace(ref coefficient, 10);
                    AddWordInPlace(ref coefficient, digit);
                    return true;
                }
                catch (OverflowException)
                {
                    return false;
                }
            }

            internal static string Format(decimal value)
            {
                var builder = new ValueStringBuilder(32);
                AppendFormat(ref builder, value);
                return builder.ToString();
            }

            internal static void AppendFormat(scoped ref ValueStringBuilder builder, decimal value)
            {
                var coefficient = Coefficient(value);
                var digits = ToDigits(coefficient);
                var scale = value.Scale;
                var integerDigits = digits.Length - scale;
                if (value.IsNegativeValue)
                {
                    builder.Append('-');
                }
                if (scale == 0)
                {
                    builder.Append(digits);
                    return;
                }
                if (integerDigits <= 0)
                {
                    builder.Append('0');
                    builder.Append('.');
                    for (var zero = integerDigits; zero < 0; zero++)
                    {
                        builder.Append('0');
                    }
                    builder.Append(digits);
                    return;
                }
                for (var index = 0; index < digits.Length; index++)
                {
                    if (index == integerDigits)
                    {
                        builder.Append('.');
                    }
                    builder.Append(digits[index]);
                }
            }

            private static decimal Create(WideInteger coefficient, int scale, bool negative, bool trim)
            {
                if (scale < 0)
                {
                    MultiplyPower10(ref coefficient, -scale);
                    scale = 0;
                }
                if (scale <= MaximumScale && Fits96(coefficient))
                {
                    var exact = CreateUnchecked(coefficient, scale, negative);
                    return trim ? TrimTrailingZeroes(exact) : exact;
                }
                if (scale == 0)
                {
                    throw new OverflowException();
                }
                var minimumRemoval = scale > MaximumScale ? scale - MaximumScale : 0;
                var result = CreateAtRemoval(coefficient, scale, negative, minimumRemoval);
                return trim ? TrimTrailingZeroes(result) : result;
            }

            private static decimal CreateAtRemoval(
                WideInteger coefficient,
                int scale,
                bool negative,
                int removal)
            {
                if (removal > scale)
                {
                    throw new OverflowException();
                }
                var divisor = Power10(removal);
                var division = DivRem(coefficient, divisor);
                RoundQuotient(ref division.Quotient, division.Remainder, divisor, negative,
                    MidpointRounding.ToEven);
                return Fits96(division.Quotient)
                    ? CreateUnchecked(division.Quotient, scale - removal, negative)
                    : CreateAtRemoval(coefficient, scale, negative, removal + 1);
            }

            private static decimal CreateUnchecked(WideInteger coefficient, int scale, bool negative) =>
                new(
                    coefficient[0],
                    coefficient.Length > 1 ? coefficient[1] : 0,
                    coefficient.Length > 2 ? coefficient[2] : 0,
                    negative,
                    scale);

            internal static WideInteger Coefficient(decimal value)
            {
                var low = value.Low;
                var middle = value.Mid;
                var high = value.High;
                var result = new WideInteger();
                result[0] = low;
                result[1] = middle;
                result[2] = high;
                return result;
            }

            private static WideInteger IntegralMagnitude(decimal value)
            {
                var scale = value.Scale;
                var coefficient = Coefficient(value);
                if (scale == 0)
                {
                    return coefficient;
                }
                return DivRem(coefficient, Power10(scale)).Quotient;
            }

            private static void RoundQuotient(
                ref WideInteger quotient,
                WideInteger remainder,
                WideInteger divisor,
                bool negative,
                MidpointRounding mode)
            {
                if (IsZero(remainder))
                {
                    return;
                }
                var increment = mode switch
                {
                    MidpointRounding.ToZero => false,
                    MidpointRounding.ToNegativeInfinity => negative,
                    MidpointRounding.ToPositiveInfinity => !negative,
                    MidpointRounding.AwayFromZero => Compare(MultiplyWord(remainder, 2), divisor) >= 0,
                    _ => Compare(MultiplyWord(remainder, 2), divisor) > 0 ||
                         Compare(MultiplyWord(remainder, 2), divisor) == 0 &&
                         (quotient[0] & 1) != 0,
                };
                if (increment)
                {
                    AddWordInPlace(ref quotient, 1);
                }
            }

            internal static char[] ToDigits(WideInteger value)
            {
                if (IsZero(value))
                {
                    return ['0'];
                }
                var work = Copy(value);
                var reversed = new char[work.Length * 10];
                var length = 0;
                while (!IsZero(work))
                {
                    work = DivideWord(work, 10, out var remainder);
                    reversed[length++] = (char)('0' + remainder);
                }
                var result = new char[length];
                for (var index = 0; index < length; index++)
                {
                    result[index] = reversed[length - index - 1];
                }
                return result;
            }

            private static void CopyDigits(char[] source, char[] destination, ref int offset)
            {
                for (var index = 0; index < source.Length; index++)
                {
                    destination[offset++] = source[index];
                }
            }

            private static WideInteger Power10(int power)
            {
                var result = CopyWithCapacity(FromUInt64(1), 6);
                MultiplyPower10(ref result, power);
                return result;
            }

            private static void MultiplyPower10(ref WideInteger value, int power)
            {
                if (!TryMultiplyPower10(ref value, power))
                {
                    throw new OverflowException();
                }
            }

            private static bool TryMultiplyPower10(ref WideInteger value, int power)
            {
                var result = value;
                while (power >= 9)
                {
                    if (!TryMultiplyWordInPlace(ref result, 1_000_000_000U))
                    {
                        return false;
                    }
                    power -= 9;
                }
                if (power != 0 &&
                    !TryMultiplyWordInPlace(ref result, Power10Word(power)))
                {
                    return false;
                }
                value = result;
                return true;
            }

            private static uint Power10Word(int power)
            {
                if (power == 1) return 10U;
                if (power == 2) return 100U;
                if (power == 3) return 1_000U;
                if (power == 4) return 10_000U;
                if (power == 5) return 100_000U;
                if (power == 6) return 1_000_000U;
                if (power == 7) return 10_000_000U;
                if (power == 8) return 100_000_000U;
                if (power == 9) return 1_000_000_000U;
                return 1U;
            }

            private static ulong Power10UInt64(int power)
            {
                var result = 1UL;
                for (var index = 0; index < power; index++)
                {
                    result *= 10;
                }
                return result;
            }

            private static WideInteger Add(WideInteger left, WideInteger right)
            {
                var leftLength = SignificantLength(left);
                var rightLength = SignificantLength(right);
                var result = new WideInteger();
                var length = (leftLength > rightLength ? leftLength : rightLength) + 1;
                var carry = 0UL;
                for (var index = 0; index < length - 1; index++)
                {
                    var sum = carry + left[index] + right[index];
                    result[index] = (uint)sum;
                    carry = sum >> 32;
                }
                if (length > result.Length && carry != 0)
                {
                    throw new OverflowException();
                }
                if (length <= result.Length)
                {
                    result[length - 1] = (uint)carry;
                }
                return Trim(result);
            }

            private static WideInteger Subtract(WideInteger left, WideInteger right)
            {
                var result = new WideInteger();
                var borrow = 0UL;
                for (var index = 0; index < left.Length; index++)
                {
                    var subtrahend = right[index] + borrow;
                    var value = left[index];
                    result[index] = (uint)(value - subtrahend);
                    borrow = value < subtrahend ? 1UL : 0UL;
                }
                return Trim(result);
            }

            private static WideInteger Multiply(WideInteger left, WideInteger right)
            {
                var leftLength = SignificantLength(left);
                var rightLength = SignificantLength(right);
                var result = new WideInteger();
                for (var leftIndex = 0; leftIndex < leftLength; leftIndex++)
                {
                    var carry = 0UL;
                    for (var rightIndex = 0; rightIndex < rightLength; rightIndex++)
                    {
                        var index = leftIndex + rightIndex;
                        var product = (ulong)left[leftIndex] * right[rightIndex] +
                            result[index] + carry;
                        result[index] = (uint)product;
                        carry = product >> 32;
                    }
                    var destination = leftIndex + rightLength;
                    AddCarry(ref result, destination, carry);
                }
                return Trim(result);
            }

            private static void AddCarry(
                ref WideInteger value,
                int index,
                ulong carry)
            {
                if (carry == 0)
                {
                    return;
                }
                if (index >= value.Length)
                {
                    throw new OverflowException();
                }
                var sum = value[index] + carry;
                value[index] = (uint)sum;
                AddCarry(ref value, index + 1, sum >> 32);
            }

            private static WideInteger MultiplyWord(WideInteger value, uint multiplier)
            {
                var result = CopyWithCapacity(value, SignificantLength(value) + 1);
                MultiplyWordInPlace(ref result, multiplier);
                return Trim(result);
            }

            private static void MultiplyWordInPlace(ref WideInteger value, uint multiplier)
            {
                if (!TryMultiplyWordInPlace(ref value, multiplier))
                {
                    throw new OverflowException();
                }
            }

            private static bool TryMultiplyWordInPlace(
                ref WideInteger value,
                uint multiplier)
            {
                var carry = 0UL;
                for (var index = 0; index < value.Length; index++)
                {
                    var product = (ulong)value[index] * multiplier + carry;
                    value[index] = (uint)product;
                    carry = product >> 32;
                }
                if (carry != 0)
                {
                    return false;
                }
                return true;
            }

            private static void AddWordInPlace(ref WideInteger value, uint addition)
            {
                var carry = (ulong)addition;
                for (var index = 0; index < value.Length; index++)
                {
                    if (carry != 0)
                    {
                        var sum = value[index] + carry;
                        value[index] = (uint)sum;
                        carry = sum >> 32;
                    }
                }
                if (carry != 0)
                {
                    throw new OverflowException();
                }
            }

            internal static WideInteger DivideWord(WideInteger value, uint divisor, out uint remainder)
            {
                var result = new WideInteger();
                var carry = 0UL;
                for (var index = value.Length - 1; index >= 0; index--)
                {
                    var dividend = (carry << 32) | value[index];
                    result[index] = (uint)(dividend / divisor);
                    carry = dividend % divisor;
                }
                remainder = (uint)carry;
                return Trim(result);
            }

            private struct DivisionResult(WideInteger quotient, WideInteger remainder)
            {
                internal WideInteger Quotient = quotient;
                internal WideInteger Remainder = remainder;
            }

            private static DivisionResult DivRem(WideInteger dividend, WideInteger divisor)
            {
                if (IsZero(divisor))
                {
                    throw new DivideByZeroException();
                }
                if (Compare(dividend, divisor) < 0)
                {
                    return new DivisionResult(ZeroValue(), Copy(dividend));
                }
                var quotient = new WideInteger();
                var remainder = new WideInteger();
                var bitCount = BitLength(dividend);
                for (var bit = bitCount - 1; bit >= 0; bit--)
                {
                    ShiftLeftOne(ref remainder);
                    if ((dividend[bit >> 5] & (1U << (bit & 31))) != 0)
                    {
                        remainder[0] |= 1;
                    }
                    if (Compare(remainder, divisor) >= 0)
                    {
                        SubtractInPlace(ref remainder, divisor);
                        quotient[bit >> 5] |= 1U << (bit & 31);
                    }
                }
                return new DivisionResult(Trim(quotient), Trim(remainder));
            }

            private static void SubtractInPlace(ref WideInteger left, WideInteger right)
            {
                var borrow = 0UL;
                for (var index = 0; index < left.Length; index++)
                {
                    var subtrahend = right[index] + borrow;
                    var value = left[index];
                    left[index] = (uint)(value - subtrahend);
                    borrow = value < subtrahend ? 1UL : 0UL;
                }
            }

            private static void ShiftLeftOne(ref WideInteger value)
            {
                var carry = 0U;
                for (var index = 0; index < value.Length; index++)
                {
                    var next = value[index] >> 31;
                    value[index] = (value[index] << 1) | carry;
                    carry = next;
                }
                if (carry != 0)
                {
                    throw new OverflowException();
                }
            }

            private static int Compare(WideInteger left, WideInteger right)
            {
                var leftLength = SignificantLength(left);
                var rightLength = SignificantLength(right);
                if (leftLength != rightLength)
                {
                    return leftLength < rightLength ? -1 : 1;
                }
                var result = 0;
                for (var index = leftLength - 1; index >= 0; index--)
                {
                    if (result == 0 && left[index] != right[index])
                    {
                        result = left[index] < right[index] ? -1 : 1;
                    }
                }
                return result;
            }

            private static int BitLength(WideInteger value)
            {
                var length = SignificantLength(value);
                var high = value[length - 1];
                var bits = (length - 1) * 32;
                while (high != 0)
                {
                    high >>= 1;
                    bits++;
                }
                return bits;
            }

            private static bool Fits96(WideInteger value) => SignificantLength(value) <= 3;

            private static bool HasMoreThan(WideInteger value, int length) =>
                SignificantLength(value) > length;

            private static uint GetLow32(WideInteger value)
            {
                if (HasMoreThan(value, 1))
                {
                    throw new OverflowException();
                }
                return value[0];
            }

            private static ulong GetLow64(WideInteger value)
            {
                if (HasMoreThan(value, 2))
                {
                    throw new OverflowException();
                }
                return value[0] | ((ulong)(value.Length > 1 ? value[1] : 0) << 32);
            }

            private static bool IsZero(WideInteger value) => SignificantLength(value) == 1 && value[0] == 0;

            private static WideInteger ZeroValue() => new();

            private static WideInteger FromUInt64(ulong value) => new()
            {
                Part0 = (uint)value,
                Part1 = (uint)(value >> 32),
            };

            private static WideInteger Copy(WideInteger value) => value;

            private static WideInteger CopyWithCapacity(WideInteger value, int capacity) => value;

            private static WideInteger Trim(WideInteger value) => value;

            private static int SignificantLength(WideInteger value)
            {
                var length = value.Length;
                for (var index = value.Length - 1; index > 0; index--)
                {
                    if (length == index + 1 && value[index] == 0)
                    {
                        length--;
                    }
                }
                return length;
            }
        }
    }
}
