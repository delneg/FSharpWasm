// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace System.Collections.Concurrent;

// Managed code runs on a single reactor, so Dictionary provides the storage
// without locks. Retry loops around user factories still preserve the public
// ConcurrentDictionary contract when a factory reenters and changes this map.
public class ConcurrentDictionary<TKey, TValue> :
    IDictionary<TKey, TValue>,
    IReadOnlyDictionary<TKey, TValue>,
    IDictionary
    where TKey : notnull
{
    private const int DefaultCapacity = 31;

    private readonly Dictionary<TKey, TValue> _dictionary;

    public ConcurrentDictionary()
        : this(DefaultConcurrencyLevel, DefaultCapacity, growLockArray: true, null)
    {
    }

    public ConcurrentDictionary(IEqualityComparer<TKey>? comparer)
        : this(DefaultConcurrencyLevel, DefaultCapacity, growLockArray: true, comparer)
    {
    }

    public ConcurrentDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection)
        : this(DefaultConcurrencyLevel, collection, null)
    {
    }

    public ConcurrentDictionary(
        IEnumerable<KeyValuePair<TKey, TValue>> collection,
        IEqualityComparer<TKey>? comparer)
        : this(DefaultConcurrencyLevel, GetCapacityFromCollection(collection), comparer)
    {
        ArgumentNullException.ThrowIfNull(collection);
        InitializeFromCollection(collection);
    }

    public ConcurrentDictionary(int concurrencyLevel, int capacity)
        : this(concurrencyLevel, capacity, growLockArray: false, null)
    {
    }

    public ConcurrentDictionary(
        int concurrencyLevel,
        int capacity,
        IEqualityComparer<TKey>? comparer)
        : this(concurrencyLevel, capacity, growLockArray: false, comparer)
    {
    }

    public ConcurrentDictionary(
        int concurrencyLevel,
        IEnumerable<KeyValuePair<TKey, TValue>> collection,
        IEqualityComparer<TKey>? comparer)
        : this(
            concurrencyLevel,
            GetCapacityFromCollection(collection),
            growLockArray: false,
            comparer)
    {
        ArgumentNullException.ThrowIfNull(collection);
        InitializeFromCollection(collection);
    }

    internal ConcurrentDictionary(
        int concurrencyLevel,
        int capacity,
        bool growLockArray,
        IEqualityComparer<TKey>? comparer)
    {
        if (concurrencyLevel <= 0 && concurrencyLevel != -1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(concurrencyLevel),
                SR.ConcurrentDictionary_ConcurrencyLevelMustBePositiveOrNegativeOne);
        }

        ArgumentOutOfRangeException.ThrowIfNegative(capacity);
        _dictionary = new Dictionary<TKey, TValue>(capacity, comparer);
    }

    public IEqualityComparer<TKey> Comparer => _dictionary.Comparer;

    public AlternateLookup<TAlternateKey> GetAlternateLookup<TAlternateKey>()
        where TAlternateKey : notnull, allows ref struct
    {
        if (!_dictionary.TryGetAlternateLookup<TAlternateKey>(out _))
        {
            throw new InvalidOperationException(SR.InvalidOperation_IncompatibleComparer);
        }

        return new AlternateLookup<TAlternateKey>(this);
    }

    public bool TryGetAlternateLookup<TAlternateKey>(
        out AlternateLookup<TAlternateKey> lookup)
        where TAlternateKey : notnull, allows ref struct
    {
        if (_dictionary.TryGetAlternateLookup<TAlternateKey>(out _))
        {
            lookup = new AlternateLookup<TAlternateKey>(this);
            return true;
        }

        lookup = default;
        return false;
    }

    public int Count => _dictionary.Count;

    public bool IsEmpty => _dictionary.Count == 0;

    public TValue this[TKey key]
    {
        get => _dictionary[key];
        set => _dictionary[key] = value;
    }

    public ICollection<TKey> Keys => GetKeys();

    public ICollection<TValue> Values => GetValues();

    IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;

    IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

    bool ICollection<KeyValuePair<TKey, TValue>>.IsReadOnly => false;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot =>
        throw new NotSupportedException(SR.ConcurrentCollection_SyncRoot_NotSupported);

    bool IDictionary.IsFixedSize => false;

    bool IDictionary.IsReadOnly => false;

    ICollection IDictionary.Keys => (ICollection)Keys;

    ICollection IDictionary.Values => (ICollection)Values;

    object? IDictionary.this[object key]
    {
        get
        {
            ArgumentNullException.ThrowIfNull(key);
            return key is TKey typedKey && TryGetValue(typedKey, out var value)
                ? value
                : null;
        }
        set => this[CastKey(key)] = CastValue(value);
    }

    public TValue AddOrUpdate(
        TKey key,
        TValue addValue,
        Func<TKey, TValue, TValue> updateValueFactory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(updateValueFactory);
        while (true)
        {
            if (TryGetValue(key, out var oldValue))
            {
                var newValue = updateValueFactory(key, oldValue);
                if (TryUpdate(key, newValue, oldValue))
                {
                    return newValue;
                }
            }
            else if (TryAdd(key, addValue))
            {
                return addValue;
            }
        }
    }

    public TValue AddOrUpdate(
        TKey key,
        Func<TKey, TValue> addValueFactory,
        Func<TKey, TValue, TValue> updateValueFactory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(addValueFactory);
        ArgumentNullException.ThrowIfNull(updateValueFactory);
        while (true)
        {
            if (TryGetValue(key, out var oldValue))
            {
                var newValue = updateValueFactory(key, oldValue);
                if (TryUpdate(key, newValue, oldValue))
                {
                    return newValue;
                }
            }
            else
            {
                var addValue = addValueFactory(key);
                if (TryAdd(key, addValue))
                {
                    return addValue;
                }
            }
        }
    }

    public TValue AddOrUpdate<TArg>(
        TKey key,
        Func<TKey, TArg, TValue> addValueFactory,
        Func<TKey, TValue, TArg, TValue> updateValueFactory,
        TArg factoryArgument)
        where TArg : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(addValueFactory);
        ArgumentNullException.ThrowIfNull(updateValueFactory);
        while (true)
        {
            if (TryGetValue(key, out var oldValue))
            {
                var newValue = updateValueFactory(key, oldValue, factoryArgument);
                if (TryUpdate(key, newValue, oldValue))
                {
                    return newValue;
                }
            }
            else
            {
                var addValue = addValueFactory(key, factoryArgument);
                if (TryAdd(key, addValue))
                {
                    return addValue;
                }
            }
        }
    }

    public void Clear() => _dictionary.Clear();

    public bool ContainsKey(TKey key) => _dictionary.ContainsKey(key);

    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() =>
        ((IEnumerable<KeyValuePair<TKey, TValue>>)ToArray()).GetEnumerator();

    public TValue GetOrAdd(TKey key, TValue value)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (TryGetValue(key, out var existing))
        {
            return existing;
        }
        return TryAdd(key, value) ? value : _dictionary[key];
    }

    public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(valueFactory);
        if (TryGetValue(key, out var existing))
        {
            return existing;
        }

        var value = valueFactory(key);
        return TryAdd(key, value) ? value : _dictionary[key];
    }

    public TValue GetOrAdd<TArg>(
        TKey key,
        Func<TKey, TArg, TValue> valueFactory,
        TArg factoryArgument)
        where TArg : allows ref struct
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(valueFactory);
        if (TryGetValue(key, out var existing))
        {
            return existing;
        }

        var value = valueFactory(key, factoryArgument);
        return TryAdd(key, value) ? value : _dictionary[key];
    }

    public KeyValuePair<TKey, TValue>[] ToArray()
    {
        var result = new KeyValuePair<TKey, TValue>[_dictionary.Count];
        ((ICollection<KeyValuePair<TKey, TValue>>)_dictionary).CopyTo(result, 0);
        return result;
    }

    public bool TryAdd(TKey key, TValue value) => _dictionary.TryAdd(key, value);

    public bool TryGetValue(
        TKey key,
        [MaybeNullWhen(false)] out TValue value) =>
        _dictionary.TryGetValue(key, out value);

    public bool TryRemove(
        TKey key,
        [MaybeNullWhen(false)] out TValue value) =>
        _dictionary.Remove(key, out value);

    public bool TryRemove(KeyValuePair<TKey, TValue> item)
    {
        if (!_dictionary.TryGetValue(item.Key, out var value) ||
            !EqualityComparer<TValue>.Default.Equals(value, item.Value))
        {
            return false;
        }

        return _dictionary.Remove(item.Key);
    }

    public bool TryUpdate(TKey key, TValue newValue, TValue comparisonValue)
    {
        if (!_dictionary.TryGetValue(key, out var currentValue) ||
            !EqualityComparer<TValue>.Default.Equals(currentValue, comparisonValue))
        {
            return false;
        }

        _dictionary[key] = newValue;
        return true;
    }

    void ICollection<KeyValuePair<TKey, TValue>>.Add(
        KeyValuePair<TKey, TValue> item) =>
        ((IDictionary<TKey, TValue>)this).Add(item.Key, item.Value);

    bool ICollection<KeyValuePair<TKey, TValue>>.Contains(
        KeyValuePair<TKey, TValue> item) =>
        TryGetValue(item.Key, out var value) &&
        EqualityComparer<TValue>.Default.Equals(value, item.Value);

    void ICollection<KeyValuePair<TKey, TValue>>.CopyTo(
        KeyValuePair<TKey, TValue>[] array,
        int arrayIndex) =>
        ((ICollection<KeyValuePair<TKey, TValue>>)_dictionary).CopyTo(array, arrayIndex);

    bool ICollection<KeyValuePair<TKey, TValue>>.Remove(
        KeyValuePair<TKey, TValue> item) => TryRemove(item);

    void IDictionary<TKey, TValue>.Add(TKey key, TValue value)
    {
        if (!TryAdd(key, value))
        {
            throw new ArgumentException(SR.ConcurrentDictionary_KeyAlreadyExisted);
        }
    }

    bool IDictionary<TKey, TValue>.Remove(TKey key) => TryRemove(key, out _);

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        ValidateCopyIndex(array, index);

        if (array is KeyValuePair<TKey, TValue>[] pairs)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)_dictionary).CopyTo(pairs, index);
            return;
        }

        if (array is DictionaryEntry[] entries)
        {
            foreach (var pair in ToArray())
            {
                entries[index++] = new DictionaryEntry(pair.Key!, pair.Value);
            }
            return;
        }

        if (array is object?[] objects)
        {
            foreach (var pair in ToArray())
            {
                objects[index++] = pair;
            }
            return;
        }

        throw new ArgumentException(SR.ConcurrentDictionary_ArrayIncorrectType, nameof(array));
    }

    void IDictionary.Add(object key, object? value) =>
        ((IDictionary<TKey, TValue>)this).Add(CastKey(key), CastValue(value));

    bool IDictionary.Contains(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key is TKey typedKey && ContainsKey(typedKey);
    }

    IDictionaryEnumerator IDictionary.GetEnumerator() =>
        new DictionaryEnumerator(ToArray());

    void IDictionary.Remove(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key is TKey typedKey)
        {
            TryRemove(typedKey, out _);
        }
    }

    private ReadOnlyCollection<TKey> GetKeys()
    {
        var keys = new List<TKey>(_dictionary.Count);
        foreach (var pair in _dictionary)
        {
            keys.Add(pair.Key);
        }
        return new ReadOnlyCollection<TKey>(keys);
    }

    private ReadOnlyCollection<TValue> GetValues()
    {
        var values = new List<TValue>(_dictionary.Count);
        foreach (var pair in _dictionary)
        {
            values.Add(pair.Value);
        }
        return new ReadOnlyCollection<TValue>(values);
    }

    private static int GetCapacityFromCollection(
        IEnumerable<KeyValuePair<TKey, TValue>> collection) =>
        collection switch
        {
            ICollection<KeyValuePair<TKey, TValue>> c =>
                Math.Max(DefaultCapacity, c.Count),
            IReadOnlyCollection<KeyValuePair<TKey, TValue>> c =>
                Math.Max(DefaultCapacity, c.Count),
            _ => DefaultCapacity,
        };

    private void InitializeFromCollection(
        IEnumerable<KeyValuePair<TKey, TValue>> collection)
    {
        foreach (KeyValuePair<TKey, TValue> pair in collection)
        {
            if (!TryAdd(pair.Key, pair.Value))
            {
                throw new ArgumentException(SR.ConcurrentDictionary_SourceContainsDuplicateKeys);
            }
        }
    }

    private static TKey CastKey(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key is TKey typedKey
            ? typedKey
            : throw new ArgumentException(SR.ConcurrentDictionary_TypeOfKeyIncorrect);
    }

    private static TValue CastValue(object? value)
    {
        if (value is TValue typedValue)
        {
            return typedValue;
        }
        if (value == null && default(TValue) is null)
        {
            return default!;
        }
        throw new ArgumentException(SR.ConcurrentDictionary_TypeOfValueIncorrect);
    }

    private static int DefaultConcurrencyLevel => 1;

    private void ValidateCopyIndex(Array array, int index)
    {
        if (index < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }
        if (index > array.Length || array.Length - index < Count)
        {
            throw new ArgumentException(SR.ConcurrentDictionary_ArrayNotLargeEnough);
        }
    }

    public readonly struct AlternateLookup<TAlternateKey>
        where TAlternateKey : notnull, allows ref struct
    {
        private readonly ConcurrentDictionary<TKey, TValue> _dictionary;

        internal AlternateLookup(ConcurrentDictionary<TKey, TValue> dictionary)
        {
            Debug.Assert(dictionary is not null);
            _dictionary = dictionary;
        }

        public ConcurrentDictionary<TKey, TValue> Dictionary => _dictionary;

        public TValue this[TAlternateKey key]
        {
            get => GetLookup()[key];
            set
            {
                var lookup = GetLookup();
                lookup[key] = value;
            }
        }

        public bool ContainsKey(TAlternateKey key) => GetLookup().ContainsKey(key);

        public bool TryAdd(TAlternateKey key, TValue value) =>
            GetLookup().TryAdd(key, value);

        public bool TryGetValue(
            TAlternateKey key,
            [MaybeNullWhen(false)] out TValue value) =>
            GetLookup().TryGetValue(key, out value);

        public bool TryGetValue(
            TAlternateKey key,
            [MaybeNullWhen(false)] out TKey actualKey,
            [MaybeNullWhen(false)] out TValue value) =>
            GetLookup().TryGetValue(key, out actualKey, out value);

        public bool TryRemove(
            TAlternateKey key,
            [MaybeNullWhen(false)] out TValue value) =>
            GetLookup().Remove(key, out _, out value);

        public bool TryRemove(
            TAlternateKey key,
            [MaybeNullWhen(false)] out TKey actualKey,
            [MaybeNullWhen(false)] out TValue value) =>
            GetLookup().Remove(key, out actualKey, out value);

        private Dictionary<TKey, TValue>.AlternateLookup<TAlternateKey> GetLookup() =>
            _dictionary._dictionary.GetAlternateLookup<TAlternateKey>();
    }

    private sealed class DictionaryEnumerator(
        KeyValuePair<TKey, TValue>[] pairs) : IDictionaryEnumerator
    {
        private int _index = -1;

        public DictionaryEntry Entry => new(CurrentPair.Key!, CurrentPair.Value);

        public object Key => CurrentPair.Key!;

        public object? Value => CurrentPair.Value;

        public object Current => Entry;

        public bool MoveNext()
        {
            if (_index + 1 >= pairs.Length)
            {
                _index = pairs.Length;
                return false;
            }
            _index++;
            return true;
        }

        public void Reset() => _index = -1;

        private KeyValuePair<TKey, TValue> CurrentPair =>
            _index >= 0 && _index < pairs.Length
                ? pairs[_index]
                : throw new InvalidOperationException();
    }
}
