namespace System.Runtime.InteropServices.TimeZones
{
    using System.Collections.Generic;
    using System.Runtime.InteropServices.WebAssembly;

    internal sealed unsafe class WasiReadOnlyFileReader(
        IWasiDescriptorReadInvoker invoker) : IReadOnlyFileReader
    {
        private readonly IWasiDescriptorReadInvoker _invoker =
            invoker ?? throw new ArgumentNullException();

        // Returns fewer than length bytes only at the end of the file.
        public byte[] Read(int handle, long offset, int length)
        {
            if (offset < 0 || length < 0)
            {
                throw new ArgumentOutOfRangeException();
            }
            var chunks = new List<byte[]>();
            var count = 0;
            while (count < length)
            {
                var chunk = ReadChunk(handle, checked(offset + count), length - count);
                if (chunk.Contents.Length == 0 && !chunk.EndOfFile)
                {
                    throw new PlatformNotSupportedException(
                        "WASI filesystem made no progress while reading the timezone asset.");
                }
                chunks.Add(chunk.Contents);
                count += chunk.Contents.Length;
                if (chunk.EndOfFile)
                {
                    break;
                }
            }
            if (chunks.Count == 1)
            {
                return chunks[0];
            }
            var contents = new byte[count];
            var position = 0;
            foreach (var chunk in chunks)
            {
                chunk.CopyTo(contents, position);
                position += chunk.Length;
            }
            return contents;
        }

        private ReadChunkResult ReadChunk(int handle, long offset, int length)
        {
            var addressSize = unchecked((nuint)nuint.Size);
            var payloadOffset = addressSize;
            var result = CanonicalAbi.Allocate(addressSize * 3 + 1, addressSize);
            try
            {
                _invoker.Invoke(handle, length, offset, result);
                var tag = CanonicalAbi.ReadByte(result, 0);
                if (tag == 1)
                {
                    throw new PlatformNotSupportedException(
                        "WASI filesystem could not read the timezone asset (error-code=" +
                        CanonicalAbi.ReadInt32(result, payloadOffset) + ").");
                }
                if (tag != 0)
                {
                    throw new PlatformNotSupportedException(
                        "WASI filesystem returned an invalid read result.");
                }
                var bytesAddress = CanonicalAbi.ReadAddress(result, payloadOffset);
                var bytesLength = CanonicalAbi.ReadAddress(result, payloadOffset + addressSize);
                try
                {
                    if (bytesLength > unchecked((nuint)length))
                    {
                        throw new PlatformNotSupportedException(
                            "WASI filesystem returned an invalid read result.");
                    }
                    var bytes = new byte[(int)bytesLength];
                    new ReadOnlySpan<byte>((void*)bytesAddress, bytes.Length).CopyTo(bytes);
                    return new ReadChunkResult(
                        bytes,
                        ReadBoolean(result, payloadOffset + addressSize * 2));
                }
                finally
                {
                    if (bytesLength != 0)
                    {
                        CanonicalAbi.Free(bytesAddress);
                    }
                }
            }
            finally
            {
                CanonicalAbi.Free(result);
            }
        }

        private static bool ReadBoolean(nuint address, nuint offset) =>
            CanonicalAbi.ReadByte(address, offset) switch
            {
                0 => false,
                1 => true,
                _ => throw new PlatformNotSupportedException(
                    "WASI filesystem returned an invalid end-of-file flag."),
            };

        private readonly struct ReadChunkResult
        {
            internal ReadChunkResult(byte[] contents, bool endOfFile)
            {
                Contents = contents;
                EndOfFile = endOfFile;
            }

            internal byte[] Contents { get; }
            internal bool EndOfFile { get; }
        }
    }
}
