// Adapted from dotnet/runtime System.Private.CoreLib BitConverter.cs and the
// System.Buffers binary primitive contracts. The upstream implementation is
// licensed under MIT.
// Copyright (c) .NET Foundation and Contributors.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace System
{
    public static class BitConverter
    {
        // NetWasm targets little-endian Wasm32 and Wasm64 memories.
        public static readonly bool IsLittleEndian = true;

        public static byte[] GetBytes(bool value) => [value ? (byte)1 : (byte)0];

        public static bool TryWriteBytes(Span<byte> destination, bool value)
        {
            if (destination.Length < 1)
            {
                return false;
            }
            destination[0] = value ? (byte)1 : (byte)0;
            return true;
        }

        public static byte[] GetBytes(char value)
        {
            var bytes = new byte[2];
            TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, char value)
        {
            if (destination.Length < 2)
            {
                return false;
            }
            destination[0] = (byte)value;
            destination[1] = (byte)(value >> 8);
            return true;
        }

        public static byte[] GetBytes(short value)
        {
            var bytes = new byte[2];
            TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, short value) =>
            TryWriteBytes(destination, unchecked((ushort)value));

        public static byte[] GetBytes(ushort value)
        {
            var bytes = new byte[2];
            TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, ushort value)
        {
            if (destination.Length < 2)
            {
                return false;
            }
            destination[0] = (byte)value;
            destination[1] = (byte)(value >> 8);
            return true;
        }

        public static byte[] GetBytes(int value)
        {
            var bytes = new byte[4];
            TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, int value) =>
            TryWriteBytes(destination, unchecked((uint)value));

        public static byte[] GetBytes(uint value)
        {
            var bytes = new byte[4];
            TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, uint value)
        {
            if (destination.Length < 4)
            {
                return false;
            }
            destination[0] = (byte)value;
            destination[1] = (byte)(value >> 8);
            destination[2] = (byte)(value >> 16);
            destination[3] = (byte)(value >> 24);
            return true;
        }

        public static byte[] GetBytes(long value)
        {
            var bytes = new byte[8];
            TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, long value) =>
            TryWriteBytes(destination, unchecked((ulong)value));

        public static byte[] GetBytes(ulong value)
        {
            var bytes = new byte[8];
            TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, ulong value)
        {
            if (destination.Length < 8)
            {
                return false;
            }
            destination[0] = (byte)value;
            destination[1] = (byte)(value >> 8);
            destination[2] = (byte)(value >> 16);
            destination[3] = (byte)(value >> 24);
            destination[4] = (byte)(value >> 32);
            destination[5] = (byte)(value >> 40);
            destination[6] = (byte)(value >> 48);
            destination[7] = (byte)(value >> 56);
            return true;
        }

        public static byte[] GetBytes(float value)
        {
            var bytes = new byte[4];
            TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, float value) =>
            TryWriteBytes(destination, SingleToUInt32Bits(value));

        public static byte[] GetBytes(double value)
        {
            var bytes = new byte[8];
            TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, double value) =>
            TryWriteBytes(destination, DoubleToUInt64Bits(value));

        public static byte[] GetBytes(Half value)
        {
            var bytes = new byte[2];
            TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, Half value) =>
            TryWriteBytes(destination, HalfToUInt16Bits(value));

        public static byte[] GetBytes(System.Numerics.BFloat16 value)
        {
            var bytes = new byte[2];
            TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, System.Numerics.BFloat16 value) =>
            TryWriteBytes(destination, BFloat16ToUInt16Bits(value));

        public static byte[] GetBytes(Int128 value)
        {
            var bytes = new byte[Int128.Size];
            TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, Int128 value)
        {
            if (destination.Length < Int128.Size)
            {
                return false;
            }
            TryWriteBytes(destination, value.Lower);
            TryWriteBytes(destination[8..], value.Upper);
            return true;
        }

        public static byte[] GetBytes(UInt128 value)
        {
            var bytes = new byte[UInt128.Size];
            TryWriteBytes(bytes, value);
            return bytes;
        }

        public static bool TryWriteBytes(Span<byte> destination, UInt128 value)
        {
            if (destination.Length < UInt128.Size)
            {
                return false;
            }
            TryWriteBytes(destination, value.Lower);
            TryWriteBytes(destination[8..], value.Upper);
            return true;
        }

        public static char ToChar(byte[] value, int startIndex) =>
            unchecked((char)ToUInt16(value, startIndex));

        public static char ToChar(ReadOnlySpan<byte> value) => unchecked((char)ToUInt16(value));

        public static short ToInt16(byte[] value, int startIndex) =>
            unchecked((short)ToUInt16(value, startIndex));

        public static short ToInt16(ReadOnlySpan<byte> value) =>
            unchecked((short)ToUInt16(value));

        public static ushort ToUInt16(byte[] value, int startIndex)
        {
            ValidateArray(value, startIndex, 2);
            return unchecked((ushort)(value[startIndex] | value[startIndex + 1] << 8));
        }

        public static ushort ToUInt16(ReadOnlySpan<byte> value)
        {
            RequireSpan(value, 2);
            return unchecked((ushort)(value[0] | value[1] << 8));
        }

        public static int ToInt32(byte[] value, int startIndex) =>
            unchecked((int)ToUInt32(value, startIndex));

        public static int ToInt32(ReadOnlySpan<byte> value) =>
            unchecked((int)ToUInt32(value));

        public static uint ToUInt32(byte[] value, int startIndex)
        {
            ValidateArray(value, startIndex, 4);
            return unchecked((uint)(value[startIndex] |
                value[startIndex + 1] << 8 |
                value[startIndex + 2] << 16 |
                value[startIndex + 3] << 24));
        }

        public static uint ToUInt32(ReadOnlySpan<byte> value)
        {
            RequireSpan(value, 4);
            return unchecked((uint)(value[0] |
                value[1] << 8 |
                value[2] << 16 |
                value[3] << 24));
        }

        public static long ToInt64(byte[] value, int startIndex) =>
            unchecked((long)ToUInt64(value, startIndex));

        public static long ToInt64(ReadOnlySpan<byte> value) =>
            unchecked((long)ToUInt64(value));

        public static ulong ToUInt64(byte[] value, int startIndex)
        {
            ValidateArray(value, startIndex, 8);
            return (ulong)value[startIndex] |
                (ulong)value[startIndex + 1] << 8 |
                (ulong)value[startIndex + 2] << 16 |
                (ulong)value[startIndex + 3] << 24 |
                (ulong)value[startIndex + 4] << 32 |
                (ulong)value[startIndex + 5] << 40 |
                (ulong)value[startIndex + 6] << 48 |
                (ulong)value[startIndex + 7] << 56;
        }

        public static ulong ToUInt64(ReadOnlySpan<byte> value)
        {
            RequireSpan(value, 8);
            return (ulong)value[0] |
                (ulong)value[1] << 8 |
                (ulong)value[2] << 16 |
                (ulong)value[3] << 24 |
                (ulong)value[4] << 32 |
                (ulong)value[5] << 40 |
                (ulong)value[6] << 48 |
                (ulong)value[7] << 56;
        }

        public static float ToSingle(byte[] value, int startIndex) =>
            UInt32BitsToSingle(ToUInt32(value, startIndex));

        public static float ToSingle(ReadOnlySpan<byte> value) =>
            UInt32BitsToSingle(ToUInt32(value));

        public static double ToDouble(byte[] value, int startIndex) =>
            UInt64BitsToDouble(ToUInt64(value, startIndex));

        public static double ToDouble(ReadOnlySpan<byte> value) =>
            UInt64BitsToDouble(ToUInt64(value));

        public static Half ToHalf(byte[] value, int startIndex) =>
            Int16BitsToHalf(ToInt16(value, startIndex));

        public static Half ToHalf(ReadOnlySpan<byte> value) =>
            Int16BitsToHalf(ToInt16(value));

        public static System.Numerics.BFloat16 ToBFloat16(byte[] value, int startIndex) =>
            Int16BitsToBFloat16(ToInt16(value, startIndex));

        public static System.Numerics.BFloat16 ToBFloat16(ReadOnlySpan<byte> value) =>
            Int16BitsToBFloat16(ToInt16(value));

        public static Int128 ToInt128(byte[] value, int startIndex)
        {
            ValidateArray(value, startIndex, Int128.Size);
            return new Int128(ToUInt64(value, startIndex + 8), ToUInt64(value, startIndex));
        }

        public static Int128 ToInt128(ReadOnlySpan<byte> value)
        {
            RequireSpan(value, Int128.Size);
            return new Int128(ToUInt64(value[8..]), ToUInt64(value));
        }

        public static UInt128 ToUInt128(byte[] value, int startIndex)
        {
            ValidateArray(value, startIndex, UInt128.Size);
            return new UInt128(ToUInt64(value, startIndex + 8), ToUInt64(value, startIndex));
        }

        public static UInt128 ToUInt128(ReadOnlySpan<byte> value)
        {
            RequireSpan(value, UInt128.Size);
            return new UInt128(ToUInt64(value[8..]), ToUInt64(value));
        }

        public static bool ToBoolean(byte[] value, int startIndex)
        {
            if (value == null)
            {
                throw new ArgumentNullException();
            }
            if ((uint)startIndex >= (uint)value.Length)
            {
                throw new ArgumentOutOfRangeException();
            }
            return value[startIndex] != 0;
        }

        public static bool ToBoolean(ReadOnlySpan<byte> value)
        {
            RequireSpan(value, 1);
            return value[0] != 0;
        }

        public static string ToString(byte[] value) =>
            value == null ? throw new ArgumentNullException() : ToString(value, 0, value.Length);

        public static string ToString(byte[] value, int startIndex) =>
            value == null ? throw new ArgumentNullException() :
            ToString(value, startIndex, value.Length - startIndex);

        public static string ToString(byte[] value, int startIndex, int length)
        {
            if (value == null)
            {
                throw new ArgumentNullException();
            }
            if (startIndex < 0 || (startIndex >= value.Length && startIndex > 0))
            {
                throw new ArgumentOutOfRangeException();
            }
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (startIndex > value.Length - length)
            {
                throw new ArgumentException();
            }
            if (length == 0)
            {
                return string.Empty;
            }
            if (length > int.MaxValue / 3)
            {
                throw new ArgumentOutOfRangeException();
            }

            var result = new char[length * 3 - 1];
            var destination = 0;
            for (var index = 0; index < length; index++)
            {
                if (index != 0)
                {
                    result[destination++] = '-';
                }
                var valueByte = value[startIndex + index];
                result[destination++] = Hex((byte)(valueByte >> 4));
                result[destination++] = Hex((byte)(valueByte & 0xf));
            }
            return string.Create(result);
        }

        public static long DoubleToInt64Bits(double value) => 0;
        public static double Int64BitsToDouble(long value) => 0;
        public static int SingleToInt32Bits(float value) => 0;
        public static float Int32BitsToSingle(int value) => 0;

        public static short HalfToInt16Bits(Half value) => unchecked((short)value._value);
        public static Half Int16BitsToHalf(short value) => new(unchecked((ushort)value));
        public static short BFloat16ToInt16Bits(System.Numerics.BFloat16 value) => unchecked((short)value._value);
        public static System.Numerics.BFloat16 Int16BitsToBFloat16(short value) => new(unchecked((ushort)value));

        public static ulong DoubleToUInt64Bits(double value) =>
            unchecked((ulong)DoubleToInt64Bits(value));

        public static double UInt64BitsToDouble(ulong value) =>
            Int64BitsToDouble(unchecked((long)value));

        public static uint SingleToUInt32Bits(float value) =>
            unchecked((uint)SingleToInt32Bits(value));

        public static float UInt32BitsToSingle(uint value) =>
            Int32BitsToSingle(unchecked((int)value));

        public static ushort HalfToUInt16Bits(Half value) => value._value;
        public static Half UInt16BitsToHalf(ushort value) => new(value);
        public static ushort BFloat16ToUInt16Bits(System.Numerics.BFloat16 value) => value._value;
        public static System.Numerics.BFloat16 UInt16BitsToBFloat16(ushort value) => new(value);

        private static void ValidateArray(byte[] value, int startIndex, int size)
        {
            if (value == null)
            {
                throw new ArgumentNullException();
            }
            if ((uint)startIndex >= (uint)value.Length)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (startIndex > value.Length - size)
            {
                throw new ArgumentException();
            }
        }

        private static void RequireSpan(ReadOnlySpan<byte> value, int size)
        {
            if (value.Length < size)
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        private static char Hex(byte value) => value < 10 ? (char)('0' + value) : (char)('A' + value - 10);
    }
}

namespace System.Buffers
{
    public enum OperationStatus
    {
        Done,
        DestinationTooSmall,
        NeedMoreData,
        InvalidData,
    }

    /// <summary>
    /// Represents a standard formatting string without using an actual String. A StandardFormat consists of a character (such as 'G', 'D' or 'X')
    /// and an optional precision ranging from 0..99, or the special value NoPrecision.
    /// </summary>
    public readonly struct StandardFormat : IEquatable<StandardFormat>
    {
        /// <summary>
        /// Precision values for format that don't use a precision, or for when the precision is to be unspecified.
        /// </summary>
        public const byte NoPrecision = byte.MaxValue;

        /// <summary>
        /// The maximum valid precision value.
        /// </summary>
        public const byte MaxPrecision = 99;

        private readonly byte _format;
        private readonly byte _precision;

        /// <summary>
        /// The character component of the format.
        /// </summary>
        public char Symbol => (char)_format;

        /// <summary>
        /// The precision component of the format. Ranges from 0..9 or the special value NoPrecision.
        /// </summary>
        public byte Precision => _precision;

        /// <summary>
        /// true if Precision is a value other than NoPrecision
        /// </summary>
        public bool HasPrecision => _precision != NoPrecision;

        /// <summary>Gets the precision if one was specified; otherwise, 0.</summary>
        internal byte PrecisionOrZero => _precision != NoPrecision ? _precision : (byte)0;

        /// <summary>
        /// true if the StandardFormat == default(StandardFormat)
        /// </summary>
        public bool IsDefault => (_format | _precision) == 0;

        /// <summary>
        /// Create a StandardFormat.
        /// </summary>
        /// <param name="symbol">A type-specific formatting character such as 'G', 'D' or 'X'</param>
        /// <param name="precision">An optional precision ranging from 0..9 or the special value NoPrecision (the default)</param>
        public StandardFormat(char symbol, byte precision = NoPrecision)
        {
            if (precision != NoPrecision && precision > MaxPrecision)
                ThrowHelper.ThrowArgumentOutOfRangeException_PrecisionTooLarge();
            if (symbol != (byte)symbol)
                ThrowHelper.ThrowArgumentOutOfRangeException_SymbolDoesNotFit();

            _format = (byte)symbol;
            _precision = precision;
        }

        /// <summary>
        /// Converts a character to a StandardFormat using the NoPrecision precision.
        /// </summary>
        public static implicit operator StandardFormat(char symbol) => new StandardFormat(symbol);

        /// <summary>
        /// Converts a <see cref="ReadOnlySpan{Char}"/> into a StandardFormat
        /// </summary>
        public static StandardFormat Parse([StringSyntax(StringSyntaxAttribute.NumericFormat)] ReadOnlySpan<char> format)
        {
            ParseHelper(format, out StandardFormat standardFormat, throws: true);

            return standardFormat;
        }

        /// <summary>
        /// Converts a classic .NET format string into a StandardFormat
        /// </summary>
        public static StandardFormat Parse([StringSyntax(StringSyntaxAttribute.NumericFormat)] string? format) => format == null ? default : Parse(format.AsSpan());

        /// <summary>
        /// Tries to convert a <see cref="ReadOnlySpan{Char}"/> into a StandardFormat. A return value indicates whether the conversion succeeded or failed.
        /// </summary>
        public static bool TryParse([StringSyntax(StringSyntaxAttribute.NumericFormat)] ReadOnlySpan<char> format, out StandardFormat result)
        {
            return ParseHelper(format, out result);
        }

        private static bool ParseHelper(ReadOnlySpan<char> format, out StandardFormat standardFormat, bool throws = false)
        {
            standardFormat = default;

            if (format.Length == 0)
                return true;

            char symbol = format[0];
            byte precision;
            if (format.Length == 1)
            {
                precision = NoPrecision;
            }
            else
            {
                uint parsedPrecision = 0;
                for (int srcIndex = 1; srcIndex < format.Length; srcIndex++)
                {
                    uint digit = format[srcIndex] - 48u; // '0'
                    if (digit > 9)
                    {
                        return throws ? throw new FormatException(SR.Format(SR.Argument_CannotParsePrecision, MaxPrecision)) : false;
                    }
                    parsedPrecision = parsedPrecision * 10 + digit;
                    if (parsedPrecision > MaxPrecision)
                    {
                        return throws ? throw new FormatException(SR.Format(SR.Argument_PrecisionTooLarge, MaxPrecision)) : false;
                    }
                }

                precision = (byte)parsedPrecision;
            }

            standardFormat = new StandardFormat(symbol, precision);
            return true;
        }

        /// <summary>
        /// Returns true if both the Symbol and Precision are equal.
        /// </summary>
        public override bool Equals([NotNullWhen(true)] object? obj) => obj is StandardFormat other && Equals(other);

        /// <summary>
        /// Compute a hash code.
        /// </summary>
        public override int GetHashCode() => _format.GetHashCode() ^ _precision.GetHashCode();

        /// <summary>
        /// Returns true if both the Symbol and Precision are equal.
        /// </summary>
        public bool Equals(StandardFormat other) => _format == other._format && _precision == other._precision;

        /// <summary>
        /// Returns the format in classic .NET format.
        /// </summary>
        public override string ToString() => new string(Format(stackalloc char[FormatStringLength]));

        /// <summary>The exact buffer length required by <see cref="Format"/>.</summary>
        internal const int FormatStringLength = 3;

        /// <summary>
        /// Formats the format in classic .NET format.
        /// </summary>
        internal Span<char> Format(Span<char> destination)
        {
            Debug.Assert(destination.Length == FormatStringLength);

            char symbol = Symbol;

            if (symbol != default && destination.Length == FormatStringLength)
            {
                destination[0] = symbol;

                uint precision = Precision;
                if (precision != NoPrecision)
                {
                    // Note that Precision is stored as a byte, so in theory it could contain
                    // values > MaxPrecision (99).  But all supported mechanisms for creating a
                    // StandardFormat limit values to being <= MaxPrecision, so the only way a value
                    // could be larger than that is if unsafe code or the equivalent were used
                    // to force a larger invalid value in, in which case we don't need to
                    // guarantee such an invalid value is properly roundtripped through here;
                    // we just need to make sure things aren't corrupted further.

                    if (precision >= 10)
                    {
                        uint div;
                        (div, precision) = Math.DivRem(precision, 10);
                        destination[1] = (char)('0' + div % 10);
                        destination[2] = (char)('0' + precision);
                        return destination;
                    }

                    destination[1] = (char)('0' + precision);
                    return destination.Slice(0, 2);
                }

                return destination.Slice(0, 1);
            }

            return default;
        }

        /// <summary>
        /// Returns true if both the Symbol and Precision are equal.
        /// </summary>
        public static bool operator ==(StandardFormat left, StandardFormat right) => left.Equals(right);

        /// <summary>
        /// Returns false if both the Symbol and Precision are equal.
        /// </summary>
        public static bool operator !=(StandardFormat left, StandardFormat right) => !left.Equals(right);
    }
}

namespace System.Buffers.Binary
{
    public static class BinaryPrimitives
    {
        public static short ReadInt16LittleEndian(ReadOnlySpan<byte> source) =>
            unchecked((short)ReadUInt16LittleEndian(source));

        public static short ReadInt16BigEndian(ReadOnlySpan<byte> source) =>
            unchecked((short)ReadUInt16BigEndian(source));

        public static ushort ReadUInt16LittleEndian(ReadOnlySpan<byte> source)
        {
            Require(source, 2);
            return unchecked((ushort)(source[0] | source[1] << 8));
        }

        public static ushort ReadUInt16BigEndian(ReadOnlySpan<byte> source)
        {
            Require(source, 2);
            return unchecked((ushort)(source[1] | source[0] << 8));
        }

        public static int ReadInt32LittleEndian(ReadOnlySpan<byte> source) =>
            unchecked((int)ReadUInt32LittleEndian(source));

        public static int ReadInt32BigEndian(ReadOnlySpan<byte> source) =>
            unchecked((int)ReadUInt32BigEndian(source));

        public static uint ReadUInt32LittleEndian(ReadOnlySpan<byte> source)
        {
            Require(source, 4);
            return (uint)source[0] | (uint)source[1] << 8 |
                (uint)source[2] << 16 | (uint)source[3] << 24;
        }

        public static uint ReadUInt32BigEndian(ReadOnlySpan<byte> source)
        {
            Require(source, 4);
            return (uint)source[3] | (uint)source[2] << 8 |
                (uint)source[1] << 16 | (uint)source[0] << 24;
        }

        public static long ReadInt64LittleEndian(ReadOnlySpan<byte> source) =>
            unchecked((long)ReadUInt64LittleEndian(source));

        public static long ReadInt64BigEndian(ReadOnlySpan<byte> source) =>
            unchecked((long)ReadUInt64BigEndian(source));

        public static ulong ReadUInt64LittleEndian(ReadOnlySpan<byte> source)
        {
            Require(source, 8);
            return (ulong)source[0] | (ulong)source[1] << 8 |
                (ulong)source[2] << 16 | (ulong)source[3] << 24 |
                (ulong)source[4] << 32 | (ulong)source[5] << 40 |
                (ulong)source[6] << 48 | (ulong)source[7] << 56;
        }

        public static ulong ReadUInt64BigEndian(ReadOnlySpan<byte> source)
        {
            Require(source, 8);
            return (ulong)source[7] | (ulong)source[6] << 8 |
                (ulong)source[5] << 16 | (ulong)source[4] << 24 |
                (ulong)source[3] << 32 | (ulong)source[2] << 40 |
                (ulong)source[1] << 48 | (ulong)source[0] << 56;
        }

        public static float ReadSingleLittleEndian(ReadOnlySpan<byte> source) =>
            BitConverter.UInt32BitsToSingle(ReadUInt32LittleEndian(source));
        public static float ReadSingleBigEndian(ReadOnlySpan<byte> source) =>
            BitConverter.UInt32BitsToSingle(ReadUInt32BigEndian(source));
        public static double ReadDoubleLittleEndian(ReadOnlySpan<byte> source) =>
            BitConverter.UInt64BitsToDouble(ReadUInt64LittleEndian(source));
        public static double ReadDoubleBigEndian(ReadOnlySpan<byte> source) =>
            BitConverter.UInt64BitsToDouble(ReadUInt64BigEndian(source));

        public static nint ReadIntPtrLittleEndian(ReadOnlySpan<byte> source) =>
            IntPtr.Size == 4 ? (nint)ReadInt32LittleEndian(source) : (nint)ReadInt64LittleEndian(source);
        public static nint ReadIntPtrBigEndian(ReadOnlySpan<byte> source) =>
            IntPtr.Size == 4 ? (nint)ReadInt32BigEndian(source) : (nint)ReadInt64BigEndian(source);
        public static nuint ReadUIntPtrLittleEndian(ReadOnlySpan<byte> source) =>
            UIntPtr.Size == 4 ? (nuint)ReadUInt32LittleEndian(source) : (nuint)ReadUInt64LittleEndian(source);
        public static nuint ReadUIntPtrBigEndian(ReadOnlySpan<byte> source) =>
            UIntPtr.Size == 4 ? (nuint)ReadUInt32BigEndian(source) : (nuint)ReadUInt64BigEndian(source);

        public static bool TryReadInt16LittleEndian(ReadOnlySpan<byte> source, out short value) { if (source.Length < 2) { value = default; return false; } value = ReadInt16LittleEndian(source); return true; }
        public static bool TryReadInt16BigEndian(ReadOnlySpan<byte> source, out short value) { if (source.Length < 2) { value = default; return false; } value = ReadInt16BigEndian(source); return true; }
        public static bool TryReadUInt16LittleEndian(ReadOnlySpan<byte> source, out ushort value) { if (source.Length < 2) { value = default; return false; } value = ReadUInt16LittleEndian(source); return true; }
        public static bool TryReadUInt16BigEndian(ReadOnlySpan<byte> source, out ushort value) { if (source.Length < 2) { value = default; return false; } value = ReadUInt16BigEndian(source); return true; }
        public static bool TryReadInt32LittleEndian(ReadOnlySpan<byte> source, out int value) { if (source.Length < 4) { value = default; return false; } value = ReadInt32LittleEndian(source); return true; }
        public static bool TryReadInt32BigEndian(ReadOnlySpan<byte> source, out int value) { if (source.Length < 4) { value = default; return false; } value = ReadInt32BigEndian(source); return true; }
        public static bool TryReadUInt32LittleEndian(ReadOnlySpan<byte> source, out uint value) { if (source.Length < 4) { value = default; return false; } value = ReadUInt32LittleEndian(source); return true; }
        public static bool TryReadUInt32BigEndian(ReadOnlySpan<byte> source, out uint value) { if (source.Length < 4) { value = default; return false; } value = ReadUInt32BigEndian(source); return true; }
        public static bool TryReadInt64LittleEndian(ReadOnlySpan<byte> source, out long value) { if (source.Length < 8) { value = default; return false; } value = ReadInt64LittleEndian(source); return true; }
        public static bool TryReadInt64BigEndian(ReadOnlySpan<byte> source, out long value) { if (source.Length < 8) { value = default; return false; } value = ReadInt64BigEndian(source); return true; }
        public static bool TryReadUInt64LittleEndian(ReadOnlySpan<byte> source, out ulong value) { if (source.Length < 8) { value = default; return false; } value = ReadUInt64LittleEndian(source); return true; }
        public static bool TryReadUInt64BigEndian(ReadOnlySpan<byte> source, out ulong value) { if (source.Length < 8) { value = default; return false; } value = ReadUInt64BigEndian(source); return true; }
        public static bool TryReadSingleLittleEndian(ReadOnlySpan<byte> source, out float value) { if (source.Length < 4) { value = default; return false; } value = ReadSingleLittleEndian(source); return true; }
        public static bool TryReadSingleBigEndian(ReadOnlySpan<byte> source, out float value) { if (source.Length < 4) { value = default; return false; } value = ReadSingleBigEndian(source); return true; }
        public static bool TryReadDoubleLittleEndian(ReadOnlySpan<byte> source, out double value) { if (source.Length < 8) { value = default; return false; } value = ReadDoubleLittleEndian(source); return true; }
        public static bool TryReadDoubleBigEndian(ReadOnlySpan<byte> source, out double value) { if (source.Length < 8) { value = default; return false; } value = ReadDoubleBigEndian(source); return true; }
        public static bool TryReadHalfLittleEndian(ReadOnlySpan<byte> source, out Half value) { if (source.Length < 2) { value = default; return false; } value = ReadHalfLittleEndian(source); return true; }
        public static bool TryReadHalfBigEndian(ReadOnlySpan<byte> source, out Half value) { if (source.Length < 2) { value = default; return false; } value = ReadHalfBigEndian(source); return true; }
        public static bool TryReadBFloat16LittleEndian(ReadOnlySpan<byte> source, out System.Numerics.BFloat16 value) { if (source.Length < 2) { value = default; return false; } value = ReadBFloat16LittleEndian(source); return true; }
        public static bool TryReadBFloat16BigEndian(ReadOnlySpan<byte> source, out System.Numerics.BFloat16 value) { if (source.Length < 2) { value = default; return false; } value = ReadBFloat16BigEndian(source); return true; }
        public static bool TryReadInt128LittleEndian(ReadOnlySpan<byte> source, out Int128 value) { if (source.Length < Int128.Size) { value = default; return false; } value = ReadInt128LittleEndian(source); return true; }
        public static bool TryReadInt128BigEndian(ReadOnlySpan<byte> source, out Int128 value) { if (source.Length < Int128.Size) { value = default; return false; } value = ReadInt128BigEndian(source); return true; }
        public static bool TryReadUInt128LittleEndian(ReadOnlySpan<byte> source, out UInt128 value) { if (source.Length < UInt128.Size) { value = default; return false; } value = ReadUInt128LittleEndian(source); return true; }
        public static bool TryReadUInt128BigEndian(ReadOnlySpan<byte> source, out UInt128 value) { if (source.Length < UInt128.Size) { value = default; return false; } value = ReadUInt128BigEndian(source); return true; }
        public static bool TryReadIntPtrLittleEndian(ReadOnlySpan<byte> source, out nint value) { if (source.Length < IntPtr.Size) { value = default; return false; } value = ReadIntPtrLittleEndian(source); return true; }
        public static bool TryReadIntPtrBigEndian(ReadOnlySpan<byte> source, out nint value) { if (source.Length < IntPtr.Size) { value = default; return false; } value = ReadIntPtrBigEndian(source); return true; }
        public static bool TryReadUIntPtrLittleEndian(ReadOnlySpan<byte> source, out nuint value) { if (source.Length < UIntPtr.Size) { value = default; return false; } value = ReadUIntPtrLittleEndian(source); return true; }
        public static bool TryReadUIntPtrBigEndian(ReadOnlySpan<byte> source, out nuint value) { if (source.Length < UIntPtr.Size) { value = default; return false; } value = ReadUIntPtrBigEndian(source); return true; }

        public static void WriteInt16LittleEndian(Span<byte> destination, short value) => WriteUInt16LittleEndian(destination, unchecked((ushort)value));
        public static void WriteInt16BigEndian(Span<byte> destination, short value) => WriteUInt16BigEndian(destination, unchecked((ushort)value));
        public static void WriteUInt16LittleEndian(Span<byte> destination, ushort value)
        {
            Require(destination, 2);
            destination[0] = (byte)value;
            destination[1] = (byte)(value >> 8);
        }
        public static void WriteUInt16BigEndian(Span<byte> destination, ushort value)
        {
            Require(destination, 2);
            destination[0] = (byte)(value >> 8);
            destination[1] = (byte)value;
        }

        public static void WriteInt32LittleEndian(Span<byte> destination, int value) => WriteUInt32LittleEndian(destination, unchecked((uint)value));
        public static void WriteInt32BigEndian(Span<byte> destination, int value) => WriteUInt32BigEndian(destination, unchecked((uint)value));
        public static void WriteUInt32LittleEndian(Span<byte> destination, uint value)
        {
            Require(destination, 4);
            destination[0] = (byte)value;
            destination[1] = (byte)(value >> 8);
            destination[2] = (byte)(value >> 16);
            destination[3] = (byte)(value >> 24);
        }
        public static void WriteUInt32BigEndian(Span<byte> destination, uint value)
        {
            Require(destination, 4);
            destination[0] = (byte)(value >> 24);
            destination[1] = (byte)(value >> 16);
            destination[2] = (byte)(value >> 8);
            destination[3] = (byte)value;
        }

        public static void WriteInt64LittleEndian(Span<byte> destination, long value) => WriteUInt64LittleEndian(destination, unchecked((ulong)value));
        public static void WriteInt64BigEndian(Span<byte> destination, long value) => WriteUInt64BigEndian(destination, unchecked((ulong)value));
        public static void WriteUInt64LittleEndian(Span<byte> destination, ulong value)
        {
            Require(destination, 8);
            destination[0] = (byte)value;
            destination[1] = (byte)(value >> 8);
            destination[2] = (byte)(value >> 16);
            destination[3] = (byte)(value >> 24);
            destination[4] = (byte)(value >> 32);
            destination[5] = (byte)(value >> 40);
            destination[6] = (byte)(value >> 48);
            destination[7] = (byte)(value >> 56);
        }
        public static void WriteUInt64BigEndian(Span<byte> destination, ulong value)
        {
            Require(destination, 8);
            destination[0] = (byte)(value >> 56);
            destination[1] = (byte)(value >> 48);
            destination[2] = (byte)(value >> 40);
            destination[3] = (byte)(value >> 32);
            destination[4] = (byte)(value >> 24);
            destination[5] = (byte)(value >> 16);
            destination[6] = (byte)(value >> 8);
            destination[7] = (byte)value;
        }

        public static void WriteSingleLittleEndian(Span<byte> destination, float value) => WriteUInt32LittleEndian(destination, BitConverter.SingleToUInt32Bits(value));
        public static void WriteHalfLittleEndian(Span<byte> destination, Half value) => WriteUInt16LittleEndian(destination, BitConverter.HalfToUInt16Bits(value));
        public static void WriteHalfBigEndian(Span<byte> destination, Half value) => WriteUInt16BigEndian(destination, BitConverter.HalfToUInt16Bits(value));
        public static void WriteBFloat16LittleEndian(Span<byte> destination, System.Numerics.BFloat16 value) => WriteUInt16LittleEndian(destination, BitConverter.BFloat16ToUInt16Bits(value));
        public static void WriteBFloat16BigEndian(Span<byte> destination, System.Numerics.BFloat16 value) => WriteUInt16BigEndian(destination, BitConverter.BFloat16ToUInt16Bits(value));
        public static void WriteInt128LittleEndian(Span<byte> destination, Int128 value)
        {
            Require(destination, Int128.Size);
            WriteUInt64LittleEndian(destination, value.Lower);
            WriteUInt64LittleEndian(destination[8..], value.Upper);
        }
        public static void WriteInt128BigEndian(Span<byte> destination, Int128 value)
        {
            Require(destination, Int128.Size);
            WriteUInt64BigEndian(destination, value.Upper);
            WriteUInt64BigEndian(destination[8..], value.Lower);
        }
        public static void WriteUInt128LittleEndian(Span<byte> destination, UInt128 value)
        {
            Require(destination, UInt128.Size);
            WriteUInt64LittleEndian(destination, value.Lower);
            WriteUInt64LittleEndian(destination[8..], value.Upper);
        }
        public static void WriteUInt128BigEndian(Span<byte> destination, UInt128 value)
        {
            Require(destination, UInt128.Size);
            WriteUInt64BigEndian(destination, value.Upper);
            WriteUInt64BigEndian(destination[8..], value.Lower);
        }
        public static void WriteSingleBigEndian(Span<byte> destination, float value) => WriteUInt32BigEndian(destination, BitConverter.SingleToUInt32Bits(value));
        public static void WriteDoubleLittleEndian(Span<byte> destination, double value) => WriteUInt64LittleEndian(destination, BitConverter.DoubleToUInt64Bits(value));
        public static void WriteDoubleBigEndian(Span<byte> destination, double value) => WriteUInt64BigEndian(destination, BitConverter.DoubleToUInt64Bits(value));
        public static void WriteIntPtrLittleEndian(Span<byte> destination, nint value) { if (IntPtr.Size == 4) WriteInt32LittleEndian(destination, (int)value); else WriteInt64LittleEndian(destination, (long)value); }
        public static void WriteIntPtrBigEndian(Span<byte> destination, nint value) { if (IntPtr.Size == 4) WriteInt32BigEndian(destination, (int)value); else WriteInt64BigEndian(destination, (long)value); }
        public static void WriteUIntPtrLittleEndian(Span<byte> destination, nuint value) { if (UIntPtr.Size == 4) WriteUInt32LittleEndian(destination, (uint)value); else WriteUInt64LittleEndian(destination, (ulong)value); }
        public static void WriteUIntPtrBigEndian(Span<byte> destination, nuint value) { if (UIntPtr.Size == 4) WriteUInt32BigEndian(destination, (uint)value); else WriteUInt64BigEndian(destination, (ulong)value); }

        public static Half ReadHalfLittleEndian(ReadOnlySpan<byte> source) =>
            BitConverter.Int16BitsToHalf(ReadInt16LittleEndian(source));
        public static Half ReadHalfBigEndian(ReadOnlySpan<byte> source) =>
            BitConverter.Int16BitsToHalf(ReadInt16BigEndian(source));
        public static System.Numerics.BFloat16 ReadBFloat16LittleEndian(ReadOnlySpan<byte> source) =>
            BitConverter.Int16BitsToBFloat16(ReadInt16LittleEndian(source));
        public static System.Numerics.BFloat16 ReadBFloat16BigEndian(ReadOnlySpan<byte> source) =>
            BitConverter.Int16BitsToBFloat16(ReadInt16BigEndian(source));

        public static Int128 ReadInt128LittleEndian(ReadOnlySpan<byte> source)
        {
            Require(source, Int128.Size);
            return new Int128(ReadUInt64LittleEndian(source[8..]), ReadUInt64LittleEndian(source));
        }

        public static Int128 ReadInt128BigEndian(ReadOnlySpan<byte> source)
        {
            Require(source, Int128.Size);
            return new Int128(ReadUInt64BigEndian(source), ReadUInt64BigEndian(source[8..]));
        }

        public static UInt128 ReadUInt128LittleEndian(ReadOnlySpan<byte> source)
        {
            Require(source, UInt128.Size);
            return new UInt128(ReadUInt64LittleEndian(source[8..]), ReadUInt64LittleEndian(source));
        }

        public static UInt128 ReadUInt128BigEndian(ReadOnlySpan<byte> source)
        {
            Require(source, UInt128.Size);
            return new UInt128(ReadUInt64BigEndian(source), ReadUInt64BigEndian(source[8..]));
        }

        public static bool TryWriteInt16LittleEndian(Span<byte> destination, short value) { if (destination.Length < 2) return false; WriteInt16LittleEndian(destination, value); return true; }
        public static bool TryWriteInt16BigEndian(Span<byte> destination, short value) { if (destination.Length < 2) return false; WriteInt16BigEndian(destination, value); return true; }
        public static bool TryWriteUInt16LittleEndian(Span<byte> destination, ushort value) { if (destination.Length < 2) return false; WriteUInt16LittleEndian(destination, value); return true; }
        public static bool TryWriteUInt16BigEndian(Span<byte> destination, ushort value) { if (destination.Length < 2) return false; WriteUInt16BigEndian(destination, value); return true; }
        public static bool TryWriteInt32LittleEndian(Span<byte> destination, int value) { if (destination.Length < 4) return false; WriteInt32LittleEndian(destination, value); return true; }
        public static bool TryWriteInt32BigEndian(Span<byte> destination, int value) { if (destination.Length < 4) return false; WriteInt32BigEndian(destination, value); return true; }
        public static bool TryWriteUInt32LittleEndian(Span<byte> destination, uint value) { if (destination.Length < 4) return false; WriteUInt32LittleEndian(destination, value); return true; }
        public static bool TryWriteUInt32BigEndian(Span<byte> destination, uint value) { if (destination.Length < 4) return false; WriteUInt32BigEndian(destination, value); return true; }
        public static bool TryWriteInt64LittleEndian(Span<byte> destination, long value) { if (destination.Length < 8) return false; WriteInt64LittleEndian(destination, value); return true; }
        public static bool TryWriteInt64BigEndian(Span<byte> destination, long value) { if (destination.Length < 8) return false; WriteInt64BigEndian(destination, value); return true; }
        public static bool TryWriteUInt64LittleEndian(Span<byte> destination, ulong value) { if (destination.Length < 8) return false; WriteUInt64LittleEndian(destination, value); return true; }
        public static bool TryWriteUInt64BigEndian(Span<byte> destination, ulong value) { if (destination.Length < 8) return false; WriteUInt64BigEndian(destination, value); return true; }
        public static bool TryWriteSingleLittleEndian(Span<byte> destination, float value) { if (destination.Length < 4) return false; WriteSingleLittleEndian(destination, value); return true; }
        public static bool TryWriteSingleBigEndian(Span<byte> destination, float value) { if (destination.Length < 4) return false; WriteSingleBigEndian(destination, value); return true; }
        public static bool TryWriteDoubleLittleEndian(Span<byte> destination, double value) { if (destination.Length < 8) return false; WriteDoubleLittleEndian(destination, value); return true; }
        public static bool TryWriteDoubleBigEndian(Span<byte> destination, double value) { if (destination.Length < 8) return false; WriteDoubleBigEndian(destination, value); return true; }
        public static bool TryWriteHalfLittleEndian(Span<byte> destination, Half value) { if (destination.Length < 2) return false; WriteHalfLittleEndian(destination, value); return true; }
        public static bool TryWriteHalfBigEndian(Span<byte> destination, Half value) { if (destination.Length < 2) return false; WriteHalfBigEndian(destination, value); return true; }
        public static bool TryWriteBFloat16LittleEndian(Span<byte> destination, System.Numerics.BFloat16 value) { if (destination.Length < 2) return false; WriteBFloat16LittleEndian(destination, value); return true; }
        public static bool TryWriteBFloat16BigEndian(Span<byte> destination, System.Numerics.BFloat16 value) { if (destination.Length < 2) return false; WriteBFloat16BigEndian(destination, value); return true; }
        public static bool TryWriteInt128LittleEndian(Span<byte> destination, Int128 value) { if (destination.Length < Int128.Size) return false; WriteInt128LittleEndian(destination, value); return true; }
        public static bool TryWriteInt128BigEndian(Span<byte> destination, Int128 value) { if (destination.Length < Int128.Size) return false; WriteInt128BigEndian(destination, value); return true; }
        public static bool TryWriteUInt128LittleEndian(Span<byte> destination, UInt128 value) { if (destination.Length < UInt128.Size) return false; WriteUInt128LittleEndian(destination, value); return true; }
        public static bool TryWriteUInt128BigEndian(Span<byte> destination, UInt128 value) { if (destination.Length < UInt128.Size) return false; WriteUInt128BigEndian(destination, value); return true; }
        public static bool TryWriteIntPtrLittleEndian(Span<byte> destination, nint value) { if (destination.Length < IntPtr.Size) return false; WriteIntPtrLittleEndian(destination, value); return true; }
        public static bool TryWriteIntPtrBigEndian(Span<byte> destination, nint value) { if (destination.Length < IntPtr.Size) return false; WriteIntPtrBigEndian(destination, value); return true; }
        public static bool TryWriteUIntPtrLittleEndian(Span<byte> destination, nuint value) { if (destination.Length < UIntPtr.Size) return false; WriteUIntPtrLittleEndian(destination, value); return true; }
        public static bool TryWriteUIntPtrBigEndian(Span<byte> destination, nuint value) { if (destination.Length < UIntPtr.Size) return false; WriteUIntPtrBigEndian(destination, value); return true; }

        public static byte ReverseEndianness(byte value) => value;
        public static sbyte ReverseEndianness(sbyte value) => value;
        public static ushort ReverseEndianness(ushort value) => unchecked((ushort)(value >> 8 | value << 8));
        internal static char ReverseEndianness(char value) => (char)ReverseEndianness((ushort)value);
        public static short ReverseEndianness(short value) => unchecked((short)ReverseEndianness((ushort)value));
        public static uint ReverseEndianness(uint value) => value >> 24 | (value >> 8 & 0xff00) | (value << 8 & 0xff0000) | value << 24;
        public static int ReverseEndianness(int value) => unchecked((int)ReverseEndianness((uint)value));
        public static ulong ReverseEndianness(ulong value) => (ulong)ReverseEndianness((uint)value) << 32 | ReverseEndianness((uint)(value >> 32));
        public static long ReverseEndianness(long value) => unchecked((long)ReverseEndianness((ulong)value));
        public static nint ReverseEndianness(nint value) => IntPtr.Size == 4 ? (nint)ReverseEndianness((int)value) : (nint)ReverseEndianness((long)value);
        public static nuint ReverseEndianness(nuint value) => UIntPtr.Size == 4 ? (nuint)ReverseEndianness((uint)value) : (nuint)ReverseEndianness((ulong)value);
        public static Int128 ReverseEndianness(Int128 value) =>
            new(ReverseEndianness(value.Lower), ReverseEndianness(value.Upper));
        public static UInt128 ReverseEndianness(UInt128 value) =>
            new(ReverseEndianness(value.Lower), ReverseEndianness(value.Upper));

        public static void ReverseEndianness(ReadOnlySpan<short> source, Span<short> destination) =>
            ReverseEndianness<short, Int16EndiannessReverser>(source, destination);
        public static void ReverseEndianness(ReadOnlySpan<ushort> source, Span<ushort> destination) =>
            ReverseEndianness<ushort, UInt16EndiannessReverser>(source, destination);
        public static void ReverseEndianness(ReadOnlySpan<int> source, Span<int> destination) =>
            ReverseEndianness<int, Int32EndiannessReverser>(source, destination);
        public static void ReverseEndianness(ReadOnlySpan<uint> source, Span<uint> destination) =>
            ReverseEndianness<uint, UInt32EndiannessReverser>(source, destination);
        public static void ReverseEndianness(ReadOnlySpan<long> source, Span<long> destination) =>
            ReverseEndianness<long, Int64EndiannessReverser>(source, destination);
        public static void ReverseEndianness(ReadOnlySpan<ulong> source, Span<ulong> destination) =>
            ReverseEndianness<ulong, UInt64EndiannessReverser>(source, destination);
        public static void ReverseEndianness(ReadOnlySpan<nint> source, Span<nint> destination) =>
            ReverseEndianness<nint, IntPtrEndiannessReverser>(source, destination);
        public static void ReverseEndianness(ReadOnlySpan<nuint> source, Span<nuint> destination) =>
            ReverseEndianness<nuint, UIntPtrEndiannessReverser>(source, destination);
        public static void ReverseEndianness(ReadOnlySpan<Int128> source, Span<Int128> destination) =>
            ReverseEndianness<Int128, Int128EndiannessReverser>(source, destination);
        public static void ReverseEndianness(ReadOnlySpan<UInt128> source, Span<UInt128> destination) =>
            ReverseEndianness<UInt128, UInt128EndiannessReverser>(source, destination);

        // TODO(netwasm-simd)
        private static void ReverseEndianness<T, TReverser>(ReadOnlySpan<T> source, Span<T> destination)
            where T : struct
            where TReverser : IEndiannessReverser<T>
        {
            if (destination.Length < source.Length)
            {
                ThrowDestinationTooSmall();
            }

            if (!source.Overlaps(destination, out var elementOffset) || elementOffset <= 0)
            {
                for (var index = 0; index < source.Length; index++)
                {
                    destination[index] = TReverser.Reverse(source[index]);
                }
            }
            else
            {
                for (var index = source.Length - 1; index >= 0; index--)
                {
                    destination[index] = TReverser.Reverse(source[index]);
                }
            }
        }

        private interface IEndiannessReverser<T> where T : struct
        {
            static abstract T Reverse(T value);
        }

        private readonly struct Int16EndiannessReverser : IEndiannessReverser<short>
        {
            public static short Reverse(short value) => ReverseEndianness(value);
        }

        private readonly struct UInt16EndiannessReverser : IEndiannessReverser<ushort>
        {
            public static ushort Reverse(ushort value) => ReverseEndianness(value);
        }

        private readonly struct Int32EndiannessReverser : IEndiannessReverser<int>
        {
            public static int Reverse(int value) => ReverseEndianness(value);
        }

        private readonly struct UInt32EndiannessReverser : IEndiannessReverser<uint>
        {
            public static uint Reverse(uint value) => ReverseEndianness(value);
        }

        private readonly struct Int64EndiannessReverser : IEndiannessReverser<long>
        {
            public static long Reverse(long value) => ReverseEndianness(value);
        }

        private readonly struct UInt64EndiannessReverser : IEndiannessReverser<ulong>
        {
            public static ulong Reverse(ulong value) => ReverseEndianness(value);
        }

        private readonly struct IntPtrEndiannessReverser : IEndiannessReverser<nint>
        {
            public static nint Reverse(nint value) => ReverseEndianness(value);
        }

        private readonly struct UIntPtrEndiannessReverser : IEndiannessReverser<nuint>
        {
            public static nuint Reverse(nuint value) => ReverseEndianness(value);
        }

        private readonly struct Int128EndiannessReverser : IEndiannessReverser<Int128>
        {
            public static Int128 Reverse(Int128 value) => ReverseEndianness(value);
        }

        private readonly struct UInt128EndiannessReverser : IEndiannessReverser<UInt128>
        {
            public static UInt128 Reverse(UInt128 value) => ReverseEndianness(value);
        }

        private static void Require(ReadOnlySpan<byte> span, int size) { if (span.Length < size) throw new ArgumentOutOfRangeException(); }
        private static void Require(Span<byte> span, int size) { if (span.Length < size) throw new ArgumentOutOfRangeException(); }
        private static void ThrowDestinationTooSmall() => throw new ArgumentException("Destination is too short.", "destination");

    }
}
