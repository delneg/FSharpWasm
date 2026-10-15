// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Portable scalar implementation adapted from System.Private.CoreLib at
// runtime commit 811225a482702af7ecc35d817966bc70b88a3a23.  NetWasm keeps
// the managed algorithms and intentionally does not select hardware SIMD.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System.Numerics
{
    /// <summary>Provides scalar bit-manipulation operations.</summary>
    public static class BitOperations
    {
        // TODO(netwasm-simd)
        private static ReadOnlySpan<byte> TrailingZeroCountDeBruijn =>
        [
            00, 01, 28, 02, 29, 14, 24, 03,
            30, 22, 20, 15, 25, 17, 04, 08,
            31, 27, 13, 23, 21, 19, 16, 07,
            26, 12, 18, 06, 11, 05, 10, 09,
        ];

        private static ReadOnlySpan<byte> Log2DeBruijn =>
        [
            00, 09, 01, 10, 13, 21, 02, 29,
            11, 14, 16, 18, 22, 25, 03, 30,
            08, 12, 20, 28, 15, 17, 24, 07,
            19, 27, 23, 06, 26, 05, 04, 31,
        ];

        public static bool IsPow2(int value) => value > 0 && (value & (value - 1)) == 0;

        public static bool IsPow2(uint value) => value != 0 && (value & (value - 1)) == 0;

        public static bool IsPow2(long value) => value > 0 && (value & (value - 1)) == 0;

        public static bool IsPow2(ulong value) => value != 0 && (value & (value - 1)) == 0;

        public static bool IsPow2(nint value) => value > 0 && (value & (value - 1)) == 0;

        public static bool IsPow2(nuint value) => value != 0 && (value & (value - 1)) == 0;

        public static uint RoundUpToPowerOf2(uint value)
        {
            if (value == 0) return 0;
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return value + 1;
        }

        public static ulong RoundUpToPowerOf2(ulong value)
        {
            if (value == 0) return 0;
            value--;
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            value |= value >> 32;
            return value + 1;
        }

        public static nuint RoundUpToPowerOf2(nuint value) => (nuint)RoundUpToPowerOf2((ulong)value);

        public static int LeadingZeroCount(uint value)
        {
            if (value == 0) return 32;
            return 31 ^ Log2SoftwareFallback(value);
        }

        public static int LeadingZeroCount(ulong value)
        {
            var high = (uint)(value >> 32);
            return high == 0
                ? 32 + LeadingZeroCount((uint)value)
                : LeadingZeroCount(high);
        }

        public static int LeadingZeroCount(nuint value) => UIntPtr.Size == 4
            ? LeadingZeroCount((uint)value)
            : LeadingZeroCount((ulong)value);

        public static int Log2(uint value)
        {
            value |= 1;
            return Log2SoftwareFallback(value);
        }

        public static int Log2(ulong value)
        {
            var high = (uint)(value >> 32);
            return high == 0 ? Log2((uint)value) : 32 + Log2(high);
        }

        public static int Log2(nuint value) => UIntPtr.Size == 4
            ? Log2((uint)value)
            : Log2((ulong)value);

        internal static int Log2Ceiling(uint value)
        {
            var result = Log2(value);
            if (PopCount(value) != 1)
            {
                result++;
            }
            return result;
        }

        internal static int Log2Ceiling(ulong value)
        {
            var result = Log2(value);
            if (PopCount(value) != 1)
            {
                result++;
            }
            return result;
        }

        public static int PopCount(uint value)
        {
            const uint c1 = 0x55555555u;
            const uint c2 = 0x33333333u;
            const uint c3 = 0x0F0F0F0Fu;
            const uint c4 = 0x01010101u;

            value -= (value >> 1) & c1;
            value = (value & c2) + ((value >> 2) & c2);
            value = (((value + (value >> 4)) & c3) * c4) >> 24;
            return (int)value;
        }

        public static int PopCount(ulong value) =>
            PopCount((uint)value) + PopCount((uint)(value >> 32));

        public static int PopCount(nuint value) => UIntPtr.Size == 4
            ? PopCount((uint)value)
            : PopCount((ulong)value);

        public static int TrailingZeroCount(int value) => TrailingZeroCount((uint)value);

        public static int TrailingZeroCount(uint value)
        {
            if (value == 0) return 32;
            var index = (int)(((value & (uint)-(int)value) * 0x077CB531u) >> 27);
            return TrailingZeroCountDeBruijn[index];
        }

        public static int TrailingZeroCount(long value) => TrailingZeroCount((ulong)value);

        public static int TrailingZeroCount(ulong value)
        {
            var low = (uint)value;
            return low == 0
                ? 32 + TrailingZeroCount((uint)(value >> 32))
                : TrailingZeroCount(low);
        }

        public static int TrailingZeroCount(nint value) => TrailingZeroCount((nuint)value);

        public static int TrailingZeroCount(nuint value) => UIntPtr.Size == 4
            ? TrailingZeroCount((uint)value)
            : TrailingZeroCount((ulong)value);

        public static uint RotateLeft(uint value, int offset) => (value << offset) | (value >> (32 - offset));

        public static ulong RotateLeft(ulong value, int offset) => (value << offset) | (value >> (64 - offset));

        public static nuint RotateLeft(nuint value, int offset) => UIntPtr.Size == 4
            ? RotateLeft((uint)value, offset)
            : (nuint)RotateLeft((ulong)value, offset);

        public static uint RotateRight(uint value, int offset) => (value >> offset) | (value << (32 - offset));

        public static ulong RotateRight(ulong value, int offset) => (value >> offset) | (value << (64 - offset));

        public static nuint RotateRight(nuint value, int offset) => UIntPtr.Size == 4
            ? RotateRight((uint)value, offset)
            : (nuint)RotateRight((ulong)value, offset);

        public static uint Crc32C(uint crc, byte data) => Crc32Fallback.Crc32C(crc, data);

        public static uint Crc32C(uint crc, ushort data) => Crc32Fallback.Crc32C(crc, data);

        public static uint Crc32C(uint crc, uint data) => Crc32Fallback.Crc32C(crc, data);

        public static uint Crc32C(uint crc, ulong data) =>
            Crc32C(Crc32C(crc, (uint)data), (uint)(data >> 32));

        internal static uint ResetLowestSetBit(uint value) => value & (value - 1);

        internal static ulong ResetLowestSetBit(ulong value) => value & (value - 1);

        internal static uint FlipBit(uint value, int index) => value ^ (1u << index);

        internal static ulong FlipBit(ulong value, int index) => value ^ (1ul << index);

        private static int Log2SoftwareFallback(uint value)
        {
            value |= value >> 1;
            value |= value >> 2;
            value |= value >> 4;
            value |= value >> 8;
            value |= value >> 16;
            return Log2DeBruijn[(int)((value * 0x07C4ACDDu) >> 27)];
        }

        private static class Crc32Fallback
        {
            private static readonly uint[] s_crcTable = GenerateTable();

            internal static uint Crc32C(uint crc, byte data) =>
                s_crcTable[(byte)(crc ^ data)] ^ (crc >> 8);

            internal static uint Crc32C(uint crc, ushort data)
            {
                crc = s_crcTable[(byte)(crc ^ (byte)data)] ^ (crc >> 8);
                data >>= 8;
                return s_crcTable[(byte)(crc ^ data)] ^ (crc >> 8);
            }

            internal static uint Crc32C(uint crc, uint data)
            {
                ref var lookupTable = ref MemoryMarshal.GetArrayDataReference(s_crcTable);
                return Crc32CCore(ref lookupTable, crc, data);
            }

            private static uint Crc32CCore(ref uint lookupTable, uint crc, uint data)
            {
                crc = Unsafe.Add(ref lookupTable, (nint)(byte)(crc ^ (byte)data)) ^ (crc >> 8);
                data >>= 8;
                crc = Unsafe.Add(ref lookupTable, (nint)(byte)(crc ^ (byte)data)) ^ (crc >> 8);
                data >>= 8;
                crc = Unsafe.Add(ref lookupTable, (nint)(byte)(crc ^ (byte)data)) ^ (crc >> 8);
                data >>= 8;
                return Unsafe.Add(ref lookupTable, (nint)(byte)(crc ^ data)) ^ (crc >> 8);
            }

            private static uint[] GenerateTable()
            {
                var table = new uint[256];
                for (var index = 0; index < table.Length; index++)
                {
                    var value = (uint)index;
                    for (var bit = 0; bit < 8; bit++)
                    {
                        value = (value & 1) == 0
                            ? value >> 1
                            : (value >> 1) ^ 0x82F63B78u;
                    }
                    table[index] = value;
                }
                return table;
            }
        }
    }
}
