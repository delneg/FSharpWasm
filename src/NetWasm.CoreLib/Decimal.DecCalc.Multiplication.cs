// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/runtime System.Decimal.DecCalc at commit
// 811225a482702af7ecc35d817966bc70b88a3a23. Wasm's native i64 arithmetic is
// used on both wasm32 and wasm64.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System
{
    public readonly partial struct Decimal
    {
        private partial struct DecCalc
        {
            private const uint TenToPowerNine = 1_000_000_000;
            private const int MaxInt32Scale = 9;
            private const int MaxInt64Scale = 19;

            private static ReadOnlySpan<ulong> UInt64Powers10 =>
            [
                10,
                100,
                1_000,
                10_000,
                100_000,
                1_000_000,
                10_000_000,
                100_000_000,
                1_000_000_000,
                10_000_000_000,
                100_000_000_000,
                1_000_000_000_000,
                10_000_000_000_000,
                100_000_000_000_000,
                1_000_000_000_000_000,
                10_000_000_000_000_000,
                100_000_000_000_000_000,
                1_000_000_000_000_000_000,
                10_000_000_000_000_000_000,
            ];

            /// <summary>
            /// Multiplies two decimal values. On return, <paramref name="left"/>
            /// contains the result.
            /// </summary>
            internal static unsafe void VarDecMul(ref DecCalc left, ref DecCalc right)
            {
                int scale = (byte)(left.uflags + right.uflags >> ScaleShift);

                ulong temporary;
                uint highProduct;
                Unsafe.SkipInit(out Buf24 productBuffer);

                if ((left.High | left.Mid) == 0)
                {
                    if ((right.High | right.Mid) == 0)
                    {
                        var low64 = Math.BigMul(left.Low, right.Low);
                        if (scale > DEC_SCALE_MAX)
                        {
                            if (scale > DEC_SCALE_MAX + MaxInt64Scale)
                            {
                                goto ReturnZero;
                            }

                            scale -= DEC_SCALE_MAX + 1;
                            var power = UInt64Powers10[scale];
                            (low64, var remainder) = Math.DivRem(low64, power);
                            power >>= 1;
                            if (remainder >= power &&
                                (remainder > power || ((uint)low64 & 1) > 0))
                            {
                                low64++;
                            }
                            scale = DEC_SCALE_MAX;
                        }

                        left.Low64 = low64;
                        left.uflags = ((right.uflags ^ left.uflags) & SignMask) |
                            ((uint)scale << ScaleShift);
                        return;
                    }

                    temporary = Math.BigMul(left.Low, right.Low64, out var low);
                    productBuffer.Low64 = low;

                    if (right.High != 0)
                    {
                        temporary += Math.BigMul(left.Low, right.High);
                        if (temporary > uint.MaxValue)
                        {
                            productBuffer.Mid64 = temporary;
                            highProduct = 3;
                            goto SkipScan;
                        }
                    }
                    productBuffer.U2 = (uint)temporary;
                    highProduct = 2;
                }
                else if ((right.High | right.Mid) == 0)
                {
                    temporary = Math.BigMul(left.Low64, right.Low, out var low);
                    productBuffer.Low64 = low;

                    if (left.High != 0)
                    {
                        temporary += Math.BigMul(right.Low, left.High);
                        if (temporary > uint.MaxValue)
                        {
                            productBuffer.Mid64 = temporary;
                            highProduct = 3;
                            goto SkipScan;
                        }
                    }
                    productBuffer.U2 = (uint)temporary;
                    highProduct = 2;
                }
                else
                {
                    var middle64 = Math.BigMul(left.Low64, right.Low64, out temporary);
                    productBuffer.Low64 = temporary;

                    if ((left.High | right.High) != 0)
                    {
                        var high64 = Math.BigMul(left.High, right.High);

                        high64 += Math.BigMul(left.Low64, right.High, out temporary);
                        middle64 += temporary;
                        if (middle64 < temporary)
                        {
                            high64++;
                        }

                        high64 += Math.BigMul(right.Low64, left.High, out temporary);
                        middle64 += temporary;
                        if (middle64 < temporary)
                        {
                            high64++;
                        }

                        productBuffer.Mid64 = middle64;
                        productBuffer.High64 = high64;
                        highProduct = 5;
                    }
                    else
                    {
                        productBuffer.Mid64 = middle64;
                        highProduct = 3;
                    }
                }

                var product = (uint*)&productBuffer;
                while (product[highProduct] == 0)
                {
                    if (highProduct == 0)
                    {
                        goto ReturnZero;
                    }
                    highProduct--;
                }

            SkipScan:
                if (highProduct > 2 || scale > DEC_SCALE_MAX)
                {
                    scale = ScaleResult(&productBuffer, highProduct, scale);
                }

                left.Low64 = productBuffer.Low64;
                left.High = productBuffer.U2;
                left.uflags = ((right.uflags ^ left.uflags) & SignMask) |
                    ((uint)scale << ScaleShift);
                return;

            ReturnZero:
                left = default;
            }

            private static unsafe int ScaleResult(
                Buf24* resultBuffer,
                uint highIndex,
                int scale)
            {
                Debug.Assert(highIndex < Buf24.Length);
                var result = (uint*)resultBuffer;

                var newScale = 0;
                if (highIndex > 2)
                {
                    newScale = (int)highIndex * 32 - 64 - 1;
                    newScale -= BitOperations.LeadingZeroCount(result[highIndex]);
                    newScale = ((newScale * 77) >> 8) + 1;
                    if (newScale > scale)
                    {
                        Number.ThrowDecimalOverflowException();
                    }
                }

                if (newScale < scale - DEC_SCALE_MAX)
                {
                    newScale = scale - DEC_SCALE_MAX;
                }

                if (newScale == 0)
                {
                    return scale;
                }

                scale -= newScale;
                uint sticky = 0;
                uint quotient;
                uint remainder = 0;

                while (true)
                {
                    sticky |= remainder;

                    uint power;
                    switch (newScale)
                    {
                        case 1:
                            power = DivByConst(result, highIndex, out quotient, out remainder, 10);
                            break;
                        case 2:
                            power = DivByConst(result, highIndex, out quotient, out remainder, 100);
                            break;
                        case 3:
                            power = DivByConst(result, highIndex, out quotient, out remainder, 1_000);
                            break;
                        case 4:
                            power = DivByConst(result, highIndex, out quotient, out remainder, 10_000);
                            break;
                        case 5:
                            power = DivByConst(result, highIndex, out quotient, out remainder, 100_000);
                            break;
                        case 6:
                            power = DivByConst(result, highIndex, out quotient, out remainder, 1_000_000);
                            break;
                        case 7:
                            power = DivByConst(result, highIndex, out quotient, out remainder, 10_000_000);
                            break;
                        case 8:
                            power = DivByConst(result, highIndex, out quotient, out remainder, 100_000_000);
                            break;
                        default:
                            power = DivByConst(result, highIndex, out quotient, out remainder, TenToPowerNine);
                            break;
                    }

                    result[highIndex] = quotient;
                    if (quotient == 0 && highIndex != 0)
                    {
                        highIndex--;
                    }

                    newScale -= MaxInt32Scale;
                    if (newScale > 0)
                    {
                        continue;
                    }

                    if (highIndex > 2)
                    {
                        if (scale == 0)
                        {
                            Number.ThrowDecimalOverflowException();
                        }
                        newScale = 1;
                        scale--;
                        continue;
                    }

                    power >>= 1;
                    if (power <= remainder &&
                        (power < remainder || ((result[0] & 1) | sticky) != 0) &&
                        ++result[0] == 0)
                    {
                        uint current = 0;
                        do
                        {
                            Debug.Assert(current + 1 < Buf24.Length);
                        }
                        while (++result[++current] == 0);

                        if (current > 2)
                        {
                            if (scale == 0)
                            {
                                Number.ThrowDecimalOverflowException();
                            }
                            highIndex = current;
                            sticky = 0;
                            remainder = 0;
                            newScale = 1;
                            scale--;
                            continue;
                        }
                    }

                    break;
                }

                return scale;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static unsafe uint DivByConst(
                uint* result,
                uint highIndex,
                out uint quotient,
                out uint remainder,
                uint power)
            {
                var high = result[highIndex];
                (quotient, remainder) = Math.DivRem(high, power);
                for (var index = highIndex - 1; (int)index >= 0; index--)
                {
                    var value = result[index] + ((ulong)remainder << 32);
                    result[index] = (uint)(value / power);
                    remainder = (uint)value - result[index] * power;
                }
                return power;
            }

            [StructLayout(LayoutKind.Explicit)]
            private struct Buf24
            {
                [FieldOffset(0)]
                internal uint U0;

                [FieldOffset(4)]
                internal uint U1;

                [FieldOffset(8)]
                internal uint U2;

                [FieldOffset(12)]
                internal uint U3;

                [FieldOffset(16)]
                internal uint U4;

                [FieldOffset(20)]
                internal uint U5;

                [FieldOffset(0)]
                private ulong low64;

                [FieldOffset(8)]
                private ulong middle64;

                [FieldOffset(16)]
                private ulong high64;

                internal ulong Low64
                {
                    get => low64;
                    set => low64 = value;
                }

                internal ulong Mid64
                {
                    get => middle64;
                    set => middle64 = value;
                }

                internal ulong High64
                {
                    get => high64;
                    set => high64 = value;
                }

                internal const int Length = 6;
            }
        }
    }
}
