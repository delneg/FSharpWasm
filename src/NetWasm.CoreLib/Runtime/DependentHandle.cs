// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

namespace System.Runtime;

public struct DependentHandle : IDisposable
{
    private int _handle;

    public DependentHandle(object? target, object? dependent) =>
        _handle = Runtime.CompilerServices.ConditionalWeakTableRuntime.Create(target, dependent);

    public readonly bool IsAllocated => _handle != 0;

    public object? Target
    {
        readonly get
        {
            ThrowIfNotAllocated();
            return Runtime.CompilerServices.ConditionalWeakTableRuntime.GetKey(_handle);
        }
        set
        {
            ThrowIfNotAllocated();
            if (value is not null)
            {
                throw new InvalidOperationException();
            }

            Runtime.CompilerServices.ConditionalWeakTableRuntime.SetTargetToNull(_handle);
        }
    }

    public object? Dependent
    {
        readonly get
        {
            ThrowIfNotAllocated();
            return Runtime.CompilerServices.ConditionalWeakTableRuntime.GetValue(_handle);
        }
        set
        {
            ThrowIfNotAllocated();
            Runtime.CompilerServices.ConditionalWeakTableRuntime.SetValue(_handle, value);
        }
    }

    public readonly (object? Target, object? Dependent) TargetAndDependent
    {
        get
        {
            ThrowIfNotAllocated();
            object? target = Runtime.CompilerServices.ConditionalWeakTableRuntime.GetKey(_handle);
            object? dependent = target is null
                ? null
                : Runtime.CompilerServices.ConditionalWeakTableRuntime.GetValue(_handle);
            GC.KeepAlive(target);
            return (target, dependent);
        }
    }

    internal readonly object? UnsafeGetTarget() =>
        Runtime.CompilerServices.ConditionalWeakTableRuntime.GetKey(_handle);

    internal readonly object? UnsafeGetTargetAndDependent(out object? dependent)
    {
        object? target = Runtime.CompilerServices.ConditionalWeakTableRuntime.GetKey(_handle);
        dependent = target is null
            ? null
            : Runtime.CompilerServices.ConditionalWeakTableRuntime.GetValue(_handle);
        GC.KeepAlive(target);
        return target;
    }

    internal readonly void UnsafeSetTargetToNull() =>
        Runtime.CompilerServices.ConditionalWeakTableRuntime.SetTargetToNull(_handle);

    internal readonly void UnsafeSetDependent(object? dependent) =>
        Runtime.CompilerServices.ConditionalWeakTableRuntime.SetValue(_handle, dependent);

    public void Dispose()
    {
        int handle = _handle;
        if (handle != 0)
        {
            _handle = 0;
            Runtime.CompilerServices.ConditionalWeakTableRuntime.Release(handle);
        }
    }

    private readonly void ThrowIfNotAllocated()
    {
        if (_handle == 0)
        {
            throw new InvalidOperationException();
        }
    }
}
