// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace System.Runtime.CompilerServices;

public sealed class ConditionalWeakTable<TKey, TValue> :
    IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : class
    where TValue : class?
{
    private const int InitialCapacity = 8;

    private Container _container;
    private int _activeEnumeratorRefCount;

    public ConditionalWeakTable() => _container = new Container(this);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public delegate TValue CreateValueCallback(TKey key);

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _container.TryGetValue(key, out value);
    }

    public void Add(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_container.FindEntry(key, out _) != -1)
        {
            throw new ArgumentException();
        }

        CreateEntry(key, value);
    }

    public bool TryAdd(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (_container.FindEntry(key, out _) != -1)
        {
            return false;
        }

        CreateEntry(key, value);
        return true;
    }

    public void AddOrUpdate(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        int entryIndex = _container.FindEntry(key, out _);
        if (entryIndex != -1)
        {
            _container.UpdateValue(entryIndex, value);
        }
        else
        {
            CreateEntry(key, value);
        }
    }

    public bool Remove(TKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _container.Remove(key, out _);
    }

    public bool Remove(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _container.Remove(key, out value);
    }

    public void Clear()
    {
        if (_activeEnumeratorRefCount > 0)
        {
            _container.RemoveAllKeys();
        }
        else
        {
            _container = new Container(this);
        }
    }

    public TValue GetOrAdd(TKey key, TValue value)
    {
        if (TryGetValue(key, out TValue? existingValue))
        {
            return existingValue;
        }

        return GetOrAddAfterFactory(key, value);
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        if (TryGetValue(key, out TValue? existingValue))
        {
            return existingValue;
        }

        return GetOrAddAfterFactory(key, valueFactory(key));
    }

    public TValue GetOrAdd<TArg>(
        TKey key,
        Func<TKey, TArg, TValue> valueFactory,
        TArg factoryArgument)
        where TArg : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(valueFactory);
        if (TryGetValue(key, out TValue? existingValue))
        {
            return existingValue;
        }

        return GetOrAddAfterFactory(key, valueFactory(key, factoryArgument));
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public TValue GetValue(TKey key, CreateValueCallback createValueCallback)
    {
        ArgumentNullException.ThrowIfNull(createValueCallback);
        if (TryGetValue(key, out TValue? existingValue))
        {
            return existingValue;
        }

        return GetOrAddAfterFactory(key, createValueCallback(key));
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    public TValue GetOrCreateValue(TKey key) =>
        GetValue(key, static _ => Activator.CreateInstance<TValue>());

    IEnumerator<KeyValuePair<TKey, TValue>>
        IEnumerable<KeyValuePair<TKey, TValue>>.GetEnumerator() =>
        _container.FirstFreeEntry == 0
            ? GenericEmptyEnumerator<KeyValuePair<TKey, TValue>>.Instance
            : new Enumerator(this);

    IEnumerator IEnumerable.GetEnumerator() =>
        ((IEnumerable<KeyValuePair<TKey, TValue>>)this).GetEnumerator();

    private TValue GetOrAddAfterFactory(TKey key, TValue value)
    {
        if (_container.TryGetValue(key, out TValue? existingValue))
        {
            return existingValue;
        }

        CreateEntry(key, value);
        return value;
    }

    private void CreateEntry(TKey key, TValue value)
    {
        Container container = _container;
        if (!container.HasCapacity)
        {
            _container = container = container.Resize();
        }

        container.CreateEntryNoResize(key, value);
    }

    private struct Entry
    {
        internal DependentHandle Handle;
        internal int HashCode;
        internal int Next;
    }

    private sealed class Container
    {
        private readonly ConditionalWeakTable<TKey, TValue> _parent;
        private int[] _buckets;
        private Entry[] _entries;
        private int _firstFreeEntry;
        private bool _invalid;

        internal Container(ConditionalWeakTable<TKey, TValue> parent)
        {
            int[] buckets = new int[InitialCapacity];
            InitializeBuckets(buckets);
            Entry[] entries = new Entry[InitialCapacity];
            _buckets = buckets;
            _entries = entries;
            _parent = parent;
        }

        private Container(
            ConditionalWeakTable<TKey, TValue> parent,
            int[] buckets,
            Entry[] entries,
            int firstFreeEntry)
        {
            _parent = parent;
            _buckets = buckets;
            _entries = entries;
            _firstFreeEntry = firstFreeEntry;
        }

        internal bool HasCapacity => _firstFreeEntry < _entries.Length;

        internal int FirstFreeEntry => _firstFreeEntry;

        internal void CreateEntryNoResize(TKey key, TValue value)
        {
            VerifyIntegrity();
            _invalid = true;

            int hashCode = RuntimeHelpers.GetHashCode(key) & int.MaxValue;
            int entryIndex = _firstFreeEntry++;
            ref Entry entry = ref _entries[entryIndex];
            entry.HashCode = hashCode;
            entry.Handle = new DependentHandle(key, value);
            int bucket = hashCode & (_buckets.Length - 1);
            entry.Next = _buckets[bucket];
            _buckets[bucket] = entryIndex;

            _invalid = false;
        }

        internal bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            int entryIndex = FindEntry(key, out object? dependent);
            if (entryIndex != -1)
            {
                value = (TValue)dependent!;
                return true;
            }

            value = default!;
            return false;
        }

        internal int FindEntry(TKey key, out object? dependent)
        {
            int hashCode = RuntimeHelpers.GetHashCode(key) & int.MaxValue;
            int bucket = hashCode & (_buckets.Length - 1);
            for (int index = _buckets[bucket]; index != -1; index = _entries[index].Next)
            {
                ref Entry entry = ref _entries[index];
                if (entry.HashCode == hashCode &&
                    ReferenceEquals(entry.Handle.UnsafeGetTargetAndDependent(out dependent), key))
                {
                    GC.KeepAlive(this);
                    return index;
                }
            }

            GC.KeepAlive(this);
            dependent = null;
            return -1;
        }

        internal bool TryGetEntry(int index, out TKey key, out TValue value)
        {
            if ((uint)index < (uint)_firstFreeEntry)
            {
                object? target = _entries[index].Handle.UnsafeGetTargetAndDependent(
                    out object? dependent);
                GC.KeepAlive(this);
                if (target is not null)
                {
                    key = (TKey)target;
                    value = (TValue)dependent!;
                    return true;
                }
            }

            key = default!;
            value = default!;
            return false;
        }

        internal bool Remove(TKey key, out TValue value)
        {
            VerifyIntegrity();
            int entryIndex = FindEntry(key, out object? dependent);
            if (entryIndex == -1)
            {
                value = default!;
                return false;
            }

            RemoveIndex(entryIndex);
            value = (TValue)dependent!;
            return true;
        }

        internal void RemoveAllKeys()
        {
            VerifyIntegrity();
            for (int index = 0; index < _firstFreeEntry; index++)
            {
                RemoveIndex(index);
            }
        }

        internal void UpdateValue(int entryIndex, TValue value)
        {
            VerifyIntegrity();
            _invalid = true;
            _entries[entryIndex].Handle.UnsafeSetDependent(value);
            _invalid = false;
        }

        internal Container Resize()
        {
            bool preserveIndices = _parent._activeEnumeratorRefCount > 0;
            bool hasExpiredEntries = false;
            if (!preserveIndices)
            {
                for (int index = 0; index < _firstFreeEntry; index++)
                {
                    ref Entry entry = ref _entries[index];
                    if (entry.HashCode == -1 ||
                        entry.Handle.IsAllocated && entry.Handle.UnsafeGetTarget() is null)
                    {
                        hasExpiredEntries = true;
                        break;
                    }
                }
            }

            int newSize = hasExpiredEntries ? _entries.Length : _entries.Length * 2;
            return Resize(newSize, preserveIndices);
        }

        private Container Resize(int newSize, bool preserveIndices)
        {
            int[] newBuckets = new int[newSize];
            InitializeBuckets(newBuckets);
            Entry[] newEntries = new Entry[newSize];
            int newEntryIndex = 0;

            _invalid = true;
            if (preserveIndices)
            {
                for (; newEntryIndex < _firstFreeEntry; newEntryIndex++)
                {
                    TransferEntry(newEntryIndex, newEntryIndex, newBuckets, newEntries);
                }
            }
            else
            {
                for (int oldIndex = 0; oldIndex < _firstFreeEntry; oldIndex++)
                {
                    ref Entry oldEntry = ref _entries[oldIndex];
                    if (oldEntry.HashCode != -1 && oldEntry.Handle.IsAllocated &&
                        oldEntry.Handle.UnsafeGetTarget() is not null)
                    {
                        TransferEntry(oldIndex, newEntryIndex++, newBuckets, newEntries);
                    }
                }
            }

            var replacement = new Container(_parent, newBuckets, newEntries, newEntryIndex);
            _invalid = false;
            GC.KeepAlive(this);
            return replacement;
        }

        private void TransferEntry(
            int oldIndex,
            int newIndex,
            int[] newBuckets,
            Entry[] newEntries)
        {
            ref Entry oldEntry = ref _entries[oldIndex];
            ref Entry newEntry = ref newEntries[newIndex];
            newEntry.HashCode = oldEntry.HashCode;
            newEntry.Handle = oldEntry.Handle;
            oldEntry.Handle = default;

            if (newEntry.HashCode == -1)
            {
                newEntry.Next = -1;
                return;
            }

            int bucket = newEntry.HashCode & (newBuckets.Length - 1);
            newEntry.Next = newBuckets[bucket];
            newBuckets[bucket] = newIndex;
        }

        private void RemoveIndex(int entryIndex)
        {
            ref Entry entry = ref _entries[entryIndex];
            if (entry.HashCode != -1)
            {
                entry.HashCode = -1;
                entry.Handle.UnsafeSetTargetToNull();
            }
        }

        private void VerifyIntegrity()
        {
            if (_invalid || _parent is null || _entries is null)
            {
                throw new InvalidOperationException();
            }
        }

        private static void InitializeBuckets(int[] buckets)
        {
            for (int index = 0; index < buckets.Length; index++)
            {
                buckets[index] = -1;
            }
        }

        ~Container()
        {
            if (_invalid || _parent is null || _entries is null)
            {
                return;
            }

            _invalid = true;
            Entry[] entries = _entries;
            _entries = null!;
            _buckets = null!;
            for (int index = 0; index < entries.Length; index++)
            {
                entries[index].Handle.Dispose();
            }
        }
    }

    private sealed class Enumerator : IEnumerator<KeyValuePair<TKey, TValue>>
    {
        private ConditionalWeakTable<TKey, TValue>? _table;
        private readonly int _maxIndexInclusive;
        private int _currentIndex = -1;
        private KeyValuePair<TKey, TValue> _current;

        internal Enumerator(ConditionalWeakTable<TKey, TValue> table)
        {
            _table = table;
            table._activeEnumeratorRefCount++;
            _maxIndexInclusive = table._container.FirstFreeEntry - 1;
        }

        ~Enumerator() => Dispose();

        public KeyValuePair<TKey, TValue> Current
        {
            get
            {
                if (_currentIndex < 0)
                {
                    throw new InvalidOperationException();
                }

                return _current;
            }
        }

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            ConditionalWeakTable<TKey, TValue>? table = _table;
            if (table is not null)
            {
                while (++_currentIndex <= _maxIndexInclusive)
                {
                    if (table._container.TryGetEntry(
                        _currentIndex,
                        out TKey key,
                        out TValue value))
                    {
                        _current = new KeyValuePair<TKey, TValue>(key, value);
                        return true;
                    }
                }
            }

            return false;
        }

        public void Reset()
        {
        }

        public void Dispose()
        {
            ConditionalWeakTable<TKey, TValue>? table = _table;
            if (table is not null)
            {
                _table = null;
                _current = default;
                table._activeEnumeratorRefCount--;
                GC.SuppressFinalize(this);
            }
        }
    }
}
