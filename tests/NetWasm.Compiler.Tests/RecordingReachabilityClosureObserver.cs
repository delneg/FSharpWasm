using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Tests;

internal sealed class RecordingReachabilityClosureObserver : IReachabilityClosureObserver
{
    private readonly List<ReachabilityClosureObservation> _observations = [];

    public IReadOnlyList<ReachabilityClosureObservation> Observations => _observations;

    public void Observe(ReachabilityClosureObservation observation)
    {
        _observations.Add(observation);
    }
}
