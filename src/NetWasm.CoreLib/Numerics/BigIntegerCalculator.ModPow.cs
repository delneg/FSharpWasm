// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
//
// Adapted from dotnet/runtime BigIntegerCalculator at commit
// 811225a482702af7ecc35d817966bc70b88a3a23. NetWasm retains the managed
// scalar square-and-multiply and grammar-school reduction paths over its
// fixed 32-bit BigInteger magnitude limbs.

using System.Buffers;

namespace System.Numerics
{
    internal static class BigIntegerCalculator
    {
        public static uint[] Pow(
            ReadOnlySpan<uint> value,
            ReadOnlySpan<uint> power,
            ReadOnlySpan<uint> modulus)
        {
            if (modulus.Length == 1)
            {
                var scalar = Pow(value, power, modulus[0]);
                return scalar == 0 ? Array.Empty<uint>() : new uint[] { scalar };
            }

            var bufferLength = checked(modulus.Length * 2);
            var valueBufferLength = Math.Max(value.Length, bufferLength);
            var valueArray = ArrayPool<uint>.Shared.Rent(valueBufferLength);
            var resultArray = ArrayPool<uint>.Shared.Rent(bufferLength);
            var tempArray = ArrayPool<uint>.Shared.Rent(bufferLength);

            try
            {
                Span<uint> valueBuffer = valueArray.AsSpan(0, valueBufferLength);
                Span<uint> resultBuffer = resultArray.AsSpan(0, bufferLength);
                Span<uint> tempBuffer = tempArray.AsSpan(0, bufferLength);
                valueBuffer.Clear();
                resultBuffer.Clear();
                tempBuffer.Clear();

                value.CopyTo(valueBuffer);
                var valueLength = Reduce(valueBuffer, value.Length, modulus);
                resultBuffer[0] = 1;
                var resultLength = 1;

                var powerLength = ActualLength(power);
                for (var limbIndex = 0; limbIndex < powerLength; limbIndex++)
                {
                    var limb = power[limbIndex];
                    var bitCount = limbIndex == powerLength - 1
                        ? 32 - BitOperations.LeadingZeroCount(limb)
                        : 32;

                    for (var bitIndex = 0; bitIndex < bitCount; bitIndex++)
                    {
                        if ((limb & 1) != 0)
                        {
                            resultLength = MultiplySelf(
                                ref resultBuffer,
                                resultLength,
                                valueBuffer.Slice(0, valueLength),
                                ref tempBuffer,
                                modulus);
                        }

                        var isLastBit = limbIndex == powerLength - 1 && bitIndex == bitCount - 1;
                        if (!isLastBit)
                        {
                            valueLength = SquareSelf(
                                ref valueBuffer,
                                valueLength,
                                ref tempBuffer,
                                modulus);
                        }

                        limb >>= 1;
                    }
                }

                if (resultLength == 0) return Array.Empty<uint>();
                var result = new uint[resultLength];
                resultBuffer.Slice(0, resultLength).CopyTo(result);
                return result;
            }
            finally
            {
                ArrayPool<uint>.Shared.Return(valueArray);
                ArrayPool<uint>.Shared.Return(resultArray);
                ArrayPool<uint>.Shared.Return(tempArray);
            }
        }

        private static uint Pow(ReadOnlySpan<uint> value, ReadOnlySpan<uint> power, uint modulus)
        {
            var reducedValue = Remainder(value, modulus);
            var result = 1u % modulus;
            var powerLength = ActualLength(power);

            for (var limbIndex = 0; limbIndex < powerLength; limbIndex++)
            {
                var limb = power[limbIndex];
                var bitCount = limbIndex == powerLength - 1
                    ? 32 - BitOperations.LeadingZeroCount(limb)
                    : 32;

                for (var bitIndex = 0; bitIndex < bitCount; bitIndex++)
                {
                    if ((limb & 1) != 0)
                    {
                        result = (uint)((ulong)result * reducedValue % modulus);
                    }

                    var isLastBit = limbIndex == powerLength - 1 && bitIndex == bitCount - 1;
                    if (!isLastBit)
                    {
                        reducedValue = (uint)((ulong)reducedValue * reducedValue % modulus);
                    }

                    limb >>= 1;
                }
            }

            return result;
        }

        private static int MultiplySelf(
            ref Span<uint> left,
            int leftLength,
            ReadOnlySpan<uint> right,
            ref Span<uint> temp,
            ReadOnlySpan<uint> modulus)
        {
            var productLength = leftLength + right.Length;
            var product = temp.Slice(0, productLength);
            product.Clear();
            Multiply(left.Slice(0, leftLength), right, product);
            left.Clear();
            Swap(ref left, ref temp);
            return Reduce(left, productLength, modulus);
        }

        private static int SquareSelf(
            ref Span<uint> value,
            int valueLength,
            ref Span<uint> temp,
            ReadOnlySpan<uint> modulus)
        {
            var productLength = valueLength * 2;
            var product = temp.Slice(0, productLength);
            product.Clear();
            Multiply(value.Slice(0, valueLength), value.Slice(0, valueLength), product);
            value.Clear();
            Swap(ref value, ref temp);
            return Reduce(value, productLength, modulus);
        }

        private static void Swap(ref Span<uint> left, ref Span<uint> right)
        {
            var temporary = left;
            left = right;
            right = temporary;
        }

        private static void Multiply(ReadOnlySpan<uint> left, ReadOnlySpan<uint> right, Span<uint> result)
        {
            if (left.Length < right.Length)
            {
                var temporary = left;
                left = right;
                right = temporary;
            }

            for (var leftIndex = 0; leftIndex < left.Length; leftIndex++)
            {
                ulong carry = 0;
                for (var rightIndex = 0; rightIndex < right.Length; rightIndex++)
                {
                    var resultIndex = leftIndex + rightIndex;
                    var product = (ulong)left[leftIndex] * right[rightIndex] + result[resultIndex] + carry;
                    result[resultIndex] = (uint)product;
                    carry = product >> 32;
                }

                var carryIndex = leftIndex + right.Length;
                while (carry != 0)
                {
                    var sum = result[carryIndex] + carry;
                    result[carryIndex] = (uint)sum;
                    carry = sum >> 32;
                    carryIndex++;
                }
            }
        }

        private static int Reduce(Span<uint> value, int valueLength, ReadOnlySpan<uint> modulus)
        {
            valueLength = ActualLength(value.Slice(0, valueLength));
            if (valueLength < modulus.Length) return valueLength;

            DivideGrammarSchool(value.Slice(0, valueLength), modulus);
            return ActualLength(value.Slice(0, Math.Min(valueLength, modulus.Length)));
        }

        private static void DivideGrammarSchool(Span<uint> left, ReadOnlySpan<uint> right)
        {
            var divisorHigh = right[^1];
            var divisorLow = right.Length > 1 ? right[^2] : 0;
            var shift = BitOperations.LeadingZeroCount(divisorHigh);
            var backShift = 32 - shift;

            if (shift > 0)
            {
                var divisorNext = right.Length > 2 ? right[^3] : 0;
                divisorHigh = (divisorHigh << shift) | (divisorLow >> backShift);
                divisorLow = (divisorLow << shift) | (divisorNext >> backShift);
            }

            for (var index = left.Length; index >= right.Length; index--)
            {
                var offset = index - right.Length;
                var existingHigh = (uint)index < (uint)left.Length ? left[index] : 0;
                var valueHigh = existingHigh;
                var valueMiddle = left[index - 1];
                var valueLow = index > 1 ? left[index - 2] : 0;

                if (shift > 0)
                {
                    var valueNext = index > 2 ? left[index - 3] : 0;
                    valueHigh = (valueHigh << shift) | (valueMiddle >> backShift);
                    valueMiddle = (valueMiddle << shift) | (valueLow >> backShift);
                    valueLow = (valueLow << shift) | (valueNext >> backShift);
                }

                var digit = valueHigh >= divisorHigh
                    ? uint.MaxValue
                    : Divide(valueHigh, valueMiddle, divisorHigh);

                while (DivideGuessTooBig(digit, valueHigh, valueMiddle, valueLow, divisorHigh, divisorLow))
                {
                    digit--;
                }

                if (digit != 0)
                {
                    var carry = SubtractDivisor(left.Slice(offset), right, digit);
                    if (carry != existingHigh)
                    {
                        AddDivisor(left.Slice(offset), right);
                    }
                }

                if ((uint)index < (uint)left.Length) left[index] = 0;
            }
        }

        private static uint Divide(uint high, uint low, uint divisor) =>
            (uint)((((ulong)high << 32) | low) / divisor);

        private static bool DivideGuessTooBig(
            uint quotient,
            uint valueHigh,
            uint valueMiddle,
            uint valueLow,
            uint divisorHigh,
            uint divisorLow)
        {
            var highProduct = (ulong)divisorHigh * quotient;
            var lowProduct = (ulong)divisorLow * quotient;
            var checkHigh = (uint)(highProduct >> 32);
            var checkMiddle = (uint)highProduct;
            var checkLowHigh = (uint)(lowProduct >> 32);
            var checkLow = (uint)lowProduct;
            var middleSum = checkMiddle + checkLowHigh;
            if (middleSum < checkLowHigh) checkHigh++;

            return checkHigh > valueHigh
                || checkHigh == valueHigh
                && (middleSum > valueMiddle || middleSum == valueMiddle && checkLow > valueLow);
        }

        private static uint SubtractDivisor(Span<uint> left, ReadOnlySpan<uint> right, uint quotient)
        {
            uint carry = 0;
            for (var index = 0; index < right.Length; index++)
            {
                var product = (ulong)right[index] * quotient + carry;
                var low = (uint)product;
                var high = (uint)(product >> 32);
                var original = left[index];
                left[index] = original - low;
                if (original < low) high++;
                carry = high;
            }

            return carry;
        }

        private static uint AddDivisor(Span<uint> left, ReadOnlySpan<uint> right)
        {
            ulong carry = 0;
            for (var index = 0; index < right.Length; index++)
            {
                var sum = (ulong)left[index] + right[index] + carry;
                left[index] = (uint)sum;
                carry = sum >> 32;
            }

            return (uint)carry;
        }

        private static uint Remainder(ReadOnlySpan<uint> left, uint right)
        {
            ulong remainder = 0;
            for (var index = left.Length - 1; index >= 0; index--)
            {
                remainder = ((remainder << 32) | left[index]) % right;
            }

            return (uint)remainder;
        }

        private static int ActualLength(ReadOnlySpan<uint> value)
        {
            var length = value.Length;
            while (length > 0 && value[length - 1] == 0) length--;
            return length;
        }
    }
}
