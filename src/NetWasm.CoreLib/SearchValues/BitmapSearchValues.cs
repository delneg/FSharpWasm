// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System.Buffers
{
    internal struct BitVector256
    {
        private uint _word0;
        private uint _word1;
        private uint _word2;
        private uint _word3;
        private uint _word4;
        private uint _word5;
        private uint _word6;
        private uint _word7;

        internal void Set(byte value)
        {
            var bit = 1u << value;
            switch (value >> 5)
            {
                case 0: _word0 |= bit; break;
                case 1: _word1 |= bit; break;
                case 2: _word2 |= bit; break;
                case 3: _word3 |= bit; break;
                case 4: _word4 |= bit; break;
                case 5: _word5 |= bit; break;
                case 6: _word6 |= bit; break;
                default: _word7 |= bit; break;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal readonly bool Contains(byte value)
        {
            var bit = 1u << value;
            return (value >> 5) switch
            {
                0 => (_word0 & bit) != 0,
                1 => (_word1 & bit) != 0,
                2 => (_word2 & bit) != 0,
                3 => (_word3 & bit) != 0,
                4 => (_word4 & bit) != 0,
                5 => (_word5 & bit) != 0,
                6 => (_word6 & bit) != 0,
                _ => (_word7 & bit) != 0,
            };
        }

        internal readonly byte[] GetValues()
        {
            var count = 0;
            for (var value = 0; value <= byte.MaxValue; value++)
            {
                if (Contains((byte)value)) count++;
            }

            var values = new byte[count];
            var index = 0;
            for (var value = 0; value <= byte.MaxValue; value++)
            {
                if (Contains((byte)value)) values[index++] = (byte)value;
            }

            return values;
        }
    }

    internal sealed class AnyByteSearchValues : SearchValues<byte>
    {
        private readonly BitVector256 _bitmap;

        internal AnyByteSearchValues(ReadOnlySpan<byte> values)
        {
            var bitmap = new BitVector256();
            for (var index = 0; index < values.Length; index++)
            {
                bitmap.Set(values[index]);
            }
            _bitmap = bitmap;
        }

        internal override byte[] GetValues() => _bitmap.GetValues();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool ContainsCore(byte value) => _bitmap.Contains(value);

        internal override int IndexOfAny(ReadOnlySpan<byte> span) =>
            Search(span, except: false, reverse: false);

        internal override int IndexOfAnyExcept(ReadOnlySpan<byte> span) =>
            Search(span, except: true, reverse: false);

        internal override int LastIndexOfAny(ReadOnlySpan<byte> span) =>
            Search(span, except: false, reverse: true);

        internal override int LastIndexOfAnyExcept(ReadOnlySpan<byte> span) =>
            Search(span, except: true, reverse: true);

        // TODO(netwasm-simd)
        private int Search(ReadOnlySpan<byte> span, bool except, bool reverse)
        {
            var index = reverse ? span.Length - 1 : 0;
            var end = reverse ? -1 : span.Length;
            var step = reverse ? -1 : 1;
            for (; index != end; index += step)
            {
                if (_bitmap.Contains(span[index]) != except)
                {
                    return index;
                }
            }

            return -1;
        }
    }

    internal sealed class BitmapCharSearchValues : SearchValues<char>
    {
        private readonly uint[] _bitmap;

        internal BitmapCharSearchValues(ReadOnlySpan<char> values, int maximum)
        {
            _bitmap = new uint[maximum / 32 + 1];
            for (var index = 0; index < values.Length; index++)
            {
                var value = values[index];
                _bitmap[value >> 5] |= 1u << value;
            }
        }

        internal override char[] GetValues()
        {
            var count = 0;
            for (var value = 0; value < _bitmap.Length * 32; value++)
            {
                if (Contains(_bitmap, value)) count++;
            }

            var values = new char[count];
            var index = 0;
            for (var value = 0; value < _bitmap.Length * 32; value++)
            {
                if (Contains(_bitmap, value)) values[index++] = (char)value;
            }

            return values;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool ContainsCore(char value) => Contains(_bitmap, value);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool Contains(uint[] bitmap, int value)
        {
            var offset = (uint)(value >> 5);
            return offset < (uint)bitmap.Length && (bitmap[offset] & (1u << value)) != 0;
        }

        internal override int IndexOfAny(ReadOnlySpan<char> span) =>
            Search(span, except: false, reverse: false);

        internal override int IndexOfAnyExcept(ReadOnlySpan<char> span) =>
            Search(span, except: true, reverse: false);

        internal override int LastIndexOfAny(ReadOnlySpan<char> span) =>
            Search(span, except: false, reverse: true);

        internal override int LastIndexOfAnyExcept(ReadOnlySpan<char> span) =>
            Search(span, except: true, reverse: true);

        // TODO(netwasm-simd)
        private int Search(ReadOnlySpan<char> span, bool except, bool reverse)
        {
            var index = reverse ? span.Length - 1 : 0;
            var end = reverse ? -1 : span.Length;
            var step = reverse ? -1 : 1;
            for (; index != end; index += step)
            {
                if (Contains(_bitmap, span[index]) != except)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
