using System.Buffers;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System
{
    // Portions derived from dotnet/runtime System.Private.CoreLib (MIT).
    public readonly struct ReadOnlyMemory<T> : IEquatable<ReadOnlyMemory<T>>
    {
        internal const int RemoveFlagsBitMask = 0x7fffffff;
        internal const int PrePinnedFlag = unchecked((int)0x80000000);

        private readonly object? _object;
        private readonly int _index;
        private readonly int _length;

        public ReadOnlyMemory(T[]? array)
        {
            _object = array;
            _index = 0;
            _length = array?.Length ?? 0;
        }

        public ReadOnlyMemory(T[]? array, int start, int length)
        {
            if (array == null)
            {
                if (start != 0 || length != 0)
                {
                    throw new ArgumentOutOfRangeException();
                }
                _object = null;
                _index = 0;
                _length = 0;
                return;
            }
            if ((uint)start > (uint)array.Length ||
                (uint)length > (uint)(array.Length - start))
            {
                throw new ArgumentOutOfRangeException();
            }
            _object = array;
            _index = start;
            _length = length;
        }

        internal ReadOnlyMemory(MemoryManager<T> manager, int start, int length)
        {
            if (manager == null) throw new ArgumentNullException();
            if (start < 0 || length < 0) throw new ArgumentOutOfRangeException();
            _object = manager;
            _index = start;
            _length = length;
        }

        internal ReadOnlyMemory(string text, int start, int length)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (typeof(T) != typeof(char)) throw new InvalidOperationException();
            if ((uint)start > (uint)text.Length ||
                (uint)length > (uint)(text.Length - start))
            {
                throw new ArgumentOutOfRangeException(nameof(start));
            }
            _object = text;
            _index = start;
            _length = length;
        }

        internal ReadOnlyMemory(object? owner, int start, int length)
        {
            _object = owner;
            _index = start;
            _length = length;
        }

        public int Length
        {
            get => _length;
        }
        public bool IsEmpty
        {
            get => _length == 0;
        }
        public static ReadOnlyMemory<T> Empty
        {
            get => default;
        }
        public ReadOnlySpan<T> Span
        {
            get
            {
                if (_object is string text)
                {
                    ref var data = ref Unsafe.As<char, T>(ref text.GetRawStringData());
                    return new ReadOnlySpan<T>(
                        ref Unsafe.Add(ref data, _index),
                        _length);
                }
                return _object is MemoryManager<T> manager
                    ? manager.GetSpan().Slice(_index, _length)
                    : _object is not T[] array
                        ? new ReadOnlySpan<T>((T[]?)null)
                        : new ReadOnlySpan<T>(
                            ref Unsafe.Add(
                                ref MemoryMarshal.GetArrayDataReference(array),
                                _index & RemoveFlagsBitMask),
                            _length);
            }
        }

        public ReadOnlyMemory<T> Slice(int start) => Slice(start, _length - start);

        public ReadOnlyMemory<T> Slice(int start, int length)
        {
            if (start < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            if (start > _length - length)
            {
                throw new ArgumentOutOfRangeException();
            }
            return _object switch
            {
                MemoryManager<T> manager =>
                    new ReadOnlyMemory<T>(manager, _index + start, length),
                string text => new ReadOnlyMemory<T>(text, _index + start, length),
                T[] => new ReadOnlyMemory<T>(_object, _index + start, length),
                _ => default,
            };
        }

        public void CopyTo(Memory<T> destination) => Span.CopyTo(destination.Span);
        public bool TryCopyTo(Memory<T> destination) => Span.TryCopyTo(destination.Span);
        public T[] ToArray() => Span.ToArray();
        public unsafe MemoryHandle Pin()
        {
            var owner = _object;
            if (owner is null)
            {
                return default;
            }
            if (typeof(T) == typeof(char) && owner is string text)
            {
                var handle = GCHandle.Alloc(owner, GCHandleType.Pinned);
                ref var data = ref Unsafe.Add(ref text.GetRawStringData(), _index);
                return new MemoryHandle(Unsafe.AsPointer(ref data), handle);
            }
            if (owner is MemoryManager<T> manager)
            {
                return manager.Pin(_index);
            }

            var array = (T[])owner;
            var start = _index & RemoveFlagsBitMask;
            ref var arrayData = ref MemoryMarshal.GetArrayDataReference(array);
            if (_index < 0)
            {
                return new MemoryHandle(Unsafe.AsPointer(ref Unsafe.Add(ref arrayData, start)));
            }
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                throw new ArgumentException();
            }
            var arrayHandle = GCHandle.Alloc(owner, GCHandleType.Pinned);
            return new MemoryHandle(
                Unsafe.AsPointer(ref Unsafe.Add(ref arrayData, start)),
                arrayHandle);
        }

        public bool Equals(ReadOnlyMemory<T> other) =>
            ReferenceEquals(_object, other._object) &&
            _index == other._index && _length == other._length;

        public override bool Equals(object? value) =>
            value is ReadOnlyMemory<T> readOnly
                ? Equals(readOnly)
                : value is Memory<T> memory && Equals(memory);
        public override int GetHashCode() =>
            _object is null
                ? 0
                : HashCode.Combine(
                    RuntimeHelpers.GetHashCode(_object),
                    _index,
                    _length);
        public override string ToString() =>
            typeof(T) == typeof(char)
                ? Span.ToString()
                : $"System.ReadOnlyMemory<{typeof(T).Name}>[{_length}]";

        public static implicit operator ReadOnlyMemory<T>(T[]? array) => new(array);
        public static implicit operator ReadOnlyMemory<T>(ArraySegment<T> segment) =>
            new(segment.Array!, segment.Offset, segment.Count);
        internal Memory<T> DangerousAsMemory() =>
            _object switch
            {
                MemoryManager<T> manager => new Memory<T>(manager, _index, _length),
                T[] or string => new Memory<T>(_object, _index, _length),
                _ => default,
            };

        internal bool TryGetArray(out ArraySegment<T> segment)
        {
            if (_object is T[] array)
            {
                segment = new ArraySegment<T>(
                    array,
                    _index & RemoveFlagsBitMask,
                    _length);
                return true;
            }

            if (_object is MemoryManager<T> manager &&
                manager.TryGetArray(out var managerSegment))
            {
                segment = managerSegment.Slice(_index, _length);
                return true;
            }

            if (_length == 0)
            {
                segment = ArraySegment<T>.Empty;
                return true;
            }

            segment = default;
            return false;
        }

        internal bool TryGetString(out string? text, out int start, out int length)
        {
            text = _object as string;
            start = text is null ? 0 : _index;
            length = text is null ? 0 : _length;
            return text is not null;
        }

        internal bool TryGetMemoryManager<TManager>(
            out TManager? manager,
            out int start,
            out int length)
            where TManager : MemoryManager<T>
        {
            manager = _object as TManager;
            if (manager is null)
            {
                start = 0;
                length = 0;
                return false;
            }

            start = _index;
            length = _length;
            return true;
        }

    }
}
