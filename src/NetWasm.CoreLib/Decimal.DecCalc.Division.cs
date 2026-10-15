// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/runtime System.Decimal.DecCalc at commit
// 811225a482702af7ecc35d817966bc70b88a3a23. The x86-specific branches are
// omitted. Most scalar paths use upstream's 64-bit branches. IncreaseScale's
// Buf12 overload uses the upstream limb path to avoid software wide multiplication
// on both Wasm targets, which lack a native high-half 64-bit multiply.

using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace System
{
    public readonly partial struct Decimal
    {
        private static ref DecCalc AsMutable(ref decimal value) =>
            ref Unsafe.As<decimal, DecCalc>(ref value);

        [StructLayout(LayoutKind.Explicit)]
        private partial struct DecCalc
        {
            [FieldOffset(0)]
            private uint uflags;

            [FieldOffset(4)]
            private uint uhi;

            [FieldOffset(8)]
            private uint ulo;

            [FieldOffset(12)]
            private uint umid;

            [FieldOffset(8)]
            private ulong ulomid;

            private uint High
            {
                get => uhi;
                set => uhi = value;
            }

            private uint Low
            {
                get => ulo;
                set => ulo = value;
            }

            private uint Mid
            {
                get => umid;
                set => umid = value;
            }

            private ulong Low64
            {
                get => ulomid;
                set => ulomid = value;
            }

            private const uint SignMask = 0x80000000;
            private const uint ScaleMask = 0x00FF0000;
            private const int ScaleShift = 16;
            private const int DEC_SCALE_MAX = 28;

            private static ReadOnlySpan<uint> UInt32Powers10 =>
            [
                1,
                10,
                100,
                1000,
                10000,
                100000,
                1000000,
                10000000,
                100000000,
                1000000000,
            ];

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static (uint Quotient, uint Remainder) Div64By32(
                ulong dividend,
                uint denominator)
            {
                uint quotient = (uint)(dividend / denominator);
                return (quotient, (uint)dividend - quotient * denominator);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static uint Div96By32(ref Buf12 buffer, uint denominator)
            {
                ulong value;
                ulong quotient;
                ulong remainder;

                if (buffer.U2 != 0)
                {
                    value = buffer.High64;
                    (quotient, remainder) = Math.DivRem(value, denominator);
                    buffer.High64 = quotient;
                    value = (remainder << 32) | buffer.U0;
                    if (value == 0)
                    {
                        return 0;
                    }

                    (quotient, remainder) = Math.DivRem(value, denominator);
                    buffer.U0 = (uint)quotient;
                    return (uint)remainder;
                }

                value = buffer.Low64;
                if (value == 0)
                {
                    return 0;
                }

                (buffer.Low64, remainder) = Math.DivRem(value, denominator);
                return (uint)remainder;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static bool Div96ByConst(
                ref ulong high64,
                ref uint low,
                uint power)
            {
                ulong quotient64 = high64 / power;
                uint quotient = (uint)((((high64 - quotient64 * power) << 32) + low) / power);
                if (low != quotient * power)
                {
                    return false;
                }

                high64 = quotient64;
                low = quotient;
                return true;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void Unscale(ref uint low, ref ulong high64, ref int scale)
            {
                while ((byte)low == 0 && scale >= 8 &&
                    Div96ByConst(ref high64, ref low, 100000000))
                {
                    scale -= 8;
                }

                if ((low & 0xF) == 0 && scale >= 4 &&
                    Div96ByConst(ref high64, ref low, 10000))
                {
                    scale -= 4;
                }

                if ((low & 3) == 0 && scale >= 2 &&
                    Div96ByConst(ref high64, ref low, 100))
                {
                    scale -= 2;
                }

                if ((low & 1) == 0 && scale >= 1 &&
                    Div96ByConst(ref high64, ref low, 10))
                {
                    scale--;
                }
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static ulong Div128By64(ref Buf16 buffer, ulong denominator)
            {
                Debug.Assert(denominator > buffer.High64);

                uint high = Div96By64(ref buffer.High96, denominator);
                uint low = Div96By64(ref buffer.Low96, denominator);
                return ((ulong)high << 32) | low;
            }

            private static uint Div96By64(ref Buf12 buffer, ulong denominator)
            {
                Debug.Assert(denominator > buffer.High64);

                ulong value;
                uint upper = buffer.U2;
                if (upper == 0)
                {
                    value = buffer.Low64;
                    if (value < denominator)
                    {
                        return 0;
                    }

                    (ulong quotient64, buffer.Low64) = Math.DivRem(value, denominator);
                    return (uint)quotient64;
                }

                uint quotient;
                uint denominatorHigh = (uint)(denominator >> 32);
                if (upper >= denominatorHigh)
                {
                    value = buffer.Low64;
                    value -= denominator << 32;
                    quotient = 0;
                    do
                    {
                        quotient--;
                        value += denominator;
                    }
                    while (value >= denominator);

                    buffer.Low64 = value;
                    return quotient;
                }

                ulong upperValue = buffer.High64;
                if (upperValue < denominatorHigh)
                {
                    return 0;
                }

                (quotient, uint remainder) = Div64By32(upperValue, denominatorHigh);
                value = buffer.U0 | ((ulong)remainder << 32);

                ulong product = Math.BigMul(quotient, (uint)denominator);
                value -= product;
                if (value > ~product)
                {
                    do
                    {
                        quotient--;
                        value += denominator;
                    }
                    while (value >= denominator);
                }

                buffer.Low64 = value;
                return quotient;
            }

            private static uint Div128By96(ref Buf16 buffer, ref Buf12 denominatorBuffer)
            {
                Debug.Assert(denominatorBuffer.U2 > buffer.U3);

                ulong dividend = buffer.High64;
                uint denominator = denominatorBuffer.U2;
                if (dividend < denominator)
                {
                    return 0;
                }

                (uint quotient, uint remainder) = Div64By32(dividend, denominator);

                ulong productLow;
                uint productHigh = (uint)Math.BigMul(
                    denominatorBuffer.Low64,
                    quotient,
                    out productLow);
                ulong value = buffer.Low64 - productLow;
                remainder -= productHigh;

                if (value > ~productLow)
                {
                    remainder--;
                    if (remainder < ~productHigh)
                    {
                        goto PositiveRemainder;
                    }
                }
                else if (remainder <= ~productHigh)
                {
                    goto PositiveRemainder;
                }

                productLow = denominatorBuffer.Low64;
                while (true)
                {
                    quotient--;
                    value += productLow;
                    remainder += denominator;

                    if (value < productLow)
                    {
                        if (remainder++ < denominator)
                        {
                            break;
                        }
                    }

                    if (remainder < denominator)
                    {
                        break;
                    }
                }

            PositiveRemainder:
                buffer.Low64 = value;
                buffer.U2 = remainder;
                return quotient;
            }

            private static uint IncreaseScale(ref Buf12 buffer, uint power)
            {
                ulong temporary = (ulong)buffer.U0 * power;
                buffer.U0 = (uint)temporary;
                temporary >>= 32;
                temporary += (ulong)buffer.U1 * power;
                buffer.U1 = (uint)temporary;
                temporary >>= 32;
                temporary += (ulong)buffer.U2 * power;
                buffer.U2 = (uint)temporary;
                return (uint)(temporary >> 32);
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            private static void IncreaseScale(ref Buf16 buffer, uint power)
            {
                ulong high = Math.BigMul(buffer.Low64, power, out ulong low);
                buffer.Low64 = low;
                buffer.High64 = Math.BigMul(buffer.U2, power) + high;
            }

            private static void IncreaseScale64(ref Buf12 buffer, uint power)
            {
                buffer.U2 = (uint)Math.BigMul(buffer.Low64, power, out ulong low);
                buffer.Low64 = low;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            private static int OverflowUnscale(ref Buf12 quotient, int scale, bool sticky)
            {
                if (--scale < 0)
                {
                    Number.ThrowDecimalOverflowException();
                }

                Debug.Assert(quotient.U2 == 0);

                const ulong highBit = 1UL << 32;
                quotient.U2 = (uint)(highBit / 10);

                ulong value = ((highBit % 10) << 32) + quotient.U1;
                uint divided = (uint)(value / 10);
                quotient.U1 = divided;
                value = ((value - divided * 10) << 32) + quotient.U0;
                divided = (uint)(value / 10);
                quotient.U0 = divided;
                uint remainder = (uint)(value - divided * 10);

                if (remainder > 5 ||
                    remainder == 5 && (sticky || (quotient.U0 & 1) != 0))
                {
                    Add32To96(ref quotient, 1);
                }

                return scale;
            }

            private static int SearchScale(ulong resMidLo, uint resHi, int scale)
            {
                const uint OVFL_MAX_9_HI = 4;
                const uint OVFL_MAX_8_HI = 42;
                const uint OVFL_MAX_7_HI = 429;
                const uint OVFL_MAX_6_HI = 4294;
                const uint OVFL_MAX_5_HI = 42949;
                const uint OVFL_MAX_4_HI = 429496;
                const uint OVFL_MAX_3_HI = 4294967;
                const uint OVFL_MAX_2_HI = 42949672;
                const uint OVFL_MAX_1_HI = 429496729;
                const ulong OVFL_MAX_9_MIDLO = 5441186219426131129;

                int curScale = 0;
                if (resHi > OVFL_MAX_1_HI)
                {
                    goto HaveScale;
                }

                PowerOvfl[] powerOvfl = PowerOvflValues;
                if (scale > DEC_SCALE_MAX - 9)
                {
                    curScale = DEC_SCALE_MAX - scale;
                    if (resHi < powerOvfl[curScale - 1].Hi)
                    {
                        goto HaveScale;
                    }
                }
                else if (resHi < OVFL_MAX_9_HI ||
                    resHi == OVFL_MAX_9_HI && resMidLo <= OVFL_MAX_9_MIDLO)
                {
                    return 9;
                }

                if (resHi > OVFL_MAX_5_HI)
                {
                    if (resHi > OVFL_MAX_3_HI)
                    {
                        curScale = 2;
                        if (resHi > OVFL_MAX_2_HI)
                        {
                            curScale--;
                        }
                    }
                    else
                    {
                        curScale = 4;
                        if (resHi > OVFL_MAX_4_HI)
                        {
                            curScale--;
                        }
                    }
                }
                else if (resHi > OVFL_MAX_7_HI)
                {
                    curScale = 6;
                    if (resHi > OVFL_MAX_6_HI)
                    {
                        curScale--;
                    }
                }
                else
                {
                    curScale = 8;
                    if (resHi > OVFL_MAX_8_HI)
                    {
                        curScale--;
                    }
                }

                if (resHi == powerOvfl[curScale - 1].Hi &&
                    resMidLo > powerOvfl[curScale - 1].MidLo)
                {
                    curScale--;
                }

            HaveScale:
                if (curScale + scale < 0)
                {
                    Number.ThrowDecimalOverflowException();
                }

                return curScale;
            }

            private static bool Add32To96(ref Buf12 buffer, uint value)
            {
                if ((buffer.Low64 += value) < value && ++buffer.U2 == 0)
                {
                    return false;
                }

                return true;
            }

            internal static void VarDecDiv(ref DecCalc left, ref DecCalc right)
            {
                Unsafe.SkipInit(out Buf12 quotient);

                uint power;
                int currentScale;
                int scale = (sbyte)(left.uflags - right.uflags >> ScaleShift);
                bool unscale = false;
                uint temporary;

                if ((right.High | right.Mid) == 0)
                {
                    uint denominator = right.Low;
                    if (denominator == 0)
                    {
                        throw new DivideByZeroException();
                    }

                    quotient.Low64 = left.Low64;
                    quotient.U2 = left.High;
                    uint remainder = Div96By32(ref quotient, denominator);

                    while (true)
                    {
                        if (remainder == 0)
                        {
                            if (scale < 0)
                            {
                                currentScale = Math.Min(9, -scale);
                                goto HaveScale;
                            }

                            break;
                        }

                        unscale = true;
                        if (scale == DEC_SCALE_MAX ||
                            (currentScale = SearchScale(
                                quotient.Low64,
                                quotient.U2,
                                scale)) == 0)
                        {
                            temporary = remainder << 1;
                            if (temporary < remainder ||
                                temporary >= denominator &&
                                (temporary > denominator || (quotient.U0 & 1) != 0))
                            {
                                goto RoundUp;
                            }

                            break;
                        }

                    HaveScale:
                        power = UInt32Powers10[currentScale];
                        scale += currentScale;

                        if (IncreaseScale(ref quotient, power) != 0)
                        {
                            Number.ThrowDecimalOverflowException();
                        }

                        ulong value = Math.BigMul(remainder, power);
                        (uint divided, remainder) = Div64By32(value, denominator);

                        if (!Add32To96(ref quotient, divided))
                        {
                            scale = OverflowUnscale(
                                ref quotient,
                                scale,
                                remainder != 0);
                            break;
                        }
                    }
                }
                else
                {
                    temporary = right.High;
                    if (temporary == 0)
                    {
                        temporary = right.Mid;
                    }

                    currentScale = BitOperations.LeadingZeroCount(temporary);
                    Unsafe.SkipInit(out Buf16 remainderBuffer);

                    remainderBuffer.Low64 = left.Low64 << currentScale;
                    remainderBuffer.High64 =
                        (left.Mid + ((ulong)left.High << 32)) >> (32 - currentScale);

                    ulong denominator = right.Low64 << currentScale;

                    if (right.High == 0)
                    {
                        quotient.U2 = 0;
                        quotient.Low64 = Div128By64(ref remainderBuffer, denominator);
                        while (true)
                        {
                            if (remainderBuffer.Low64 == 0)
                            {
                                if (scale < 0)
                                {
                                    currentScale = Math.Min(9, -scale);
                                    goto HaveScale64;
                                }

                                break;
                            }

                            unscale = true;
                            if (scale == DEC_SCALE_MAX ||
                                (currentScale = SearchScale(
                                    quotient.Low64,
                                    quotient.U2,
                                    scale)) == 0)
                            {
                                ulong doubled = remainderBuffer.Low64;
                                if ((long)doubled < 0 ||
                                    (doubled <<= 1) > denominator ||
                                    doubled == denominator && (quotient.U0 & 1) != 0)
                                {
                                    goto RoundUp;
                                }

                                break;
                            }

                        HaveScale64:
                            power = UInt32Powers10[currentScale];
                            scale += currentScale;

                            if (IncreaseScale(ref quotient, power) != 0)
                            {
                                Number.ThrowDecimalOverflowException();
                            }

                            IncreaseScale64(ref remainderBuffer.Low96, power);
                            temporary = Div96By64(
                                ref remainderBuffer.Low96,
                                denominator);
                            if (!Add32To96(ref quotient, temporary))
                            {
                                scale = OverflowUnscale(
                                    ref quotient,
                                    scale,
                                    remainderBuffer.Low64 != 0);
                                break;
                            }
                        }
                    }
                    else
                    {
                        Unsafe.SkipInit(out Buf12 denominatorBuffer);

                        denominatorBuffer.Low64 = denominator;
                        denominatorBuffer.U2 = (uint)(
                            (right.Mid + ((ulong)right.High << 32)) >>
                            (32 - currentScale));

                        quotient.Low64 = Div128By96(
                            ref remainderBuffer,
                            ref denominatorBuffer);
                        quotient.U2 = 0;

                        while (true)
                        {
                            if ((remainderBuffer.Low64 | remainderBuffer.U2) == 0)
                            {
                                if (scale < 0)
                                {
                                    currentScale = Math.Min(9, -scale);
                                    goto HaveScale96;
                                }

                                break;
                            }

                            unscale = true;
                            if (scale == DEC_SCALE_MAX ||
                                (currentScale = SearchScale(
                                    quotient.Low64,
                                    quotient.U2,
                                    scale)) == 0)
                            {
                                if ((int)remainderBuffer.U2 < 0)
                                {
                                    goto RoundUp;
                                }

                                temporary = remainderBuffer.U1 >> 31;
                                remainderBuffer.Low64 <<= 1;
                                remainderBuffer.U2 =
                                    (remainderBuffer.U2 << 1) + temporary;

                                if (remainderBuffer.U2 > denominatorBuffer.U2 ||
                                    remainderBuffer.U2 == denominatorBuffer.U2 &&
                                    (remainderBuffer.Low64 > denominatorBuffer.Low64 ||
                                     remainderBuffer.Low64 == denominatorBuffer.Low64 &&
                                     (quotient.U0 & 1) != 0))
                                {
                                    goto RoundUp;
                                }

                                break;
                            }

                        HaveScale96:
                            power = UInt32Powers10[currentScale];
                            scale += currentScale;

                            if (IncreaseScale(ref quotient, power) != 0)
                            {
                                Number.ThrowDecimalOverflowException();
                            }

                            IncreaseScale(ref remainderBuffer, power);
                            temporary = Div128By96(
                                ref remainderBuffer,
                                ref denominatorBuffer);
                            if (!Add32To96(ref quotient, temporary))
                            {
                                scale = OverflowUnscale(
                                    ref quotient,
                                    scale,
                                    (remainderBuffer.Low64 | remainderBuffer.High64) != 0);
                                break;
                            }
                        }
                    }
                }

            Unscale:
                if (unscale)
                {
                    uint low = quotient.U0;
                    ulong high = quotient.High64;
                    Unscale(ref low, ref high, ref scale);
                    left.Low = low;
                    left.Mid = (uint)high;
                    left.High = (uint)(high >> 32);
                }
                else
                {
                    left.Low64 = quotient.Low64;
                    left.High = quotient.U2;
                }

                left.uflags =
                    ((left.uflags ^ right.uflags) & SignMask) |
                    ((uint)scale << ScaleShift);
                return;

            RoundUp:
                if (++quotient.Low64 == 0 && ++quotient.U2 == 0)
                {
                    scale = OverflowUnscale(ref quotient, scale, true);
                }

                goto Unscale;
            }

            private readonly struct PowerOvfl
            {
                public readonly uint Hi;
                public readonly ulong MidLo;

                public PowerOvfl(uint hi, uint mid, uint lo)
                {
                    Hi = hi;
                    MidLo = ((ulong)mid << 32) + lo;
                }
            }

            private static readonly PowerOvfl[] PowerOvflValues =
            [
                new(429496729, 2576980377, 2576980377),
                new(42949672, 4123168604, 687194767),
                new(4294967, 1271310319, 2645699854),
                new(429496, 3133608139, 694066715),
                new(42949, 2890341191, 2216890319),
                new(4294, 4154504685, 2369172679),
                new(429, 2133437386, 4102387834),
                new(42, 4078814305, 410238783),
            ];

            [StructLayout(LayoutKind.Explicit, Pack = sizeof(uint))]
            private struct Buf12
            {
                [FieldOffset(0)]
                internal uint U0;

                [FieldOffset(4)]
                internal uint U1;

                [FieldOffset(8)]
                internal uint U2;

                [FieldOffset(0)]
                private ulong low64;

                [FieldOffset(4)]
                private ulong high64;

                internal ulong Low64
                {
                    get => low64;
                    set => low64 = value;
                }

                internal ulong High64
                {
                    get => high64;
                    set => high64 = value;
                }
            }

            [StructLayout(LayoutKind.Explicit)]
            private struct Buf16
            {
                [FieldOffset(0)]
                internal uint U0;

                [FieldOffset(4)]
                internal uint U1;

                [FieldOffset(8)]
                internal uint U2;

                [FieldOffset(12)]
                internal uint U3;

                [FieldOffset(0)]
                internal Buf12 Low96;

                [FieldOffset(4)]
                internal Buf12 High96;

                internal ulong Low64
                {
                    get => Low96.Low64;
                    set => Low96.Low64 = value;
                }

                internal ulong High64
                {
                    get => High96.High64;
                    set => High96.High64 = value;
                }
            }
        }
    }
}
