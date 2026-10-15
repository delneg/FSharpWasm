// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Adapted from dotnet/runtime System.Private.CoreLib Comparer.cs at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

using System.Globalization;
using System.Runtime.CompilerServices;

namespace System.Collections
{
    [Serializable]
    [TypeForwardedFrom("mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public sealed class Comparer : IComparer
    {
        public static readonly Comparer Default = new(CultureInfo.CurrentCulture);
        public static readonly Comparer DefaultInvariant = new(CultureInfo.InvariantCulture);

        public Comparer(CultureInfo culture)
        {
            ArgumentNullException.ThrowIfNull(culture);
            if (culture != CultureInfo.InvariantCulture)
                throw new PlatformNotSupportedException();
        }

        public int Compare(object? a, object? b)
        {
            if (a == b) return 0;
            if (a == null) return -1;
            if (b == null) return 1;

            if (a is string sa && b is string sb)
                return string.CompareOrdinal(sa, sb);

            if (a is IComparable ia)
                return ia.CompareTo(b);

            if (b is IComparable ib)
                return -ib.CompareTo(a);

            throw new ArgumentException(SR.Argument_ImplementIComparable);
        }
    }
}
