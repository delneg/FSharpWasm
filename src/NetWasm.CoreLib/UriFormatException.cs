// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System
{
    /// <summary>
    /// An exception class used when an invalid Uniform Resource Identifier is detected.
    /// </summary>
    [Serializable]
    [System.Runtime.CompilerServices.TypeForwardedFrom("System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089")]
    public class UriFormatException : FormatException
    {
        public UriFormatException() : base()
        {
        }

        public UriFormatException(string? textString) : base(textString)
        {
        }

        public UriFormatException(string? textString, Exception? e) : base(textString, e)
        {
        }
    }
}
