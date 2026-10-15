// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace System.Threading.Tasks;

internal struct CachedCompletedInt32Task
{
    private Task<int>? _task;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<int> GetTask(int result)
    {
        if (_task is Task<int> task)
        {
            Debug.Assert(task.IsCompletedSuccessfully);
            if (task.Result == result)
                return task;
        }
        return _task = Task.FromResult(result);
    }
}
