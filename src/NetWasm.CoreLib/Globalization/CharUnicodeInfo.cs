// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text;

namespace System.Globalization;

public static partial class CharUnicodeInfo
{
    public static UnicodeCategory GetUnicodeCategory(char value) =>
        (UnicodeCategory)(CategoriesValues[GetCategoryCasingOffset(value)] & 0x1f);

    public static UnicodeCategory GetUnicodeCategory(int codePoint)
    {
        ValidateCodePoint(codePoint);
        return (UnicodeCategory)(CategoriesValues[GetCategoryCasingOffset((uint)codePoint)] & 0x1f);
    }

    public static UnicodeCategory GetUnicodeCategory(string value, int index)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateIndex(value, index);
        return GetUnicodeCategory(GetCodePoint(value, index));
    }

    public static double GetNumericValue(char value) => GetNumericValue((int)value);

    internal static double GetNumericValue(int codePoint)
    {
        ValidateCodePoint(codePoint);
        var offset = GetNumericGraphemeOffset((uint)codePoint) * sizeof(double);
        return BitConverter.ToDouble(NumericValues.Slice(offset, sizeof(double)));
    }

    public static double GetNumericValue(string value, int index)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateIndex(value, index);
        return GetNumericValue(GetCodePoint(value, index));
    }

    internal static bool GetIsWhiteSpace(char value) =>
        (CategoriesValues[GetCategoryCasingOffset(value)] & 0x80) != 0;

    internal static StrongBidiCategory GetBidiCategory(ReadOnlySpan<char> value, int index)
    {
        int codePoint = value[index];
        if (char.IsHighSurrogate((char)codePoint) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
        {
            codePoint = char.ConvertToUtf32((char)codePoint, value[index + 1]);
        }

        return (StrongBidiCategory)(CategoriesValues[GetCategoryCasingOffset((uint)codePoint)] & 0x60);
    }

    internal static StrongBidiCategory GetBidiCategory(StringBuilder value, int index)
    {
        int codePoint = value[index];
        if (char.IsHighSurrogate((char)codePoint) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]))
        {
            codePoint = char.ConvertToUtf32((char)codePoint, value[index + 1]);
        }

        return (StrongBidiCategory)(CategoriesValues[GetCategoryCasingOffset((uint)codePoint)] & 0x60);
    }

    internal static char ToUpper(char value) => ApplyCasing(value, UppercaseValues);

    internal static uint ToUpper(uint value) => ApplyCasing(value, UppercaseValues);

    internal static char ToLower(char value) => ApplyCasing(value, LowercaseValues);

    internal static uint ToLower(uint value) => ApplyCasing(value, LowercaseValues);

    private static char ApplyCasing(char value, ReadOnlySpan<byte> deltas)
    {
        int byteOffset = GetCategoryCasingOffset(value) * 2;
        short delta = (short)(deltas[byteOffset] | deltas[byteOffset + 1] << 8);
        return (char)(value + delta);
    }

    private static uint ApplyCasing(uint value, ReadOnlySpan<byte> deltas)
    {
        ValidateCodePoint((int)value);
        var byteOffset = GetCategoryCasingOffset(value) * 2;
        var delta = (ushort)(deltas[byteOffset] | deltas[byteOffset + 1] << 8);
        return (value & 0xffff0000u) | (ushort)(value + delta);
    }

    private static int GetCategoryCasingOffset(uint codePoint)
    {
        uint index = CategoryCasingLevel1Index[(int)(codePoint >> 9)];
        int level2Offset = (int)((index << 6) + ((codePoint >> 3) & 0x3e));
        ReadOnlySpan<byte> level2 = CategoryCasingLevel2Index;
        index = (uint)(level2[level2Offset] | level2[level2Offset + 1] << 8);
        return CategoryCasingLevel3Index[(int)((index << 4) + (codePoint & 0x0f))];
    }

    private static int GetNumericGraphemeOffset(uint codePoint)
    {
        uint index = NumericGraphemeLevel1Index[(int)(codePoint >> 9)];
        var level2Offset = (int)((index << 6) + ((codePoint >> 3) & 0x3e));
        var level2 = NumericGraphemeLevel2Index;
        index = (uint)(level2[level2Offset] | level2[level2Offset + 1] << 8);
        return NumericGraphemeLevel3Index[(int)((index << 4) + (codePoint & 0x0f))];
    }

    private static int GetCodePoint(string value, int index)
    {
        var first = value[index];
        if (first is >= '\uD800' and <= '\uDBFF' && index + 1 < value.Length)
        {
            var second = value[index + 1];
            if (second is >= '\uDC00' and <= '\uDFFF')
            {
                return ((first - 0xD800) << 10) + second - 0xDC00 + 0x10000;
            }
        }
        return first;
    }

    private static void ValidateCodePoint(int codePoint)
    {
        if ((uint)codePoint > 0x10ffff) throw new ArgumentOutOfRangeException(nameof(codePoint));
    }

    private static void ValidateIndex(string value, int index)
    {
        if ((uint)index >= (uint)value.Length) throw new ArgumentOutOfRangeException(nameof(index));
    }
}
