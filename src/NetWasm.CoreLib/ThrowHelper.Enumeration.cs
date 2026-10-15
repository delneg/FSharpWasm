// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime ThrowHelper.cs at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Diagnostics.CodeAnalysis;

namespace System
{
    internal static partial class ThrowHelper
    {
        [DoesNotReturn]
        internal static void ThrowInvalidOperationException_EnumCurrent(int index)
        {
            throw GetInvalidOperationException_EnumCurrent(index);
        }

        [DoesNotReturn]
        internal static void ThrowArgumentException_TupleIncorrectType(object obj)
        {
            throw new ArgumentException(
                SR.Format(SR.ArgumentException_ValueTupleIncorrectType, obj.GetType()),
                "other");
        }

        private static InvalidOperationException GetInvalidOperationException_EnumCurrent(int index)
        {
            return new InvalidOperationException(
                index < 0 ?
                Collections.SR.InvalidOperation_EnumNotStarted :
                Collections.SR.InvalidOperation_EnumEnded);
        }
    }
}
