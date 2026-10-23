// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Runtime.InteropServices;

namespace System.Threading
{
    // NetWasm runs one thread. These types keep the .NET threading surface that
    // libraries such as FSharp.Core's Async use, on top of the event loop: queued
    // work items, started threads and timers become scheduled work, and a wait
    // on an unsignaled handle runs the loop until the handle is signaled (F#
    // programs; see PlatformWorld.BlockingWaitDrivesEventLoop). Where the loop
    // cannot run, such a wait throws PlatformNotSupportedException.

    public delegate void WaitCallback(object? state);

    public delegate void WaitOrTimerCallback(object? state, bool timedOut);

    public delegate void ThreadStart();

    public delegate void ParameterizedThreadStart(object? obj);

    public static class ThreadPool
    {
        public static bool QueueUserWorkItem(WaitCallback callBack) => QueueUserWorkItem(callBack, null);

        public static bool QueueUserWorkItem(WaitCallback callBack, object? state)
        {
            ArgumentNullException.ThrowIfNull(callBack);
            EventLoopWork.Schedule(() => callBack(state), 0);
            return true;
        }

        public static bool UnsafeQueueUserWorkItem(WaitCallback callBack, object? state) =>
            QueueUserWorkItem(callBack, state);

        public static RegisteredWaitHandle RegisterWaitForSingleObject(
            WaitHandle waitObject,
            WaitOrTimerCallback callBack,
            object? state,
            int millisecondsTimeOutInterval,
            bool executeOnlyOnce)
        {
            ArgumentNullException.ThrowIfNull(waitObject);
            ArgumentNullException.ThrowIfNull(callBack);
            ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeOutInterval, -1);
            var registration = new RegisteredWaitHandle(waitObject, callBack, state, executeOnlyOnce);
            waitObject.Register(registration);
            if (millisecondsTimeOutInterval != Timeout.Infinite)
            {
                registration.Timeout = EventLoopWork.Schedule(
                    () => registration.Fire(timedOut: true),
                    millisecondsTimeOutInterval);
            }
            return registration;
        }

        public static RegisteredWaitHandle RegisterWaitForSingleObject(
            WaitHandle waitObject,
            WaitOrTimerCallback callBack,
            object? state,
            long millisecondsTimeOutInterval,
            bool executeOnlyOnce) =>
            RegisterWaitForSingleObject(
                waitObject,
                callBack,
                state,
                checked((int)millisecondsTimeOutInterval),
                executeOnlyOnce);
    }

    public sealed class RegisteredWaitHandle
    {
        private readonly WaitHandle _handle;
        private readonly WaitOrTimerCallback _callback;
        private readonly object? _state;
        private readonly bool _once;
        private bool _active = true;

        internal RegisteredWaitHandle(WaitHandle handle, WaitOrTimerCallback callback, object? state, bool once)
        {
            _handle = handle;
            _callback = callback;
            _state = state;
            _once = once;
        }

        internal IDisposable? Timeout { get; set; }

        internal void Fire(bool timedOut)
        {
            if (!_active)
            {
                return;
            }
            if (_once)
            {
                Unregister(null);
            }
            _callback(_state, timedOut);
        }

        public bool Unregister(WaitHandle? waitObject)
        {
            if (!_active)
            {
                return false;
            }
            _active = false;
            Timeout?.Dispose();
            _handle.Unregister(this);
            return true;
        }
    }

    public abstract class WaitHandle : IDisposable
    {
        public const int WaitTimeout = 258;

        private RegisteredWaitHandle[] _registrations = [];

        private protected WaitHandle()
        {
        }

        private protected abstract bool IsSignaled { get; }

        private protected virtual void Consume()
        {
        }

        public virtual bool WaitOne() => WaitOne(Timeout.Infinite);

        public virtual bool WaitOne(TimeSpan timeout) => WaitOne(checked((int)timeout.TotalMilliseconds));

        public virtual bool WaitOne(int millisecondsTimeout, bool exitContext) => WaitOne(millisecondsTimeout);

        public virtual bool WaitOne(int millisecondsTimeout)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(millisecondsTimeout, -1);
            if (!IsSignaled && millisecondsTimeout != 0)
            {
                var timedOut = false;
                using var timeout = millisecondsTimeout == Timeout.Infinite
                    ? null
                    : EventLoopWork.Schedule(() => timedOut = true, millisecondsTimeout);
                if (!PlatformServices.RunEventLoopUntil(() => IsSignaled || timedOut) && !IsSignaled)
                {
                    throw new PlatformNotSupportedException(
                        "A blocking wait cannot complete: no scheduled work or pending I/O can signal the handle.");
                }
            }
            if (!IsSignaled)
            {
                return false;
            }
            Consume();
            return true;
        }

        internal void Register(RegisteredWaitHandle registration)
        {
            _registrations = [.. _registrations, registration];
            if (IsSignaled)
            {
                Signal();
            }
        }

        internal void Unregister(RegisteredWaitHandle registration) =>
            _registrations = Array.FindAll(_registrations, item => item != registration);

        private protected void Signal()
        {
            foreach (var registration in _registrations)
            {
                EventLoopWork.Schedule(() => registration.Fire(timedOut: false), 0);
            }
        }

        public virtual void Close() => Dispose();

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool explicitDisposing)
        {
        }
    }

    public enum EventResetMode
    {
        AutoReset = 0,
        ManualReset = 1,
    }

    public class EventWaitHandle : WaitHandle
    {
        private readonly EventResetMode _mode;
        private bool _signaled;

        public EventWaitHandle(bool initialState, EventResetMode mode)
        {
            if (mode is not (EventResetMode.AutoReset or EventResetMode.ManualReset))
            {
                throw new ArgumentOutOfRangeException(nameof(mode));
            }
            _signaled = initialState;
            _mode = mode;
        }

        private protected override bool IsSignaled => _signaled;

        private protected override void Consume()
        {
            if (_mode == EventResetMode.AutoReset)
            {
                _signaled = false;
            }
        }

        public bool Set()
        {
            _signaled = true;
            Signal();
            return true;
        }

        public bool Reset()
        {
            _signaled = false;
            return true;
        }
    }

    public sealed class ManualResetEvent : EventWaitHandle
    {
        public ManualResetEvent(bool initialState) : base(initialState, EventResetMode.ManualReset)
        {
        }
    }

    public sealed class AutoResetEvent : EventWaitHandle
    {
        public AutoResetEvent(bool initialState) : base(initialState, EventResetMode.AutoReset)
        {
        }
    }

    public sealed class Thread
    {
        private static readonly Thread s_main = new(managedThreadId: 1, isThreadPoolThread: false);
        private static readonly Thread s_worker = new(managedThreadId: 2, isThreadPoolThread: true);
        private static Thread? s_current;

        private readonly Delegate? _start;
        private bool _started;

        private Thread(int managedThreadId, bool isThreadPoolThread)
        {
            ManagedThreadId = managedThreadId;
            IsThreadPoolThread = isThreadPoolThread;
        }

        public Thread(ThreadStart start) : this(start, (Delegate)start)
        {
        }

        public Thread(ParameterizedThreadStart start) : this(start, (Delegate)start)
        {
        }

        private Thread(object? start, Delegate starter) : this(managedThreadId: 2, isThreadPoolThread: false)
        {
            ArgumentNullException.ThrowIfNull(start);
            _start = starter;
        }

        // Work that the event loop runs reports itself as a pool thread, so libraries
        // that keep running on "the current pool thread" stay on the loop.
        public static Thread CurrentThread => s_current ?? s_main;

        public int ManagedThreadId { get; }

        public bool IsThreadPoolThread { get; }

        public bool IsBackground { get; set; }

        public string? Name { get; set; }

        public bool IsAlive => _started;

        public void Start() => Start(null);

        public void Start(object? parameter)
        {
            if (_start is null || _started)
            {
                throw new ThreadStateException();
            }
            _started = true;
            var start = _start;
            EventLoopWork.Schedule(() =>
            {
                if (start is ParameterizedThreadStart parameterized)
                {
                    parameterized(parameter);
                }
                else
                {
                    ((ThreadStart)start)();
                }
            }, 0);
        }

        internal static void RunAsWorker(Action work)
        {
            var previous = s_current;
            s_current = s_worker;
            try
            {
                work();
            }
            finally
            {
                s_current = previous;
            }
        }
    }

    public class ThreadStateException : SystemException
    {
        public ThreadStateException() : base("Thread was in an invalid state for the operation being executed.")
        {
        }

        public ThreadStateException(string? message) : base(message)
        {
        }

        public ThreadStateException(string? message, Exception? innerException) : base(message, innerException)
        {
        }
    }

    public sealed class Timer : IDisposable
    {
        private readonly TimerCallback _callback;
        private readonly object? _state;
        private IDisposable? _schedule;
        private long _period;
        private bool _disposed;

        public Timer(TimerCallback callback) : this(callback, null, Timeout.Infinite, Timeout.Infinite)
        {
        }

        public Timer(TimerCallback callback, object? state, int dueTime, int period)
            : this(callback, state, (long)dueTime, period)
        {
        }

        public Timer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            : this(callback, state, (long)dueTime.TotalMilliseconds, (long)period.TotalMilliseconds)
        {
        }

        public Timer(TimerCallback callback, object? state, long dueTime, long period)
        {
            ArgumentNullException.ThrowIfNull(callback);
            _callback = callback;
            _state = state;
            Change(dueTime, period);
        }

        public bool Change(int dueTime, int period) => Change((long)dueTime, period);

        public bool Change(TimeSpan dueTime, TimeSpan period) =>
            Change((long)dueTime.TotalMilliseconds, (long)period.TotalMilliseconds);

        public bool Change(long dueTime, long period)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(dueTime, -1);
            ArgumentOutOfRangeException.ThrowIfLessThan(period, -1);
            ObjectDisposedException.ThrowIf(_disposed, this);
            _schedule?.Dispose();
            _schedule = null;
            _period = period;
            if (dueTime != Timeout.Infinite)
            {
                Arm(dueTime);
            }
            return true;
        }

        private void Arm(long milliseconds) =>
            _schedule = EventLoopWork.Schedule(Tick, milliseconds);

        private void Tick()
        {
            if (_disposed)
            {
                return;
            }
            _schedule = null;
            if (_period > 0)
            {
                Arm(_period);
            }
            _callback(_state);
        }

        public void Dispose()
        {
            _disposed = true;
            _schedule?.Dispose();
            _schedule = null;
        }
    }

    internal static class EventLoopWork
    {
        internal static IDisposable Schedule(Action work, long milliseconds) =>
            PlatformServices.Scheduler.Schedule(
                () => Thread.RunAsWorker(work),
                checked((ulong)milliseconds * 1_000_000UL));
    }
}
