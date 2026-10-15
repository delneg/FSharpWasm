// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Ported from dotnet/runtime System.Decimal.DecCalc at commit
// 811225a482702af7ecc35d817966bc70b88a3a23. The x86-specific branches are
// omitted; Wasm uses the upstream 64-bit scalar path.

namespace System
{
    public readonly partial struct Decimal
    {
        private partial struct DecCalc
        {
            internal static void InternalRound(
                ref DecCalc value,
                uint scale,
                MidpointRounding mode)
            {
                value.uflags -= scale << ScaleShift;

                uint remainder;
                var sticky = 0U;
                uint power;

                while (scale >= MaxInt32Scale)
                {
                    scale -= MaxInt32Scale;

                    const uint divisor = TenToPowerNine;
                    var high = value.uhi;
                    if (high == 0)
                    {
                        var low = value.Low64;
                        var quotient = low / divisor;
                        value.Low64 = quotient;
                        remainder = (uint)(low - quotient * divisor);
                    }
                    else
                    {
                        uint quotient;
                        (value.uhi, remainder) = Math.DivRem(high, divisor);
                        var middle = value.umid;
                        if ((middle | remainder) != 0)
                        {
                            value.umid = quotient =
                                (uint)((((ulong)remainder << 32) | middle) / divisor);
                            remainder = middle - quotient * divisor;
                        }
                        var low = value.ulo;
                        if ((low | remainder) != 0)
                        {
                            value.ulo = quotient =
                                (uint)((((ulong)remainder << 32) | low) / divisor);
                            remainder = low - quotient * divisor;
                        }
                    }
                    power = divisor;
                    if (scale == 0)
                    {
                        goto CheckRemainder;
                    }
                    sticky |= remainder;
                }

                power = UInt32Powers10[(int)scale];
                {
                    var high = value.uhi;
                    if (high == 0)
                    {
                        var low = value.Low64;
                        if (low == 0)
                        {
                            if (mode <= MidpointRounding.ToZero)
                            {
                                goto Done;
                            }
                            remainder = 0;
                            goto CheckRemainder;
                        }
                        var quotient = low / power;
                        value.Low64 = quotient;
                        remainder = (uint)(low - quotient * power);
                    }
                    else
                    {
                        uint quotient;
                        (value.uhi, remainder) = Math.DivRem(high, power);
                        var middle = value.umid;
                        if ((middle | remainder) != 0)
                        {
                            value.umid = quotient =
                                (uint)((((ulong)remainder << 32) | middle) / power);
                            remainder = middle - quotient * power;
                        }
                        var low = value.ulo;
                        if ((low | remainder) != 0)
                        {
                            value.ulo = quotient =
                                (uint)((((ulong)remainder << 32) | low) / power);
                            remainder = low - quotient * power;
                        }
                    }
                }

            CheckRemainder:
                if (mode == MidpointRounding.ToZero)
                {
                    goto Done;
                }
                if (mode == MidpointRounding.ToEven)
                {
                    remainder <<= 1;
                    if ((sticky | value.ulo & 1) != 0)
                    {
                        remainder++;
                    }
                    if (power >= remainder)
                    {
                        goto Done;
                    }
                }
                else if (mode == MidpointRounding.AwayFromZero)
                {
                    remainder <<= 1;
                    if (power > remainder)
                    {
                        goto Done;
                    }
                }
                else if (mode == MidpointRounding.ToNegativeInfinity)
                {
                    if ((remainder | sticky) == 0 || (value.uflags & SignMask) == 0)
                    {
                        goto Done;
                    }
                }
                else if ((remainder | sticky) == 0 || (value.uflags & SignMask) != 0)
                {
                    goto Done;
                }

                if (++value.Low64 == 0)
                {
                    value.uhi++;
                }

            Done:
                return;
            }
        }
    }
}
