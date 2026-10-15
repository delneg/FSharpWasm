// Portions derived from dotnet/runtime System.Private.CoreLib.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System
{
    // The compiler supplies the storage and indexing representation of managed
    // arrays. Rectangular-array construction and typed Get/Set/Address calls
    // are runtime-provided members lowered by the compiler; the managed
    // members below provide the shared System.Array surface.
    public abstract class Array : ICollection, IEnumerable, IList, IStructuralComparable,
        IStructuralEquatable, ICloneable
    {
        internal const int IntrosortSizeThreshold = 16;

        public int Length
        {
            get => this is object[] values ? values.Length : 0;
        }
        public long LongLength
        {
            get => Length;
        }
        public int Rank
        {
            get => 1;
        }
        public static int MaxLength
        {
            get => int.MaxValue;
        }
        public bool IsFixedSize
        {
            get => true;
        }
        public bool IsReadOnly
        {
            get => false;
        }
        public bool IsSynchronized
        {
            get => false;
        }
        public object SyncRoot
        {
            get => this;
        }

        private static class EmptyArray<T>
        {
            internal static readonly T[] Value = new T[0];
        }

        public static T[] Empty<T>() => EmptyArray<T>.Value;

        public static ReadOnlyCollection<T> AsReadOnly<T>(T[] array)
        {
            if (array is null)
            {
                throw new ArgumentNullException();
            }

            return new ReadOnlyCollection<T>(array);
        }

        public static void Clear<T>(T[] array, int index, int length)
        {
            ValidateRange(array, index, length);
            for (var current = index; current < index + length; current++)
            {
                array[current] = default!;
            }
        }

        public static void Clear(Array array)
        {
            ArgumentNullException.ThrowIfNull(array);
            InternalClear(array, 0, array.Length);
        }

        public static void Clear(Array array, int index, int length)
        {
            ArgumentNullException.ThrowIfNull(array);
            var offset = (long)index - array.GetLowerBound(0);
            if (offset < 0 || offset > array.Length || length < 0 || length > array.Length - offset)
                throw new IndexOutOfRangeException();
            InternalClear(array, (int)offset, length);
        }

        public static void Copy<T>(
            T[] sourceArray,
            int sourceIndex,
            T[] destinationArray,
            int destinationIndex,
            int length) => CopyPhysical(
                (Array)sourceArray,
                sourceIndex,
                destinationArray,
                destinationIndex,
                length);

        public static void Copy(Array sourceArray, Array destinationArray, int length)
        {
            // The first element is always at physical offset zero, including
            // ARRAY instances whose public indices start at a non-zero bound.
            CopyPhysical(sourceArray, 0, destinationArray, 0, length);
        }

        public static void Copy(Array sourceArray, Array destinationArray, long length)
        {
            Copy(sourceArray, destinationArray, ToInt32(length));
        }

        public static void Copy(
            Array sourceArray,
            int sourceIndex,
            Array destinationArray,
            int destinationIndex,
            int length)
        {
            ArgumentNullException.ThrowIfNull(sourceArray);
            ArgumentNullException.ThrowIfNull(destinationArray);
            if (sourceArray.Rank != destinationArray.Rank)
            {
                throw new RankException();
            }
            if (length < 0) throw new ArgumentOutOfRangeException();
            var sourceOffset = (long)sourceIndex - sourceArray.GetLowerBound(0);
            var destinationOffset = (long)destinationIndex - destinationArray.GetLowerBound(0);
            if (sourceOffset < 0 || destinationOffset < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (sourceOffset > int.MaxValue || destinationOffset > int.MaxValue)
            {
                throw new ArgumentException();
            }
            CopyPhysical(sourceArray, (int)sourceOffset, destinationArray, (int)destinationOffset, length);
        }

        private static void CopyPhysical(
            Array sourceArray,
            int sourceOffset,
            Array destinationArray,
            int destinationOffset,
            int length)
        {
            ArgumentNullException.ThrowIfNull(sourceArray);
            ArgumentNullException.ThrowIfNull(destinationArray);
            if (sourceArray.Rank != destinationArray.Rank)
            {
                throw new RankException();
            }
            ValidateRange(sourceArray.Length, sourceOffset, length);
            ValidateRange(destinationArray.Length, destinationOffset, length);
            var status = InternalCopy(
                sourceArray,
                sourceOffset,
                destinationArray,
                destinationOffset,
                length);
            if (status == ArrayCopyStatus.TypeMismatch)
            {
                throw new ArrayTypeMismatchException();
            }
            if (status == ArrayCopyStatus.ElementCastFailure)
            {
                throw new InvalidCastException();
            }
            if (status == ArrayCopyStatus.OutOfMemory)
            {
                throw new OutOfMemoryException();
            }
            if (status != ArrayCopyStatus.Success)
            {
                throw new InvalidOperationException();
            }
        }

        public static void Copy(
            Array sourceArray,
            long sourceIndex,
            Array destinationArray,
            long destinationIndex,
            long length)
        {
            Copy(
                sourceArray,
                ToInt32(sourceIndex),
                destinationArray,
                ToInt32(destinationIndex),
                ToInt32(length));
        }

        public static void Resize<T>([NotNull] ref T[]? array, int newSize)
        {
            if (newSize < 0)
            {
                throw new ArgumentOutOfRangeException();
            }

            var replacement = new T[newSize];
            if (array is not null)
            {
                var length = array.Length < newSize ? array.Length : newSize;
                Copy(array, 0, replacement, 0, length);
            }
            array = replacement;
        }

        public static int IndexOf<T>(T[] array, T value) =>
            IndexOf(array, value, 0, array?.Length ?? 0);

        public static int IndexOf<T>(T[] array, T value, int startIndex) =>
            IndexOf(array, value, startIndex, (array?.Length ?? 0) - startIndex);

        public static int IndexOf<T>(T[] array, T value, int startIndex, int count)
        {
            ValidateSearchRange(array, startIndex, count);
            var comparer = EqualityComparer<T>.Default;
            var end = startIndex + count;
            for (var index = startIndex; index < end; index++)
            {
                if (comparer.Equals(array[index], value))
                {
                    return index;
                }
            }
            return -1;
        }

        public static int IndexOf(Array array, object? value) =>
            IndexOf(array, value, 0, array?.Length ?? 0);

        public static int IndexOf(Array array, object? value, int startIndex) =>
            IndexOf(array, value, startIndex, (array?.Length ?? 0) - startIndex);

        public static int IndexOf(Array array, object? value, int startIndex, int count)
        {
            var lowerBound = ValidateVectorRange(array, startIndex, count);
            var physicalIndex = startIndex - lowerBound;
            for (var offset = 0; offset < count; offset++)
            {
                if (object.Equals(
                        InternalGetValue(array, physicalIndex + offset),
                        value))
                {
                    return startIndex + offset;
                }
            }

            return lowerBound - 1;
        }

        public static int LastIndexOf<T>(T[] array, T value) =>
            LastIndexOf(array, value, (array?.Length ?? 0) - 1, array?.Length ?? 0);

        public static int LastIndexOf<T>(T[] array, T value, int startIndex) =>
            LastIndexOf(array, value, startIndex, startIndex + 1);

        public static int LastIndexOf<T>(T[] array, T value, int startIndex, int count)
        {
            ValidateLastSearchRange(array, startIndex, count);
            var comparer = EqualityComparer<T>.Default;
            var end = startIndex - count;
            for (var index = startIndex; index > end; index--)
            {
                if (comparer.Equals(array[index], value))
                {
                    return index;
                }
            }
            return -1;
        }

        public static int LastIndexOf(Array array, object? value) =>
            LastIndexOf(array, value, (array?.Length ?? 0) - 1, array?.Length ?? 0);

        public static int LastIndexOf(Array array, object? value, int startIndex) =>
            LastIndexOf(array, value, startIndex, startIndex + 1);

        public static int LastIndexOf(Array array, object? value, int startIndex, int count)
        {
            ArgumentNullException.ThrowIfNull(array);
            if (array.Rank != 1)
            {
                throw new RankException();
            }
            var lowerBound = array.GetLowerBound(0);
            if (array.Length == 0)
            {
                return lowerBound - 1;
            }
            if (startIndex < lowerBound ||
                startIndex >= lowerBound + array.Length ||
                count < 0 || count > startIndex - lowerBound + 1)
            {
                throw new ArgumentOutOfRangeException();
            }

            var physicalIndex = startIndex - lowerBound;
            for (var offset = 0; offset < count; offset++)
            {
                if (object.Equals(
                        InternalGetValue(array, physicalIndex - offset),
                        value))
                {
                    return startIndex - offset;
                }
            }

            return lowerBound - 1;
        }

        public static int BinarySearch<T>(T[] array, T value) =>
            BinarySearch(array, 0, array?.Length ?? 0, value, null);

        public static int BinarySearch<T>(T[] array, T value, IComparer<T>? comparer) =>
            BinarySearch(array, 0, array?.Length ?? 0, value, comparer);

        public static int BinarySearch<T>(T[] array, int index, int length, T value) =>
            BinarySearch(array, index, length, value, null);

        public static int BinarySearch<T>(
            T[] array,
            int index,
            int length,
            T value,
            IComparer<T>? comparer)
        {
            ValidateRange(array, index, length);
            return ArraySortHelper<T>.Default.BinarySearch(
                array,
                index,
                length,
                value,
                comparer);
        }

        public static int BinarySearch(Array array, object? value) =>
            BinarySearch(array, 0, array?.Length ?? 0, value, null);

        public static int BinarySearch(Array array, object? value, IComparer? comparer) =>
            BinarySearch(array, 0, array?.Length ?? 0, value, comparer);

        public static int BinarySearch(Array array, int index, int length, object? value) =>
            BinarySearch(array, index, length, value, null);

        public static int BinarySearch(
            Array array,
            int index,
            int length,
            object? value,
            IComparer? comparer)
        {
            var lowerBound = ValidateVectorRange(
                array,
                index,
                length,
                validateRankFirst: false);
            comparer ??= DefaultArrayComparer.Instance;
            var low = index;
            var high = index + length - 1;
            while (low <= high)
            {
                var middle = low + ((high - low) >> 1);
                int comparison;
                try
                {
                    comparison = comparer.Compare(
                        InternalGetValue(array, middle - lowerBound),
                        value);
                }
                catch (Exception exception)
                {
                    throw new InvalidOperationException(null, exception);
                }
                if (comparison == 0) return middle;
                if (comparison < 0) low = middle + 1;
                else high = middle - 1;
            }

            return ~low;
        }

        public static void ConstrainedCopy(
            Array sourceArray,
            int sourceIndex,
            Array destinationArray,
            int destinationIndex,
            int length)
        {
            ArgumentNullException.ThrowIfNull(sourceArray);
            ArgumentNullException.ThrowIfNull(destinationArray);
            if (sourceArray.Rank != destinationArray.Rank)
            {
                throw new RankException();
            }
            if (length < 0) throw new ArgumentOutOfRangeException();
            var sourceOffset = (long)sourceIndex - sourceArray.GetLowerBound(0);
            var destinationOffset =
                (long)destinationIndex - destinationArray.GetLowerBound(0);
            if (sourceOffset < 0 || destinationOffset < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (sourceOffset > int.MaxValue || destinationOffset > int.MaxValue)
            {
                throw new ArgumentException();
            }
            ValidateRange(sourceArray.Length, (int)sourceOffset, length);
            ValidateRange(destinationArray.Length, (int)destinationOffset, length);
            if (!destinationArray.GetType().IsAssignableFrom(sourceArray.GetType()))
            {
                throw new ArrayTypeMismatchException();
            }

            CopyPhysical(
                sourceArray,
                (int)sourceOffset,
                destinationArray,
                (int)destinationOffset,
                length);
        }

        public static TOutput[] ConvertAll<TInput, TOutput>(
            TInput[] array,
            Converter<TInput, TOutput> converter)
        {
            if (array is null || converter is null)
            {
                throw new ArgumentNullException();
            }
            var result = new TOutput[array.Length];
            for (var index = 0; index < array.Length; index++)
            {
                result[index] = converter(array[index]);
            }
            return result;
        }

        public static bool Exists<T>(T[] array, Predicate<T> match) =>
            FindIndex(array, match) >= 0;

        public static void Fill<T>(T[] array, T value) =>
            Fill(array, value, 0, array?.Length ?? 0);

        public static void Fill<T>(T[] array, T value, int startIndex, int count)
        {
            ValidateRange(array, startIndex, count);
            for (var index = startIndex; index < startIndex + count; index++)
            {
                array[index] = value;
            }
        }

        public static T? Find<T>(T[] array, Predicate<T> match)
        {
            ValidatePredicate(array, match);
            for (var index = 0; index < array.Length; index++)
            {
                if (match(array[index])) return array[index];
            }
            return default;
        }

        public static T[] FindAll<T>(T[] array, Predicate<T> match)
        {
            ValidatePredicate(array, match);
            var result = new List<T>();
            for (var index = 0; index < array.Length; index++)
            {
                if (match(array[index])) result.Add(array[index]);
            }
            var values = new T[result.Count];
            result.CopyTo(values, 0);
            return values;
        }

        public static int FindIndex<T>(T[] array, Predicate<T> match) =>
            FindIndex(array, 0, array?.Length ?? 0, match);

        public static int FindIndex<T>(T[] array, int startIndex, Predicate<T> match) =>
            FindIndex(array, startIndex, (array?.Length ?? 0) - startIndex, match);

        public static int FindIndex<T>(T[] array, int startIndex, int count, Predicate<T> match)
        {
            ValidatePredicate(array, match);
            ValidateRange(array, startIndex, count);
            for (var index = startIndex; index < startIndex + count; index++)
            {
                if (match(array[index])) return index;
            }
            return -1;
        }

        public static T? FindLast<T>(T[] array, Predicate<T> match)
        {
            ValidatePredicate(array, match);
            for (var index = array.Length - 1; index >= 0; index--)
            {
                if (match(array[index])) return array[index];
            }
            return default;
        }

        public static int FindLastIndex<T>(T[] array, Predicate<T> match) =>
            FindLastIndex(array, (array?.Length ?? 0) - 1, array?.Length ?? 0, match);

        public static int FindLastIndex<T>(T[] array, int startIndex, Predicate<T> match) =>
            FindLastIndex(array, startIndex, startIndex + 1, match);

        public static int FindLastIndex<T>(T[] array, int startIndex, int count, Predicate<T> match)
        {
            ValidatePredicate(array, match);
            ValidateLastSearchRange(array, startIndex, count);
            for (var index = startIndex; index > startIndex - count; index--)
            {
                if (match(array[index])) return index;
            }
            return -1;
        }

        public static void ForEach<T>(T[] array, Action<T> action)
        {
            if (array is null || action is null)
            {
                throw new ArgumentNullException();
            }
            for (var index = 0; index < array.Length; index++) action(array[index]);
        }

        public static void Reverse<T>(T[] array) =>
            Reverse(array, 0, array?.Length ?? 0);

        public static void Reverse<T>(T[] array, int index, int length)
        {
            ValidateRange(array, index, length);
            var left = index;
            var right = index + length - 1;
            while (left < right)
            {
                (array[left], array[right]) = (array[right], array[left]);
                left++;
                right--;
            }
        }

        public static void Reverse(Array array)
        {
            Reverse(array, 0, array?.Length ?? 0);
        }

        public static void Reverse(Array array, int index, int length)
        {
            var lowerBound = ValidateVectorRange(
                array,
                index,
                length,
                validateRankFirst: false);
            if (length <= 1)
            {
                return;
            }

            var source = (Array)InternalClone(array);
            var physicalIndex = index - lowerBound;
            for (var offset = 0; offset < length; offset++)
            {
                CopyPhysical(
                    source,
                    physicalIndex + length - offset - 1,
                    array,
                    physicalIndex + offset,
                    1);
            }
        }

        public static void Sort<T>(T[] array) => Sort(array, 0, array?.Length ?? 0, null);
        public static void Sort<T>(T[] array, IComparer<T>? comparer) =>
            Sort(array, 0, array?.Length ?? 0, comparer);
        public static void Sort<T>(T[] array, int index, int length) =>
            Sort(array, index, length, null);
        public static void Sort<T>(T[] array, Comparison<T> comparison)
        {
            if (comparison is null) throw new ArgumentNullException();
            ArgumentNullException.ThrowIfNull(array);
            ArraySortHelper<T>.Sort(new Span<T>(array), comparison);
        }
        public static void Sort<T>(T[] array, int index, int length, IComparer<T>? comparer)
        {
            ValidateRange(array, index, length);
            ArraySortHelper<T>.Default.Sort(new Span<T>(array, index, length), comparer);
        }

        public static void Sort<TKey, TValue>(TKey[] keys, TValue[]? items) =>
            Sort(keys, items, 0, keys?.Length ?? 0, null);
        public static void Sort<TKey, TValue>(TKey[] keys, TValue[]? items, IComparer<TKey>? comparer) =>
            Sort(keys, items, 0, keys?.Length ?? 0, comparer);
        public static void Sort<TKey, TValue>(TKey[] keys, TValue[]? items, int index, int length) =>
            Sort(keys, items, index, length, null);
        public static void Sort<TKey, TValue>(
            TKey[] keys,
            TValue[]? items,
            int index,
            int length,
            IComparer<TKey>? comparer)
        {
            ValidateRange(keys, index, length);
            if (items is not null) ValidateRange(items, index, length);
            if (items is null)
            {
                ArraySortHelper<TKey>.Default.Sort(
                    new Span<TKey>(keys, index, length),
                    comparer);
                return;
            }
            ArraySortHelper<TKey, TValue>.Default.Sort(
                new Span<TKey>(keys, index, length),
                new Span<TValue>(items, index, length),
                comparer);
        }

        public static void Sort(Array array) => Sort(array, 0, array?.Length ?? 0, null);
        public static void Sort(Array array, IComparer? comparer) =>
            Sort(array, 0, array?.Length ?? 0, comparer);
        public static void Sort(Array array, int index, int length) =>
            Sort(array, index, length, null);
        public static void Sort(Array array, int index, int length, IComparer? comparer)
        {
            Sort(array, null, index, length, comparer);
        }

        public static void Sort(Array keys, Array? items)
        {
            Sort(keys, items, 0, keys?.Length ?? 0, null);
        }

        public static void Sort(Array keys, Array? items, IComparer? comparer)
        {
            Sort(keys, items, 0, keys?.Length ?? 0, comparer);
        }

        public static void Sort(Array keys, Array? items, int index, int length)
        {
            Sort(keys, items, index, length, null);
        }

        public static void Sort(
            Array keys,
            Array? items,
            int index,
            int length,
            IComparer? comparer)
        {
            ArgumentNullException.ThrowIfNull(keys);
            var lowerBound = ValidateVectorRange(keys, index, length);
            if (items is not null)
            {
                var itemLowerBound = ValidateVectorRange(items, index, length);
                if (itemLowerBound != lowerBound)
                {
                    throw new ArgumentException();
                }
            }
            if (length <= 1)
            {
                return;
            }

            comparer ??= DefaultArrayComparer.Instance;
            if (keys is object[] keyValues &&
                (items is null || items is object[]))
            {
                var physicalIndex = index - lowerBound;
                var itemValues = (object[]?)items;
                var adapter = new NonGenericComparerAdapter(comparer);
                if (itemValues is null)
                {
                    ArraySortHelper<object?>.Default.Sort(
                        new Span<object?>(keyValues, physicalIndex, length),
                        adapter);
                    return;
                }
                ArraySortHelper<object?, object?>.Default.Sort(
                    new Span<object?>(keyValues, physicalIndex, length),
                    new Span<object?>(itemValues, physicalIndex, length),
                    adapter);
                return;
            }

            var order = new int[length];
            for (var offset = 0; offset < length; offset++)
            {
                order[offset] = offset;
            }
            var sourceOffset = index - lowerBound;
            ArraySortHelper<int>.Default.Sort(
                new Span<int>(order),
                new ArrayIndexComparer(keys, sourceOffset, comparer));

            var keySource = (Array)InternalClone(keys);
            var itemSource = items is null ? null : (Array)InternalClone(items);
            for (var offset = 0; offset < length; offset++)
            {
                CopyPhysical(
                    keySource,
                    sourceOffset + order[offset],
                    keys,
                    sourceOffset + offset,
                    1);
                if (items is not null)
                {
                    CopyPhysical(
                        itemSource!,
                        sourceOffset + order[offset],
                        items,
                        sourceOffset + offset,
                        1);
                }
            }
        }

        public static bool TrueForAll<T>(T[] array, Predicate<T> match)
        {
            ValidatePredicate(array, match);
            for (var index = 0; index < array.Length; index++)
            {
                if (!match(array[index])) return false;
            }
            return true;
        }

        public int GetLength(int dimension)
        {
            if (dimension != 0) throw new IndexOutOfRangeException();
            return Length;
        }

        public long GetLongLength(int dimension) => GetLength(dimension);
        public int GetLowerBound(int dimension)
        {
            _ = GetLength(dimension);
            return 0;
        }
        public int GetUpperBound(int dimension)
        {
            return unchecked(GetLowerBound(dimension) + GetLength(dimension) - 1);
        }

        public object? GetValue(int index)
        {
            if (Rank != 1) throw new ArgumentException();
            var offset = (long)index - GetLowerBound(0);
            if (offset < 0 || offset >= Length) throw new IndexOutOfRangeException();
            return InternalGetValue(this, (int)offset);
        }

        public object? GetValue(params int[] indices)
        {
            if (indices is null) throw new ArgumentNullException();
            if (indices.Length != 1) throw new PlatformNotSupportedException();
            return GetValue(indices[0]);
        }

        public object? GetValue(long index) => GetValue(ToInt32(index));

        public object? GetValue(params long[] indices)
        {
            if (indices is null) throw new ArgumentNullException();
            if (indices.Length != 1) throw new PlatformNotSupportedException();
            return GetValue(indices[0]);
        }

        public void SetValue(object? value, int index)
        {
            if (this is object[] values)
            {
                values[index] = value!;
                return;
            }
            throw new PlatformNotSupportedException();
        }

        public void SetValue(object? value, params int[] indices)
        {
            if (indices is null) throw new ArgumentNullException();
            if (indices.Length != 1) throw new PlatformNotSupportedException();
            SetValue(value, indices[0]);
        }

        public void SetValue(object? value, long index) => SetValue(value, ToInt32(index));

        public void SetValue(object? value, params long[] indices)
        {
            if (indices is null) throw new ArgumentNullException();
            if (indices.Length != 1) throw new PlatformNotSupportedException();
            SetValue(value, indices[0]);
        }

        public void CopyTo(Array array, int index)
        {
            Copy(this, 0, array, index, Length);
        }

        public void CopyTo(Array array, long index) => CopyTo(array, ToInt32(index));

        public object Clone()
        {
            return InternalClone(this);
        }

        private static object InternalClone(Array array) =>
            throw new PlatformNotSupportedException();

        internal static object InternalCloneRange(Array array, int offset, int length) =>
            throw new PlatformNotSupportedException();

        public Collections.IEnumerator GetEnumerator()
        {
            if (this is object[] values) return new SZArrayEnumerator<object>(values);
            return new ArrayEnumerator(this);
        }

        private static object? InternalGetValue(Array array, int offset) =>
            throw new PlatformNotSupportedException();

        private sealed class ArrayEnumerator(Array array) : Collections.IEnumerator, ICloneable
        {
            private int _index = -1;

            public object Clone()
            {
                var clone = new ArrayEnumerator(array);
                clone._index = _index;
                return clone;
            }

            public object Current => _index >= 0 && _index < array.Length
                ? InternalGetValue(array, _index)!
                : throw new InvalidOperationException();

            public bool MoveNext()
            {
                var next = _index + 1;
                if (next < array.Length)
                {
                    _index = next;
                    return true;
                }
                _index = array.Length;
                return false;
            }

            public void Reset() => _index = -1;
        }

        public void Initialize()
        {
        }

        int ICollection.Count => Length;
        void ICollection.CopyTo(Array array, int index) => CopyTo(array, index);

        object? IList.this[int index]
        {
            get => GetValue(index);
            set => SetValue(value, index);
        }

        int IList.Add(object? value) => throw new NotSupportedException();
        bool IList.Contains(object? value) => IndexOf(this, value) >= 0;
        void IList.Clear() => Clear(this);
        int IList.IndexOf(object? value) => IndexOf(this, value);
        void IList.Insert(int index, object? value) => throw new NotSupportedException();
        void IList.Remove(object? value) => throw new NotSupportedException();
        void IList.RemoveAt(int index) => throw new NotSupportedException();

        int IStructuralComparable.CompareTo(object? other, IComparer comparer)
        {
            if (other is null) return 1;
            if (other is not Array values || values.Length != Length) throw new ArgumentException();
            for (var index = 0; index < Length; index++)
            {
                var result = comparer.Compare(GetValue(index), values.GetValue(index));
                if (result != 0) return result;
            }
            return 0;
        }

        bool IStructuralEquatable.Equals(object? other, IEqualityComparer comparer)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is not Array values || values.Length != Length) return false;
            for (var index = 0; index < Length; index++)
            {
                if (!comparer.Equals(GetValue(index), values.GetValue(index))) return false;
            }
            return true;
        }

        int IStructuralEquatable.GetHashCode(IEqualityComparer comparer)
        {
            var hash = 17;
            var start = Length < 8 ? 0 : Length - 8;
            for (var index = start; index < Length; index++)
            {
                hash = hash * 31 + comparer.GetHashCode(GetValue(index)!);
            }
            return hash;
        }

        private static void ValidateRange<T>(T[]? array, int index, int length)
        {
            if (array is null) throw new ArgumentNullException();
            if (index < 0 || length < 0 || index > array.Length - length)
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        private static void ValidateRange(int arrayLength, int index, int length)
        {
            if (index < 0 || length < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (index > arrayLength - length) throw new ArgumentException();
        }

        private static int ValidateVectorRange(
            Array? array,
            int index,
            int length,
            bool validateRankFirst = true)
        {
            ArgumentNullException.ThrowIfNull(array);
            if (validateRankFirst && array.Rank != 1)
            {
                throw new RankException();
            }
            var lowerBound = array.GetLowerBound(0);
            if (index < lowerBound || length < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (index - lowerBound > array.Length - length)
            {
                throw new ArgumentException();
            }
            if (!validateRankFirst && array.Rank != 1)
            {
                throw new RankException();
            }
            return lowerBound;
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern ArrayCopyStatus InternalCopy(
            Array sourceArray,
            int sourceIndex,
            Array destinationArray,
            int destinationIndex,
            int length);

        private enum ArrayCopyStatus
        {
            Success,
            TypeMismatch,
            ElementCastFailure,
            OutOfMemory,
        }

        [MethodImpl(MethodImplOptions.InternalCall)]
        private static extern void InternalClear(Array array, int index, int length);

        private static void ValidateSearchRange<T>(T[]? array, int startIndex, int count)
        {
            ValidateRange(array, startIndex, count);
        }

        private static void ValidateLastSearchRange<T>(T[]? array, int startIndex, int count)
        {
            if (array is null) throw new ArgumentNullException();
            if (array.Length == 0)
            {
                if (startIndex == -1 && count == 0) return;
                throw new ArgumentOutOfRangeException();
            }
            if (startIndex < 0 || startIndex >= array.Length || count < 0 || count > startIndex + 1)
            {
                throw new ArgumentOutOfRangeException();
            }
        }

        private static void ValidatePredicate<T>(T[]? array, Predicate<T>? match)
        {
            if (array is null || match is null) throw new ArgumentNullException();
        }

        private static int ToInt32(long value)
        {
            var result = (int)value;
            return value == result ? result : throw new ArgumentOutOfRangeException();
        }

        private sealed class ComparisonComparer<T>(Comparison<T> comparison) : Comparer<T>
        {
            public override int Compare(T? left, T? right) => comparison(left!, right!);
        }

        private sealed class DefaultArrayComparer : IComparer
        {
            internal static DefaultArrayComparer Instance { get; } = new();

            public int Compare(object? left, object? right)
            {
                if (ReferenceEquals(left, right)) return 0;
                if (left is null) return -1;
                if (right is null) return 1;
                if (left is IComparable comparable) return comparable.CompareTo(right);
                if (right is IComparable reverseComparable)
                {
                    return -reverseComparable.CompareTo(left);
                }
                throw new ArgumentException("At least one object must implement IComparable.");
            }
        }

        private sealed class NonGenericComparerAdapter(IComparer comparer) : IComparer<object?>
        {
            public int Compare(object? left, object? right) => comparer.Compare(left, right);
        }

        private sealed class ArrayIndexComparer(
            Array keys,
            int sourceOffset,
            IComparer comparer) : IComparer<int>
        {
            public int Compare(int left, int right) => comparer.Compare(
                InternalGetValue(keys, sourceOffset + left),
                InternalGetValue(keys, sourceOffset + right));
        }
    }
}
