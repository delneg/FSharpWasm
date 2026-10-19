using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System
{
    // Portions derived from dotnet/runtime System.Private.CoreLib (MIT).
    public ref struct Span<T>
    {
        private ref T _reference;
        private int _length;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span(T[]? array)
        {
            if (array == null)
            {
                _reference = ref Unsafe.NullRef<T>();
                _length = 0;
                return;
            }
            if (!typeof(T).IsValueType && array.GetType() != typeof(T[]))
            {
                throw new ArrayTypeMismatchException();
            }
            _reference = ref MemoryMarshal.GetArrayDataReference(array);
            _length = array.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span(T[]? array, int start, int length)
        {
            if (array == null)
            {
                if (start != 0 || length != 0)
                {
                    throw new ArgumentOutOfRangeException();
                }
                _reference = ref Unsafe.NullRef<T>();
                _length = 0;
                return;
            }
            if (!typeof(T).IsValueType && array.GetType() != typeof(T[]))
            {
                throw new ArrayTypeMismatchException();
            }
            if ((uint)start > (uint)array.Length ||
                (uint)length > (uint)(array.Length - start))
            {
                throw new ArgumentOutOfRangeException();
            }
            _reference = ref Unsafe.Add(
                ref MemoryMarshal.GetArrayDataReference(array), start);
            _length = length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public unsafe Span(void* pointer, int length)
        {
            if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                throw new ArgumentException();
            }
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            _reference = ref Unsafe.AsRef<T>(pointer);
            _length = length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal Span(ref T reference, int length)
        {
            _reference = ref reference;
            _length = length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span(ref T reference)
        {
            _reference = ref reference;
            _length = 1;
        }

        public int Length
        {
            get => _length;
        }

        public bool IsEmpty
        {
            get => _length == 0;
        }

        public static Span<T> Empty
        {
            get => default;
        }

        public static implicit operator Span<T>(ArraySegment<T> segment) =>
            new(segment.Array!, segment.Offset, segment.Count);

        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if ((uint)index >= (uint)_length)
                {
                    throw new IndexOutOfRangeException();
                }
                return ref Unsafe.Add(ref _reference, index);
            }
        }

        public ref T this[Index index] => ref this[index.GetOffset(_length)];

        public Span<T> this[Range range]
        {
            get
            {
                var offsets = range.GetOffsetAndLength(_length);
                return Slice(offsets.Item1, offsets.Item2);
            }
        }

        public static bool operator ==(Span<T> left, Span<T> right) =>
            left._length == right._length &&
            Unsafe.AreSame(ref left._reference, ref right._reference);

        public static bool operator !=(Span<T> left, Span<T> right) =>
            !(left == right);

        public override bool Equals(object? value) =>
            throw new NotSupportedException();

        public override int GetHashCode() => throw new NotSupportedException();

        public override string ToString()
        {
            if (typeof(T) == typeof(char))
            {
                var characters = new ReadOnlySpan<char>(
                    ref Unsafe.As<T, char>(ref _reference), _length);
                return string.Create(characters.ToArray());
            }

            return $"System.Span<{typeof(T).Name}>[{_length}]";
        }

        public Enumerator GetEnumerator() => new(this);

        public ref struct Enumerator : System.Collections.Generic.IEnumerator<T>,
            System.Collections.IEnumerator, IDisposable
        {
            private readonly Span<T> _span;
            private int _index;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Enumerator(Span<T> span)
            {
                _span = span;
                _index = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                var next = _index + 1;
                if (next < _span.Length)
                {
                    _index = next;
                    return true;
                }

                return false;
            }

            public ref T Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ref _span[_index];
            }
            T System.Collections.Generic.IEnumerator<T>.Current => Current;
            object System.Collections.IEnumerator.Current => Current!;
            void System.Collections.IEnumerator.Reset() => _index = -1;
            void IDisposable.Dispose() { }
        }

        public ref T GetPinnableReference()
        {
            ref T result = ref Unsafe.NullRef<T>();
            if (_length != 0)
            {
                result = ref _reference;
            }
            return ref result;
        }

        internal ref T DangerousReference => ref _reference;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> Slice(int start) => Slice(start, _length - start);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Span<T> Slice(int start, int length)
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
            return new Span<T>(ref Unsafe.Add(ref _reference, start), length);
        }

        public void Clear()
        {
            for (var index = 0; index < _length; index++)
            {
                Unsafe.Add(ref _reference, index) = default(T)!;
            }
        }

        public void Fill(T value)
        {
            for (var index = 0; index < _length; index++)
            {
                Unsafe.Add(ref _reference, index) = value;
            }
        }

        public void CopyTo(Span<T> destination)
        {
            if ((uint)_length <= (uint)destination._length)
            {
                Buffer.Memmove(
                    ref destination._reference,
                    ref _reference,
                    _length);
                return;
            }

            throw new ArgumentException();
        }

        public bool TryCopyTo(Span<T> destination)
        {
            if ((uint)_length <= (uint)destination._length)
            {
                Buffer.Memmove(
                    ref destination._reference,
                    ref _reference,
                    _length);
                return true;
            }

            return false;
        }

        public T[] ToArray()
        {
            var result = new T[_length];
            CopyTo(new Span<T>(result));
            return result;
        }

        public static implicit operator Span<T>(T[]? array) => new(array);

        public static implicit operator ReadOnlySpan<T>(Span<T> span) =>
            new(ref span._reference, span._length);

    }
}
