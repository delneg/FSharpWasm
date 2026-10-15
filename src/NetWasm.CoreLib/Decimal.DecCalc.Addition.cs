// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/runtime System.Decimal.DecCalc at commit
// 811225a482702af7ecc35d817966bc70b88a3a23. Wasm's native i64 arithmetic is
// used on both wasm32 and wasm64.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System
{
    public readonly partial struct Decimal
    {
        private partial struct DecCalc
        {
            internal static unsafe void DecAddSub(ref DecCalc d1, ref DecCalc d2, bool sign)
            {
                ulong low64 = d1.Low64;
                uint high = d1.High, flags = d1.uflags, d2flags = d2.uflags;

                uint xorflags = d2flags ^ flags;
                sign ^= (xorflags & SignMask) != 0;

                if ((xorflags & ScaleMask) == 0)
                {
                    goto AlignedAdd;
                }
                else
                {
                    uint d1flags = flags;
                    flags = d2flags & ScaleMask | flags & SignMask;
                    int scale = (int)(flags - d1flags) >> ScaleShift;

                    if (scale < 0)
                    {
                        scale = -scale;
                        flags = d1flags;
                        if (sign)
                            flags ^= SignMask;
                        low64 = d2.Low64;
                        high = d2.High;
                        d2 = d1;
                    }

                    uint power;
                    ulong tmp64;

                    if (high == 0)
                    {
                        if (low64 <= uint.MaxValue)
                        {
                            if ((uint)low64 == 0)
                            {
                                uint signFlags = flags & SignMask;
                                if (sign)
                                    signFlags ^= SignMask;
                                d1 = d2;
                                d1.uflags = d2.uflags & ScaleMask | signFlags;
                                return;
                            }

                            do
                            {
                                if ((uint)scale <= MaxInt32Scale)
                                {
                                    low64 = Math.BigMul((uint)low64, UInt32Powers10[scale]);
                                    goto AlignedAdd;
                                }
                                scale -= MaxInt32Scale;
                                low64 = Math.BigMul((uint)low64, TenToPowerNine);
                            } while (low64 <= uint.MaxValue);
                        }

                        do
                        {
                            power = TenToPowerNine;
                            if ((uint)scale < MaxInt32Scale)
                                power = UInt32Powers10[scale];
                            high = (uint)Math.BigMul(low64, power, out low64);
                            if ((scale -= MaxInt32Scale) <= 0)
                                goto AlignedAdd;
                        } while (high == 0);
                    }

                    while (true)
                    {
                        power = TenToPowerNine;
                        if ((uint)scale < MaxInt32Scale)
                            power = UInt32Powers10[scale];
                        tmp64 = Math.BigMul(low64, power, out low64);
                        tmp64 += Math.BigMul(high, power);

                        scale -= MaxInt32Scale;
                        if (tmp64 > uint.MaxValue)
                            break;

                        high = (uint)tmp64;
                        if (scale <= 0)
                            goto AlignedAdd;
                    }

                    Unsafe.SkipInit(out Buf24 bufNum);

                    bufNum.Low64 = low64;
                    bufNum.Mid64 = tmp64;
                    uint hiProd = 3;

                    for (; scale > 0; scale -= MaxInt32Scale)
                    {
                        power = TenToPowerNine;
                        if ((uint)scale < MaxInt32Scale)
                            power = UInt32Powers10[scale];
                        tmp64 = 0;
                        uint* rgulNum = (uint*)&bufNum;
                        for (uint cur = 0; ;)
                        {
                            Debug.Assert(cur < Buf24.Length);
                            tmp64 += Math.BigMul(rgulNum[cur], power);
                            rgulNum[cur] = (uint)tmp64;
                            cur++;
                            tmp64 >>= 32;
                            if (cur > hiProd)
                                break;
                        }

                        if ((uint)tmp64 != 0)
                        {
                            Debug.Assert(hiProd + 1 < Buf24.Length);
                            rgulNum[++hiProd] = (uint)tmp64;
                        }
                    }

                    tmp64 = bufNum.Low64;
                    low64 = d2.Low64;
                    uint tmpHigh = bufNum.U2;
                    high = d2.High;

                    if (sign)
                    {
                        low64 = tmp64 - low64;
                        high = tmpHigh - high;

                        if (low64 > tmp64)
                        {
                            high--;
                            if (high < tmpHigh)
                                goto NoCarry;
                        }
                        else if (high <= tmpHigh)
                            goto NoCarry;

                        uint* number = (uint*)&bufNum;
                        uint cur = 3;
                        do
                        {
                            Debug.Assert(cur < Buf24.Length);
                        } while (number[cur++]-- == 0);
                        Debug.Assert(hiProd < Buf24.Length);
                        if (number[hiProd] == 0 && --hiProd <= 2)
                            goto ReturnResult;
                    }
                    else
                    {
                        low64 += tmp64;
                        high += tmpHigh;

                        if (low64 < tmp64)
                        {
                            high++;
                            if (high > tmpHigh)
                                goto NoCarry;
                        }
                        else if (high >= tmpHigh)
                            goto NoCarry;

                        uint* number = (uint*)&bufNum;
                        for (uint cur = 3; ++number[cur++] == 0;)
                        {
                            Debug.Assert(cur < Buf24.Length);
                            if (hiProd < cur)
                            {
                                number[cur] = 1;
                                hiProd = cur;
                                break;
                            }
                        }
                    }

                NoCarry:
                    bufNum.Low64 = low64;
                    bufNum.U2 = high;
                    scale = ScaleResult(&bufNum, hiProd, (byte)(flags >> ScaleShift));
                    flags = (flags & ~ScaleMask) | ((uint)scale << ScaleShift);
                    low64 = bufNum.Low64;
                    high = bufNum.U2;
                    goto ReturnResult;
                }

            SignFlip:
                {
                    flags ^= SignMask;
                    high = ~high;
                    low64 = (ulong)-(long)low64;
                    if (low64 == 0)
                        high++;
                    goto ReturnResult;
                }

            AlignedScale:
                {
                    if ((flags & ScaleMask) == 0)
                        Number.ThrowDecimalOverflowException();
                    flags -= 1 << ScaleShift;

                    const uint den = 10;
                    ulong num = high + (1UL << 32);
                    high = (uint)(num / den);
                    num = ((num - high * den) << 32) + (low64 >> 32);
                    uint div = (uint)(num / den);
                    num = ((num - div * den) << 32) + (uint)low64;
                    low64 = div;
                    low64 <<= 32;
                    div = (uint)(num / den);
                    low64 += div;
                    div = (uint)num - div * den;

                    if (div >= 5 && (div > 5 || (low64 & 1) != 0))
                    {
                        if (++low64 == 0)
                            high++;
                    }
                    goto ReturnResult;
                }

            AlignedAdd:
                {
                    ulong d1Low64 = low64;
                    uint d1High = high;
                    if (sign)
                    {
                        low64 = d1Low64 - d2.Low64;
                        high = d1High - d2.High;

                        if (low64 > d1Low64)
                        {
                            high--;
                            if (high >= d1High)
                                goto SignFlip;
                        }
                        else if (high > d1High)
                            goto SignFlip;
                    }
                    else
                    {
                        low64 = d1Low64 + d2.Low64;
                        high = d1High + d2.High;

                        if (low64 < d1Low64)
                        {
                            high++;
                            if (high <= d1High)
                                goto AlignedScale;
                        }
                        else if (high < d1High)
                            goto AlignedScale;
                    }
                    goto ReturnResult;
                }

            ReturnResult:
                d1.uflags = flags;
                d1.High = high;
                d1.Low64 = low64;
                return;
            }
        }
    }
}
