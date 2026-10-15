// Compatibility support for the scalar Utf8Parser/Utf8Formatter port.
// The upstream implementation is licensed under MIT by the .NET Foundation.

using System.Buffers;
using System.Diagnostics.CodeAnalysis;

namespace System
{
    internal static partial class ThrowHelper
    {
        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException_PrecisionTooLarge()
        {
            throw new ArgumentOutOfRangeException("precision", SR.Format(SR.Argument_PrecisionTooLarge, StandardFormat.MaxPrecision));
        }

        [DoesNotReturn]
        internal static void ThrowArgumentOutOfRangeException_SymbolDoesNotFit()
        {
            throw new ArgumentOutOfRangeException("symbol", SR.Argument_BadFormatSpecifier);
        }

        internal static void ThrowFormatException_BadFormatSpecifier() => throw new FormatException();
        internal static void ThrowArgumentNullException(ExceptionArgument argument) =>
            throw new ArgumentNullException(argument.ToString());
        internal static void ThrowDivideByZeroException() => throw new DivideByZeroException();
        internal static void ThrowNotSupportedException() => throw new NotSupportedException();
        internal static void ThrowOverflowException() => throw new OverflowException();
        internal static void ThrowValueArgumentOutOfRange_NeedNonNegNumException() =>
            throw new ArgumentOutOfRangeException("value");
    }

    internal enum ExceptionArgument
    {
        s,
    }
}
