// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System.Buffers
{
    internal sealed class RangeByteSearchValues : SearchValues<byte>
    {
        private readonly byte _minimum;
        private readonly byte _maximum;
        private readonly uint _minimumUnsigned;
        private readonly uint _range;

        internal RangeByteSearchValues(byte minimum, byte maximum)
        {
            _minimum = minimum;
            _maximum = maximum;
            _minimumUnsigned = minimum;
            _range = (uint)(maximum - minimum);
        }

        internal override byte[] GetValues()
        {
            var values = new byte[_range + 1];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = (byte)(_minimum + index);
            }

            return values;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool ContainsCore(byte value) =>
            value - _minimumUnsigned <= _range;

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
                if (ContainsCore(span[index]) != except)
                {
                    return index;
                }
            }

            return -1;
        }
    }

    internal sealed class RangeCharSearchValues : SearchValues<char>
    {
        private readonly char _minimum;
        private readonly char _maximum;
        private readonly uint _minimumUnsigned;
        private readonly uint _range;

        internal RangeCharSearchValues(char minimum, char maximum)
        {
            _minimum = minimum;
            _maximum = maximum;
            _minimumUnsigned = minimum;
            _range = (uint)(maximum - minimum);
        }

        internal override char[] GetValues()
        {
            var values = new char[_range + 1];
            for (var index = 0; index < values.Length; index++)
            {
                values[index] = (char)(_minimum + index);
            }

            return values;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool ContainsCore(char value) =>
            value - _minimumUnsigned <= _range;

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
                if (ContainsCore(span[index]) != except)
                {
                    return index;
                }
            }

            return -1;
        }
    }
}
