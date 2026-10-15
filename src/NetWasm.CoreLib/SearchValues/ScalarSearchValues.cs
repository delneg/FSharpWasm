// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;

namespace System.Buffers
{
    internal static class ScalarSearchValues
    {
        // TODO(netwasm-simd)
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static int IndexOfAny<T>(
            ReadOnlySpan<T> span,
            T value0,
            T value1,
            T value2,
            T value3,
            T value4,
            int count,
            bool except,
            bool reverse)
            where T : struct, IEquatable<T>
        {
            var index = reverse ? span.Length - 1 : 0;
            var end = reverse ? -1 : span.Length;
            var step = reverse ? -1 : 1;
            for (; index != end; index += step)
            {
                var current = span[index];
                var matches = current.Equals(value0) ||
                    count > 1 && current.Equals(value1) ||
                    count > 2 && current.Equals(value2) ||
                    count > 3 && current.Equals(value3) ||
                    count > 4 && current.Equals(value4);
                if (matches != except)
                {
                    return index;
                }
            }

            return -1;
        }
    }

    internal sealed class Any1SearchValues<T> : SearchValues<T>
        where T : struct, IEquatable<T>
    {
        private readonly T _value0;

        internal Any1SearchValues(ReadOnlySpan<T> values) => _value0 = values[0];

        internal override T[] GetValues() => new[] { _value0 };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool ContainsCore(T value) => value.Equals(_value0);

        internal override int IndexOfAny(ReadOnlySpan<T> span) =>
            Search(span, except: false, reverse: false);

        internal override int IndexOfAnyExcept(ReadOnlySpan<T> span) =>
            Search(span, except: true, reverse: false);

        internal override int LastIndexOfAny(ReadOnlySpan<T> span) =>
            Search(span, except: false, reverse: true);

        internal override int LastIndexOfAnyExcept(ReadOnlySpan<T> span) =>
            Search(span, except: true, reverse: true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Search(ReadOnlySpan<T> span, bool except, bool reverse) =>
            ScalarSearchValues.IndexOfAny(
                span,
                _value0,
                default,
                default,
                default,
                default,
                1,
                except,
                reverse);
    }

    internal sealed class Any2SearchValues<T> : SearchValues<T>
        where T : struct, IEquatable<T>
    {
        private readonly T _value0;
        private readonly T _value1;

        internal Any2SearchValues(ReadOnlySpan<T> values)
        {
            _value0 = values[0];
            _value1 = values[1];
        }

        internal override T[] GetValues() => new[] { _value0, _value1 };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool ContainsCore(T value) =>
            value.Equals(_value0) || value.Equals(_value1);

        internal override int IndexOfAny(ReadOnlySpan<T> span) =>
            Search(span, except: false, reverse: false);

        internal override int IndexOfAnyExcept(ReadOnlySpan<T> span) =>
            Search(span, except: true, reverse: false);

        internal override int LastIndexOfAny(ReadOnlySpan<T> span) =>
            Search(span, except: false, reverse: true);

        internal override int LastIndexOfAnyExcept(ReadOnlySpan<T> span) =>
            Search(span, except: true, reverse: true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Search(ReadOnlySpan<T> span, bool except, bool reverse) =>
            ScalarSearchValues.IndexOfAny(
                span,
                _value0,
                _value1,
                default,
                default,
                default,
                2,
                except,
                reverse);
    }

    internal sealed class Any3SearchValues<T> : SearchValues<T>
        where T : struct, IEquatable<T>
    {
        private readonly T _value0;
        private readonly T _value1;
        private readonly T _value2;

        internal Any3SearchValues(ReadOnlySpan<T> values)
        {
            _value0 = values[0];
            _value1 = values[1];
            _value2 = values[2];
        }

        internal override T[] GetValues() => new[] { _value0, _value1, _value2 };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool ContainsCore(T value) =>
            value.Equals(_value0) || value.Equals(_value1) || value.Equals(_value2);

        internal override int IndexOfAny(ReadOnlySpan<T> span) =>
            Search(span, except: false, reverse: false);

        internal override int IndexOfAnyExcept(ReadOnlySpan<T> span) =>
            Search(span, except: true, reverse: false);

        internal override int LastIndexOfAny(ReadOnlySpan<T> span) =>
            Search(span, except: false, reverse: true);

        internal override int LastIndexOfAnyExcept(ReadOnlySpan<T> span) =>
            Search(span, except: true, reverse: true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Search(ReadOnlySpan<T> span, bool except, bool reverse) =>
            ScalarSearchValues.IndexOfAny(
                span,
                _value0,
                _value1,
                _value2,
                default,
                default,
                3,
                except,
                reverse);
    }

    internal sealed class Any4SearchValues<T> : SearchValues<T>
        where T : struct, IEquatable<T>
    {
        private readonly T _value0;
        private readonly T _value1;
        private readonly T _value2;
        private readonly T _value3;

        internal Any4SearchValues(ReadOnlySpan<T> values)
        {
            _value0 = values[0];
            _value1 = values[1];
            _value2 = values[2];
            _value3 = values[3];
        }

        internal override T[] GetValues() => new[] { _value0, _value1, _value2, _value3 };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool ContainsCore(T value) =>
            value.Equals(_value0) || value.Equals(_value1) ||
            value.Equals(_value2) || value.Equals(_value3);

        internal override int IndexOfAny(ReadOnlySpan<T> span) =>
            Search(span, except: false, reverse: false);

        internal override int IndexOfAnyExcept(ReadOnlySpan<T> span) =>
            Search(span, except: true, reverse: false);

        internal override int LastIndexOfAny(ReadOnlySpan<T> span) =>
            Search(span, except: false, reverse: true);

        internal override int LastIndexOfAnyExcept(ReadOnlySpan<T> span) =>
            Search(span, except: true, reverse: true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Search(ReadOnlySpan<T> span, bool except, bool reverse) =>
            ScalarSearchValues.IndexOfAny(
                span,
                _value0,
                _value1,
                _value2,
                _value3,
                default,
                4,
                except,
                reverse);
    }

    internal sealed class Any5SearchValues<T> : SearchValues<T>
        where T : struct, IEquatable<T>
    {
        private readonly T _value0;
        private readonly T _value1;
        private readonly T _value2;
        private readonly T _value3;
        private readonly T _value4;

        internal Any5SearchValues(ReadOnlySpan<T> values)
        {
            _value0 = values[0];
            _value1 = values[1];
            _value2 = values[2];
            _value3 = values[3];
            _value4 = values[4];
        }

        internal override T[] GetValues() =>
            new[] { _value0, _value1, _value2, _value3, _value4 };

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override bool ContainsCore(T value) =>
            value.Equals(_value0) || value.Equals(_value1) ||
            value.Equals(_value2) || value.Equals(_value3) || value.Equals(_value4);

        internal override int IndexOfAny(ReadOnlySpan<T> span) =>
            Search(span, except: false, reverse: false);

        internal override int IndexOfAnyExcept(ReadOnlySpan<T> span) =>
            Search(span, except: true, reverse: false);

        internal override int LastIndexOfAny(ReadOnlySpan<T> span) =>
            Search(span, except: false, reverse: true);

        internal override int LastIndexOfAnyExcept(ReadOnlySpan<T> span) =>
            Search(span, except: true, reverse: true);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int Search(ReadOnlySpan<T> span, bool except, bool reverse) =>
            ScalarSearchValues.IndexOfAny(
                span,
                _value0,
                _value1,
                _value2,
                _value3,
                _value4,
                5,
                except,
                reverse);
    }
}
