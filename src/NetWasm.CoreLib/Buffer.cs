// Portions derived from dotnet/runtime System.Private.CoreLib.
// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System
{
    public static partial class Buffer
    {
        // memory.copy has memmove semantics. The collectors neither move objects nor use
        // write barriers, so copying references as bytes is safe. Wasmtime runs memory.copy
        // out of line, so copies of up to 16 bytes load every byte before storing any,
        // which also keeps overlapping copies correct.
        internal static unsafe void Memmove<T>(
            ref T destination,
            ref T source,
            int elementCount)
        {
            var byteCount = (nuint)(uint)elementCount * (nuint)Unsafe.SizeOf<T>();
            if (byteCount > 16)
            {
                NativeMemory.Copy(
                    Unsafe.AsPointer(ref source),
                    Unsafe.AsPointer(ref destination),
                    byteCount);
                return;
            }

            ref var to = ref Unsafe.As<T, byte>(ref destination);
            ref var from = ref Unsafe.As<T, byte>(ref source);
            if (byteCount >= 8)
            {
                var head = Unsafe.ReadUnaligned<ulong>(ref from);
                var tail = Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref from, byteCount - 8));
                Unsafe.WriteUnaligned(ref to, head);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref to, byteCount - 8), tail);
            }
            else if (byteCount >= 4)
            {
                var head = Unsafe.ReadUnaligned<uint>(ref from);
                var tail = Unsafe.ReadUnaligned<uint>(ref Unsafe.Add(ref from, byteCount - 4));
                Unsafe.WriteUnaligned(ref to, head);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref to, byteCount - 4), tail);
            }
            else if (byteCount >= 2)
            {
                var head = Unsafe.ReadUnaligned<ushort>(ref from);
                var tail = Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref from, byteCount - 2));
                Unsafe.WriteUnaligned(ref to, head);
                Unsafe.WriteUnaligned(ref Unsafe.Add(ref to, byteCount - 2), tail);
            }
            else if (byteCount == 1)
            {
                to = from;
            }
        }

        public static void BlockCopy(Array src, int srcOffset, Array dst, int dstOffset, int count)
        {
            if (src is null) throw new ArgumentNullException();
            if (dst is null) throw new ArgumentNullException();

            var srcLength = ByteLength(src);
            var dstLength = ReferenceEquals(src, dst) ? srcLength : ByteLength(dst);
            if (srcOffset < 0 || dstOffset < 0 || count < 0) throw new ArgumentOutOfRangeException();
            if (srcOffset > srcLength - count || dstOffset > dstLength - count)
            {
                throw new ArgumentException();
            }

            ref byte source = ref MemoryMarshal.GetArrayDataReference(src);
            ref byte destination = ref MemoryMarshal.GetArrayDataReference(dst);
            Memmove(
                ref Unsafe.Add(ref destination, dstOffset),
                ref Unsafe.Add(ref source, srcOffset),
                count);
        }

        public static int ByteLength(Array array)
        {
            if (array is null) throw new ArgumentNullException();
            if (array is byte[] bytes) return bytes.Length;
            if (array is sbyte[] sbytes) return sbytes.Length;
            if (array is bool[] booleans) return booleans.Length;
            if (array is char[] chars) return checked(chars.Length * 2);
            if (array is short[] shorts) return checked(shorts.Length * 2);
            if (array is ushort[] ushorts) return checked(ushorts.Length * 2);
            if (array is int[] ints) return checked(ints.Length * 4);
            if (array is uint[] uints) return checked(uints.Length * 4);
            if (array is float[] floats) return checked(floats.Length * 4);
            if (array is long[] longs) return checked(longs.Length * 8);
            if (array is ulong[] ulongs) return checked(ulongs.Length * 8);
            if (array is double[] doubles) return checked(doubles.Length * 8);
            if (array is IntPtr[] nativeInts)
            {
                return checked(nativeInts.Length * IntPtr.Size);
            }
            if (array is UIntPtr[] nativeUInts)
            {
                return checked(nativeUInts.Length * IntPtr.Size);
            }
            throw new ArgumentException();
        }

        public static byte GetByte(Array array, int index)
        {
            if ((uint)index >= (uint)ByteLength(array)) throw new ArgumentOutOfRangeException();
            return Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), index);
        }

        public static void SetByte(Array array, int index, byte value)
        {
            if ((uint)index >= (uint)ByteLength(array)) throw new ArgumentOutOfRangeException();
            Unsafe.Add(ref MemoryMarshal.GetArrayDataReference(array), index) = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static unsafe void MemoryCopy(
            void* source,
            void* destination,
            long destinationSizeInBytes,
            long sourceBytesToCopy)
        {
            if (sourceBytesToCopy > destinationSizeInBytes)
            {
                throw new ArgumentOutOfRangeException();
            }

            NativeMemory.Copy(
                source,
                destination,
                checked((nuint)sourceBytesToCopy));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [CLSCompliant(false)]
        public static unsafe void MemoryCopy(
            void* source,
            void* destination,
            ulong destinationSizeInBytes,
            ulong sourceBytesToCopy)
        {
            if (sourceBytesToCopy > destinationSizeInBytes)
            {
                throw new ArgumentOutOfRangeException();
            }

            NativeMemory.Copy(
                source,
                destination,
                checked((nuint)sourceBytesToCopy));
        }
    }
}
