// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// Ported from dotnet/runtime at commit
// 811225a482702af7ecc35d817966bc70b88a3a23.

namespace System.Collections.Generic
{
    internal enum InsertionBehavior : byte
    {
        None = 0,
        OverwriteExisting = 1,
        ThrowOnExisting = 2,
    }
}
