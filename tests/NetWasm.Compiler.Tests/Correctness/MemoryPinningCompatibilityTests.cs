using System.Collections.Immutable;

namespace NetWasm.Compiler.Tests.Correctness;

[Collection(CorrectnessTestGroup.Name)]
public sealed class MemoryPinningCompatibilityTests(CorrectnessTestRunner runner)
    : CSharpSemanticTestBase(runner)
{
    [Fact]
    public void MemoryAliasesPinsAndEmptyReferencesMatchDesktopOnRealRuntimes() =>
        Run(new(
            "MemoryPinningCompatibility",
            "NetWasm.Correctness.MemoryPinningCompatibility",
            Source,
            [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10])
        {
            AllowUnsafe = true,
            ExpectedReturnValue = 42,
            RequiredRuntimeCapabilities = OracleRuntimeCapabilities.GarbageCollection,
            Matrix = new(
                CorpusMatrixProfile.Extended,
                Backend: CorpusExecutionBackend.Linked),
            OracleMode = OracleMode.SameSource,
            SameSourceReason =
                "The fixture compares supported Memory, pinning and array-data contracts against the production runtime.",
        });

    private const string Source = """
        using System;
        using System.Buffers;
        using System.Runtime.CompilerServices;
        using System.Runtime.InteropServices;

        namespace NetWasm.Correctness.MemoryPinningCompatibility;

        public sealed class ProbeManager : MemoryManager<int>
        {
            private readonly int[] _values = [42];

            public static int HashCalls;

            public override Span<int> GetSpan() => _values;

            public override MemoryHandle Pin(int elementIndex = 0) =>
                _values.AsMemory(elementIndex).Pin();

            public override void Unpin()
            {
            }

            public override int GetHashCode()
            {
                HashCalls++;
                return -1;
            }

            protected override void Dispose(bool disposing)
            {
            }
        }

        public static class EntryPoint
        {
            public static int Run(int input) => input switch
            {
                0 => StringViewsAliasAndRoot(),
                1 => DangerousStringMemoryAliases(),
                2 => OwnedArrayPinRootsAndWrites(),
                3 => StringPinUsesTheRequestedOffset(),
                4 => ReferenceArrayPinsAreRejected(),
                5 => PrePinnedMemoryDoesNotOwnTheExternalHandle(),
                6 => ArrayProjectionPreservesEmptyBackingIdentity(),
                7 => EmptyArrayAndTailReferencesPreserveIdentity(),
                8 => DefaultMemoryPinsToNull(),
                9 => MemoryHashingIgnoresOwnerOverrides(),
                _ => CreateFromPinnedArrayValidatesInRuntimeOrder(),
            };

            public static int Trace() => 0;

            private static int StringViewsAliasAndRoot()
            {
                string? text = new string(new[] { 'a', 'b', 'c', 'd' });
                ReadOnlySpan<char> direct = text.AsSpan(1, 2);
                ReadOnlyMemory<char> memory = text.AsMemory(1, 2);
                ref char directReference = ref MemoryMarshal.GetReference(direct);
                ref char memoryReference = ref MemoryMarshal.GetReference(memory.Span);
                if (!Unsafe.AreSame(ref directReference, ref memoryReference))
                {
                    return -1;
                }

                text = null;
                GC.Collect();
                return direct.Length == 2 && direct[0] == 'b' && direct[1] == 'c'
                    ? 42
                    : -2;
            }

            private static int DangerousStringMemoryAliases()
            {
                var text = new string(new[] { 'a', 'b', 'c' });
                ReadOnlyMemory<char> readOnly = text.AsMemory();
                var writable = MemoryMarshal.AsMemory(readOnly);
                writable.Span[1] = 'x';
                GC.Collect();
                return text[1] == 'x' && readOnly.Span[1] == 'x' ? 42 : -3;
            }

            private static unsafe int OwnedArrayPinRootsAndWrites()
            {
                var values = new[] { 1, 2, 3 };
                using var handle = values.AsMemory(1, 1).Pin();
                if (handle.Pointer == null)
                {
                    return -4;
                }

                *(int*)handle.Pointer = 40;
                GC.Collect();
                *(int*)handle.Pointer += 2;
                return values[1] == 42 ? 42 : -5;
            }

            private static unsafe int StringPinUsesTheRequestedOffset()
            {
                var text = new string(new[] { 'x', '*', 'z' });
                using var handle = text.AsMemory(1, 1).Pin();
                if (handle.Pointer == null || *(char*)handle.Pointer != '*')
                {
                    return -6;
                }

                GC.Collect();
                return *(char*)handle.Pointer == '*' ? 42 : -7;
            }

            private static int ReferenceArrayPinsAreRejected()
            {
                var writableRejected = false;
                var readOnlyRejected = false;
                var emptyRejected = false;
                try
                {
                    new Memory<object>(new object[1]).Pin().Dispose();
                }
                catch (ArgumentException)
                {
                    writableRejected = true;
                }
                try
                {
                    new ReadOnlyMemory<object>(new object[1]).Pin().Dispose();
                }
                catch (ArgumentException)
                {
                    readOnlyRejected = true;
                }
                try
                {
                    new Memory<object>(new object[0]).Pin().Dispose();
                }
                catch (ArgumentException)
                {
                    emptyRejected = true;
                }

                return writableRejected && readOnlyRejected && emptyRejected ? 42 : -8;
            }

            private static unsafe int PrePinnedMemoryDoesNotOwnTheExternalHandle()
            {
                var values = new[] { 1, 2, 3 };
                var external = GCHandle.Alloc(values, GCHandleType.Pinned);
                try
                {
                    var memory = MemoryMarshal.CreateFromPinnedArray(values, 1, 1);
                    ReadOnlyMemory<int> readOnly = memory;
                    using (var first = memory.Pin())
                    {
                        if (first.Pointer == null)
                        {
                            return -9;
                        }
                        *(int*)first.Pointer = 42;
                    }
                    using (var second = readOnly.Pin())
                    {
                        if (second.Pointer == null || *(int*)second.Pointer != 42)
                        {
                            return -10;
                        }
                    }
                    GC.Collect();
                    return external.IsAllocated && ReferenceEquals(external.Target, values) &&
                        values[1] == 42
                        ? 42
                        : -11;
                }
                finally
                {
                    external.Free();
                }
            }

            private static int ArrayProjectionPreservesEmptyBackingIdentity()
            {
                var values = new[] { 7, 8 };
                ReadOnlyMemory<int> empty = new Memory<int>(values, 1, 0);
                if (!MemoryMarshal.TryGetArray(empty, out var segment) ||
                    !ReferenceEquals(segment.Array, values) ||
                    segment.Offset != 1 || segment.Count != 0)
                {
                    return -12;
                }

                var external = GCHandle.Alloc(values, GCHandleType.Pinned);
                try
                {
                    ReadOnlyMemory<int> pinned =
                        MemoryMarshal.CreateFromPinnedArray(values, 1, 0);
                    if (!MemoryMarshal.TryGetArray(pinned, out var pinnedSegment) ||
                        !ReferenceEquals(pinnedSegment.Array, values) ||
                        pinnedSegment.Offset != 1 || pinnedSegment.Count != 0)
                    {
                        return -13;
                    }
                }
                finally
                {
                    external.Free();
                }

                return 42;
            }

            private static int EmptyArrayAndTailReferencesPreserveIdentity()
            {
                var first = new int[0];
                var second = new int[0];
                ref int firstReference = ref MemoryMarshal.GetArrayDataReference(first);
                ref int secondReference = ref MemoryMarshal.GetArrayDataReference(second);
                var firstObjects = new object[0];
                var secondObjects = new object[0];
                ref object firstObjectReference =
                    ref MemoryMarshal.GetArrayDataReference(firstObjects);
                ref object secondObjectReference =
                    ref MemoryMarshal.GetArrayDataReference(secondObjects);
                if (Unsafe.IsNullRef(ref firstReference) ||
                    Unsafe.IsNullRef(ref secondReference) ||
                    Unsafe.AreSame(ref firstReference, ref secondReference) ||
                    Unsafe.IsNullRef(ref firstObjectReference) ||
                    Unsafe.IsNullRef(ref secondObjectReference) ||
                    Unsafe.AreSame(ref firstObjectReference, ref secondObjectReference) ||
                    first.AsSpan() == Span<int>.Empty ||
                    second.AsSpan() == first.AsSpan())
                {
                    return -14;
                }

                int[]? values = [42];
                ReadOnlySpan<int> tail = values.AsSpan(1, 0);
                ref int tailReference = ref MemoryMarshal.GetReference(tail);
                if (Unsafe.IsNullRef(ref tailReference))
                {
                    return -15;
                }
                values = null;
                GC.Collect();
                return !Unsafe.IsNullRef(ref tailReference) ? 42 : -16;
            }

            private static unsafe int DefaultMemoryPinsToNull()
            {
                using var writable = default(Memory<int>).Pin();
                using var readOnly = default(ReadOnlyMemory<int>).Pin();
                return writable.Pointer == null && readOnly.Pointer == null ? 42 : -17;
            }

            private static int MemoryHashingIgnoresOwnerOverrides()
            {
                ProbeManager.HashCalls = 0;
                using var manager = new ProbeManager();
                var writable = manager.Memory;
                ReadOnlyMemory<int> readOnly = writable;
                _ = writable.GetHashCode();
                _ = readOnly.GetHashCode();
                object boxedWritable = writable;
                object boxedReadOnly = readOnly;
                return ProbeManager.HashCalls == 0 &&
                    boxedWritable.Equals(boxedReadOnly) &&
                    boxedReadOnly.Equals(boxedWritable) &&
                    default(Memory<int>).GetHashCode() == 0 &&
                    default(ReadOnlyMemory<int>).GetHashCode() == 0
                    ? 42
                    : -18;
            }

            private static int CreateFromPinnedArrayValidatesInRuntimeOrder()
            {
                var nullRangeRejected = false;
                var covarianceRejectedFirst = false;
                var rangeRejected = false;
                try
                {
                    MemoryMarshal.CreateFromPinnedArray<int>(null, 1, 0);
                }
                catch (ArgumentOutOfRangeException)
                {
                    nullRangeRejected = true;
                }

                object[] covariant = new string[1];
                try
                {
                    MemoryMarshal.CreateFromPinnedArray(covariant, -1, 2);
                }
                catch (ArrayTypeMismatchException)
                {
                    covarianceRejectedFirst = true;
                }

                try
                {
                    MemoryMarshal.CreateFromPinnedArray(new int[1], 2, 0);
                }
                catch (ArgumentOutOfRangeException)
                {
                    rangeRejected = true;
                }

                return nullRangeRejected && covarianceRejectedFirst && rangeRejected
                    ? 42
                    : -19;
            }
        }
        """;
}
