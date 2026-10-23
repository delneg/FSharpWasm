namespace System.Runtime.InteropServices
{
    using Collections.Generic;
    using Threading.Tasks;
    using WebAssembly;

    // The event loop of a world without netwasm:runtime/reactor-host, after Mono's WasiEventLoop:
    // the program drives it from its asynchronous entry point until that entry point's task
    // completes. Zero-delay work runs in order from the ready queue; delays and pollables wait
    // together in one wasi:io/poll call. As on Mono, nothing drives it from a synchronous Main, so
    // work deferred there never runs and a blocking wait on it throws PlatformNotSupportedException.
    internal sealed class CommandWorldScheduler :
        IPlatformScheduler,
        IPlatformPollableScheduler
    {
        private readonly Queue<CommandWorldWork> _ready = new();
        private readonly List<CommandWorldWork> _waiting = new();
        private readonly IPlatformPollableSubscription _clock;
        private readonly IPlatformPoller _poller;
        private readonly IPlatformPollableRelease _pollables;

        internal CommandWorldScheduler(
            IPlatformPollableSubscription clock,
            IPlatformPoller poller,
            IPlatformPollableRelease pollables)
        {
            _clock = clock ?? throw new ArgumentNullException();
            _poller = poller ?? throw new ArgumentNullException();
            _pollables = pollables ?? throw new ArgumentNullException();
        }

        internal int PendingCount => _ready.Count + _waiting.Count;

        public IPlatformSchedule Schedule(Action continuation, ulong delayNanoseconds)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException();
            }

            if (delayNanoseconds != 0)
            {
                return SchedulePollable(
                    continuation,
                    _clock.SubscribeDuration(unchecked((long)delayNanoseconds)));
            }

            var work = new CommandWorldWork(this, continuation, CommandWorldWork.NoPollable);
            _ready.Enqueue(work);
            return work;
        }

        // The scheduler owns the pollable from here and drops it once it fires or is cancelled.
        public IPlatformSchedule SchedulePollable(Action continuation, int pollable)
        {
            if (continuation == null)
            {
                throw new ArgumentNullException();
            }

            if (pollable < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pollable));
            }

            var work = new CommandWorldWork(this, continuation, pollable);
            _waiting.Add(work);
            return work;
        }

        internal void RunUntilCompleted(Task task)
        {
            if (task == null)
            {
                throw new ArgumentNullException();
            }

            while (!task.IsCompleted)
            {
                if (!RunOnce())
                {
                    throw new InvalidOperationException(
                        "The asynchronous entry point cannot complete: no scheduled work or pending I/O remains.");
                }
            }
        }

        // Runs the loop until done() holds. Returns false when nothing is scheduled and done() still
        // does not hold, so the caller can never be released.
        internal bool RunUntil(Func<bool> done)
        {
            if (done == null)
            {
                throw new ArgumentNullException();
            }

            while (!done())
            {
                if (!RunOnce())
                {
                    return false;
                }
            }
            return true;
        }

        // Runs the work that is ready now. When none is, waits once for any pollable and runs the
        // work it released. Returns false when nothing is scheduled at all.
        internal bool RunOnce()
        {
            if (_ready.Count == 0)
            {
                if (_waiting.Count == 0)
                {
                    return false;
                }

                Wait();
            }

            for (var count = _ready.Count; count > 0; count--)
            {
                var work = _ready.Dequeue();
                if (work.TryStart())
                {
                    work.Continuation();
                }
            }

            return true;
        }

        internal void Cancel(CommandWorldWork work)
        {
            if (work.Pollable != CommandWorldWork.NoPollable && _waiting.Remove(work))
            {
                _pollables.Release(work.Pollable);
            }
        }

        private void Wait()
        {
            var pollables = new int[_waiting.Count];
            for (var index = 0; index < pollables.Length; index++)
            {
                pollables[index] = _waiting[index].Pollable;
            }

            var fired = new bool[pollables.Length];
            foreach (var index in _poller.Poll(pollables))
            {
                if (index >= (uint)fired.Length)
                {
                    throw new InvalidOperationException();
                }
                fired[index] = true;
            }

            var kept = 0;
            for (var index = 0; index < pollables.Length; index++)
            {
                var work = _waiting[index];
                if (fired[index])
                {
                    _pollables.Release(work.Pollable);
                    _ready.Enqueue(work);
                }
                else
                {
                    _waiting[kept++] = work;
                }
            }
            _waiting.RemoveRange(kept, pollables.Length - kept);
        }
    }

    // A world with neither a host-driven reactor nor wasi:io/poll cannot wait for anything. As on
    // Mono's single-threaded WASI runtime with nothing pumping its loop, deferred work never runs;
    // awaiting completed work still does, and the program imports nothing for it.
    internal sealed class UnpumpedScheduler :
        IPlatformScheduler,
        IPlatformPollableScheduler
    {
        public IPlatformSchedule Schedule(Action continuation, ulong delayNanoseconds) =>
            Defer(continuation);

        public IPlatformSchedule SchedulePollable(Action continuation, int pollable)
        {
            if (pollable < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(pollable));
            }

            return Defer(continuation);
        }

        private static IPlatformSchedule Defer(Action continuation) =>
            continuation == null ? throw new ArgumentNullException() : new UnpumpedWork();

        private sealed class UnpumpedWork : IPlatformSchedule
        {
            public void Dispose()
            {
            }
        }
    }

    internal sealed class CommandWorldWork : IPlatformSchedule
    {
        internal const int NoPollable = -1;

        private readonly CommandWorldScheduler _scheduler;
        private bool _active = true;

        internal CommandWorldWork(CommandWorldScheduler scheduler, Action continuation, int pollable)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException();
            Continuation = continuation ?? throw new ArgumentNullException();
            Pollable = pollable;
        }

        internal Action Continuation { get; }
        internal int Pollable { get; }

        // Work cancelled after it became ready stays queued but no longer runs.
        internal bool TryStart()
        {
            if (!_active)
            {
                return false;
            }

            _active = false;
            return true;
        }

        public void Dispose()
        {
            if (_active)
            {
                _active = false;
                _scheduler.Cancel(this);
            }
        }
    }

    internal interface IPlatformPoller
    {
        // Blocks until at least one pollable is ready; returns the indexes of the ready ones.
        uint[] Poll(int[] pollables);
    }

    internal interface IPlatformPollableRelease
    {
        void Release(int pollable);
    }

    internal sealed class WasiPoller : IPlatformPoller
    {
        public uint[] Poll(int[] pollables)
        {
            if (pollables == null)
            {
                throw new ArgumentNullException();
            }

            var count = unchecked((nuint)(uint)pollables.Length);
            var addressSize = unchecked((nuint)nuint.Size);
            var handles = CanonicalAbi.Allocate(count * sizeof(int), sizeof(int));
            var result = CanonicalAbi.Allocate(addressSize * 2, addressSize);
            try
            {
                for (var index = 0; index < pollables.Length; index++)
                {
                    CanonicalAbi.WriteInt32(
                        handles, unchecked((nuint)(uint)index) * sizeof(int), pollables[index]);
                }

                WasiPollImports.Poll(handles, count, result);
                var elements = CanonicalAbi.ReadAddress(result, 0);
                var length = CanonicalAbi.ReadAddress(result, addressSize);
                if (length > (nuint)(uint)pollables.Length)
                {
                    throw new InvalidOperationException();
                }

                try
                {
                    var ready = new uint[(int)length];
                    for (var index = 0; index < ready.Length; index++)
                    {
                        ready[index] = unchecked((uint)CanonicalAbi.ReadInt32(
                            elements, unchecked((nuint)(uint)index) * sizeof(int)));
                    }
                    return ready;
                }
                finally
                {
                    if (length != 0)
                    {
                        CanonicalAbi.Free(elements);
                    }
                }
            }
            finally
            {
                CanonicalAbi.Free(result);
                CanonicalAbi.Free(handles);
            }
        }
    }

    internal sealed class WasiPollableRelease : IPlatformPollableRelease
    {
        public void Release(int pollable) => WasiPollImports.Drop(pollable);
    }
}
