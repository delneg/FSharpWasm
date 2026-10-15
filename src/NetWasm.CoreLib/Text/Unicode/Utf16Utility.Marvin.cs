// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime Utf16Utility.cs at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Text.Unicode
{
    internal static partial class Utf16Utility
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool AllCharsInUInt32AreAscii(uint value)
        {
            return (value & ~0x007F_007Fu) == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint ConvertAllAsciiCharsInUInt32ToUppercase(uint value)
        {
            Debug.Assert(AllCharsInUInt32AreAscii(value));

            uint lowerIndicator = value + 0x0080_0080u - 0x0061_0061u;
            uint upperIndicator = value + 0x0080_0080u - 0x007B_007Bu;
            uint combinedIndicator = lowerIndicator ^ upperIndicator;
            uint mask = (combinedIndicator & 0x0080_0080u) >> 2;

            return value ^ mask;
        }
    }
}
