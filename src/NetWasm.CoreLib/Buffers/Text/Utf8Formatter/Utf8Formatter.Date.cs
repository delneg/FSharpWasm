// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Scalar adaptation: the current profile exposes invariant DateTime text
// directly, without the culture/ICU DateTimeFormat graph.

using System.Text;

namespace System.Buffers.Text;

public static partial class Utf8Formatter
{
    private static bool TryWrite(string value, Span<byte> destination, out int bytesWritten)
    {
        if (value.Length > destination.Length)
        {
            bytesWritten = 0;
            return false;
        }

        for (var index = 0; index < value.Length; index++)
        {
            var character = value[index];
            if (character > 0x7F)
            {
                bytesWritten = 0;
                return false;
            }

            destination[index] = (byte)character;
        }

        bytesWritten = value.Length;
        return true;
    }

    public static bool TryFormat(DateTimeOffset value, Span<byte> destination, out int bytesWritten, StandardFormat format = default(System.Buffers.StandardFormat))
    {
        if (format.IsDefault)
        {
            return TryWrite(value.ToString(), destination, out bytesWritten);
        }

        return format.Symbol switch
        {
            'G' => TryWrite(value.DateTime.ToString("G"), destination, out bytesWritten),
            'R' => TryWriteRfc1123(value.UtcDateTime, destination, out bytesWritten, lowercase: false),
            'l' => TryWriteRfc1123(value.UtcDateTime, destination, out bytesWritten, lowercase: true),
            'O' or 'o' => TryWrite(value.ToString("O"), destination, out bytesWritten),
            _ => ThrowBadDateFormat(out bytesWritten),
        };
    }

    public static bool TryFormat(DateTime value, Span<byte> destination, out int bytesWritten, StandardFormat format = default(System.Buffers.StandardFormat))
    {
        var symbol = format.IsDefault ? 'G' : format.Symbol;
        return symbol switch
        {
            'G' => TryWrite(value.ToString("G"), destination, out bytesWritten),
            'R' => TryWriteRfc1123(value, destination, out bytesWritten, lowercase: false),
            'l' => TryWriteRfc1123(value, destination, out bytesWritten, lowercase: true),
            'O' or 'o' => TryWrite(value.ToString("O"), destination, out bytesWritten),
            _ => ThrowBadDateFormat(out bytesWritten),
        };
    }

    private static bool TryWriteRfc1123(
        DateTime value,
        Span<byte> destination,
        out int bytesWritten,
        bool lowercase)
    {
        if (!TryWrite(
                value.ToString("ddd, dd MMM yyyy HH:mm:ss 'GMT'"),
                destination,
                out bytesWritten))
        {
            return false;
        }

        if (lowercase)
        {
            Ascii.ToLowerInPlace(destination[..bytesWritten], out bytesWritten);
        }
        return true;
    }

    private static bool ThrowBadDateFormat(out int bytesWritten)
    {
        bytesWritten = 0;
        ThrowHelper.ThrowFormatException_BadFormatSpecifier();
        return false;
    }
}
