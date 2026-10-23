namespace System.Runtime.InteropServices
{
    using CommandLine;
    using Threading.Tasks;
    using TimeZones;

    internal readonly struct PlatformTimestamp
    {
        internal PlatformTimestamp(ulong seconds, uint nanoseconds)
        {
            if (nanoseconds >= 1_000_000_000)
            {
                throw new ArgumentException();
            }
            Seconds = seconds;
            Nanoseconds = nanoseconds;
        }

        internal ulong Seconds { get; }
        internal uint Nanoseconds { get; }
    }

    // Compatibility aggregate retained because existing CoreLib component
    // fixtures implement and inspect both clock readings through this type.
    // Production consumers use IPlatformUtcClock or IPlatformMonotonicClock.
    internal interface IPlatformClock :
        IPlatformUtcClock,
        IPlatformMonotonicClock
    {
    }

    internal interface IPlatformUtcClock
    {
        PlatformTimestamp GetUtcTime();
    }

    internal interface IPlatformMonotonicClock
    {
        ulong GetMonotonicTime();
    }

    internal interface IPlatformUtcClockSource
    {
        PlatformTimestamp GetUtcTime();
    }

    internal interface IPlatformMonotonicClockSource
    {
        ulong GetMonotonicTime();
    }

    internal interface IPlatformSchedule : IDisposable
    {
    }

    internal interface IPlatformScheduler
    {
        IPlatformSchedule Schedule(Action continuation, ulong delayNanoseconds);
    }

    internal interface IPlatformPollableScheduler
    {
        IPlatformSchedule SchedulePollable(Action continuation, int pollable);
    }

    internal interface IPlatformPollableSubscription
    {
        int SubscribeDuration(long durationNanoseconds);
    }

    internal interface IPlatformReactorWatcher
    {
        void Watch(int pollable, uint token);
    }

    internal interface IPlatformReactorCancellation
    {
        void Cancel(uint token);
    }

    // Compatibility aggregate for the stream resource contract. The actual
    // readiness and write actors use the one-action interfaces below.
    internal interface IPlatformOutputStream :
        IDisposable,
        IPlatformOutputReadiness,
        IPlatformOutputWriter,
        IPlatformBlockingOutputWriter
    {
    }

    internal interface IPlatformOutputReadiness
    {
        ulong CheckWrite();
    }

    internal interface IPlatformOutputWriter
    {
        void Write(byte[] contents);
    }

    internal interface IPlatformBlockingOutputWriter
    {
        void WriteBlocking(byte[] contents);
    }

    internal interface IPlatformOutputErrorThrower
    {
        void Throw(nuint result, nuint payloadOffset);
    }

    internal interface IPlatformOutputStreamFactory
    {
        IPlatformOutputStream Create(int handle);
    }

    internal interface IPlatformOutputStreamHandleSource
    {
        int GetStandardOutput();

        int GetStandardError();
    }

    internal interface IPlatformOutputReadinessSource
    {
        ulong CheckWrite(int handle);
    }

    internal interface IPlatformOutputWriterSource
    {
        void Write(int handle, byte[] contents);
    }

    internal interface IPlatformBlockingOutputWriterSource
    {
        void WriteBlocking(int handle, byte[] contents);
    }

    internal interface IPlatformOutputResourceSource
    {
        void Drop(int handle);
    }

    internal interface IPlatformOutputErrorResource
    {
        void Drop(int handle);
    }

    internal interface IPlatformStreams
    {
        IPlatformOutputStream GetStandardOutput();

        IPlatformOutputStream GetStandardError();
    }

    internal static class WasiPlatformServicesComposition
    {
        internal static ILocalTimeOffsetResolver CreateLocalTimeOffsetResolver() =>
            new UtcLocalTimeOffsetResolver();

        internal static ILocalTimePreflightValidator CreateLocalTimePreflightValidator() =>
            new LocalTimePreflightValidator(
                CreateEnvironmentVariableReader(),
                new TimeZoneAssetFileOpener(
                    new WasiPreopenedDirectorySource(new WasiPreopensInvoker()),
                    new WasiReadOnlyFileOpener(new WasiDescriptorOpenInvoker()),
                    new WasiReadOnlyFileReader(new WasiDescriptorReadInvoker())),
                new TimeZoneAssetParser(),
                new TimeZoneDefinitionResolverFactory(new TimeZoneTransitionTableReader()),
                new LocalTimeOffsetResolverFactory(new TimeZoneTransitionResolver()),
                new UtcLocalTimeOffsetResolver(),
                new LocalTimeOffsetResolverInstaller(),
                new TimeZoneCatalogSnapshotInstaller());

        internal static IPlatformUtcClock CreateUtcClock() =>
            new WasiUtcClock(new WasiUtcClockSource());

        internal static ICommandLineArgumentSource CreateCommandLineArgumentSource() =>
            new WasiCommandLineArgumentSource(new WasiCommandLineInvoker());

        internal static IManagedEntryPointArgumentSource
            CreateManagedEntryPointArgumentSource(
                ICommandLineArgumentSource commandLineArguments) =>
            new ManagedEntryPointArgumentSource(commandLineArguments);

        internal static IEnvironmentVariableReader CreateEnvironmentVariableReader() =>
            new EnvironmentVariableReader(new WasiEnvironmentVariableSource(
                new WasiEnvironmentInvoker()));

        internal static IEnvironmentVariableSource CreateInitialEnvironmentVariableSource() =>
            new WasiEnvironmentVariableSource(new WasiEnvironmentInvoker());

        internal static IPlatformMonotonicClock CreateMonotonicClock() =>
            new WasiMonotonicClock(new WasiMonotonicClockSource());

        internal static IPlatformClock CreateClock(
            IPlatformUtcClock utcClock,
            IPlatformMonotonicClock monotonicClock) =>
            new WasiClockBackend(utcClock, monotonicClock);

        internal static WasiPollableReactor CreatePollableReactor()
        {
            var reactor = new WasiPollableReactor(
                new WasiPollableSubscription(),
                new WasiReactorWatcher(),
                new WasiReactorCancellation());
            reactor.RegisterForHostWakeups();
            return reactor;
        }

        internal static CommandWorldScheduler CreateCommandWorldScheduler() =>
            new(new WasiPollableSubscription(), new WasiPoller(), new WasiPollableRelease());

        internal static IPlatformTimerFactory CreateTimerFactory(
            IPlatformScheduler scheduler) =>
            new PlatformTimerFactory(scheduler);

        internal static IPlatformStreams CreateStreams() => new WasiStreamBackend(
            new WasiOutputStreamHandleSource(),
            new WasiOutputStreamFactory(
                new WasiOutputReadinessSource(new WasiOutputErrorThrower(
                    new WasiOutputErrorResource())),
                new WasiOutputWriterSource(new WasiOutputErrorThrower(
                    new WasiOutputErrorResource())),
                new WasiBlockingOutputWriterSource(
                    new WasiBlockingOutputInvoker(),
                    new WasiOutputErrorThrower(new WasiOutputErrorResource())),
                new WasiOutputResourceSource()));
    }

    // System APIs such as DateTime.UtcNow and Task.Delay are framework-mandated
    // static entry points. This type is their composition boundary, not a
    // service locator: it stores the selected capabilities directly and never
    // resolves a service by type or name.
    internal static class PlatformServices
    {
        private static IPlatformClock? _clock;
        private static ICommandLineArgumentSource? _commandLineArguments;
        private static IManagedEntryPointArgumentSource? _managedEntryPointArguments;
        private static IPlatformUtcClock? _utcClock;
        private static IPlatformMonotonicClock? _monotonicClock;
        private static IPlatformPollableScheduler? _pollableScheduler;
        private static CommandWorldScheduler? _commandScheduler;
        private static IPlatformScheduler? _scheduler;
        private static IPlatformTimerFactory? _timerFactory;
        private static IPlatformStreams? _streams;
        private static ILocalTimeOffsetResolver? _localTimeOffsets;
        private static TimeZoneCatalogSnapshot? _timeZoneCatalogSnapshot;
        private static bool _localTimeReady;

        // IPlatformClock is retained as a compatibility aggregate for existing
        // CoreLib fixtures. New consumers should request the one-action view.
        internal static IPlatformClock Clock =>
            _clock ??= WasiPlatformServicesComposition.CreateClock(
                UtcClock,
                MonotonicClock);

        internal static ICommandLineArgumentSource CommandLineArguments =>
            _commandLineArguments ??=
                WasiPlatformServicesComposition.CreateCommandLineArgumentSource();

        internal static string[] ReadCommandLineArguments() =>
            CommandLineArguments.Read();

        internal static IManagedEntryPointArgumentSource ManagedEntryPointArguments =>
            _managedEntryPointArguments ??=
                WasiPlatformServicesComposition.CreateManagedEntryPointArgumentSource(
                    CommandLineArguments);

        internal static string[] ReadManagedEntryPointArguments() =>
            ManagedEntryPointArguments.Read();

        internal static IPlatformUtcClock UtcClock =>
            _utcClock ??= WasiPlatformServicesComposition.CreateUtcClock();

        internal static IPlatformMonotonicClock MonotonicClock =>
            _monotonicClock ??= WasiPlatformServicesComposition.CreateMonotonicClock();

        internal static IPlatformPollableScheduler PollableScheduler
        {
            get
            {
                if (_pollableScheduler == null)
                {
                    ComposeSchedulers();
                }
                return _pollableScheduler!;
            }
        }

        internal static IPlatformScheduler Scheduler
        {
            get
            {
                if (_scheduler == null)
                {
                    ComposeSchedulers();
                }
                return _scheduler!;
            }
        }

        internal static IPlatformTimerFactory TimerFactory =>
            _timerFactory ??=
                WasiPlatformServicesComposition.CreateTimerFactory(Scheduler);

        internal static IPlatformStreams Streams =>
            _streams ??= WasiPlatformServicesComposition.CreateStreams();

        internal static ILocalTimeOffsetResolver LocalTimeOffsets =>
            _localTimeOffsets ??=
                WasiPlatformServicesComposition.CreateLocalTimeOffsetResolver();

        internal static void UseLocalTimeOffsetResolver(
            ILocalTimeOffsetResolver resolver)
        {
            _localTimeOffsets = resolver ?? throw new ArgumentNullException();
        }

        internal static TimeZoneCatalogSnapshot TimeZoneCatalogSnapshot =>
            _timeZoneCatalogSnapshot ??=
                new TimeZoneCatalogSnapshot(null, null, null);

        internal static void UseTimeZoneCatalogSnapshot(
            TimeZoneCatalogSnapshot snapshot)
        {
            _timeZoneCatalogSnapshot = snapshot ?? throw new ArgumentNullException();
        }

        internal static void PreflightLocalTime() =>
            WasiPlatformServicesComposition.CreateLocalTimePreflightValidator()
                .Validate();

        // The selected WIT world decides which scheduler a program can bind: a world whose host
        // drives netwasm:runtime/reactor-host gets the pollable reactor, a world with wasi:io/poll
        // runs its own event loop, and any other world defers work it cannot wait for. A command
        // program never imports the reactor interfaces.
        private static void ComposeSchedulers()
        {
            if (PlatformWorld.ImportsReactorHost())
            {
                var reactor = WasiPlatformServicesComposition.CreatePollableReactor();
                _scheduler = reactor;
                _pollableScheduler = reactor;
            }
            else if (!PlatformWorld.ImportsEventLoop())
            {
                var scheduler = new UnpumpedScheduler();
                _scheduler = scheduler;
                _pollableScheduler = scheduler;
            }
            else
            {
                var scheduler = WasiPlatformServicesComposition.CreateCommandWorldScheduler();
                _scheduler = scheduler;
                _pollableScheduler = scheduler;
                _commandScheduler = scheduler;
            }
        }

        // AsyncEntryPoint calls this with Main's task: the program drives its own event loop until
        // the task completes. Work only reaches the scheduler through an await, so a task that is
        // still running has already composed it.
        internal static void RunEntryPointToCompletion(Task task)
        {
            if (task == null)
            {
                throw new ArgumentNullException();
            }

            // A reactor world's host drives the loop; there the await-semantics observation that
            // follows throws PlatformNotSupportedException for an incomplete task, as on Mono.
            if (task.IsCompleted || PlatformWorld.ImportsReactorHost())
            {
                return;
            }

            if (!PlatformWorld.ImportsEventLoop())
            {
                throw new PlatformNotSupportedException(
                    "The asynchronous entry point cannot complete: the selected world has no wasi:io/poll event loop.");
            }

            if (_commandScheduler == null)
            {
                throw new InvalidOperationException(
                    "The asynchronous entry point cannot complete: no scheduled work or pending I/O remains.");
            }

            _commandScheduler.RunUntilCompleted(task);
        }

        // A blocking wait in an F# program (see PlatformWorld.BlockingWaitDrivesEventLoop): runs the
        // command world's event loop until done() holds. Returns false when it cannot: elsewhere,
        // in a reactor world, or when no scheduled work or pending I/O remains.
        internal static bool RunEventLoopUntil(Func<bool> done)
        {
            if (done == null)
            {
                throw new ArgumentNullException();
            }

            if (done())
            {
                return true;
            }

            if (!PlatformWorld.BlockingWaitDrivesEventLoop() ||
                PlatformWorld.ImportsReactorHost() ||
                !PlatformWorld.ImportsEventLoop())
            {
                return false;
            }

            _ = Scheduler;
            return _commandScheduler != null && _commandScheduler.RunUntil(done);
        }

        internal static void EnsureLocalTimeReady()
        {
            if (_localTimeReady)
            {
                return;
            }
            PreflightLocalTime();
            _localTimeReady = true;
        }

    }
}
