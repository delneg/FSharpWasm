using NetWasm.Compiler.Analysis;

namespace NetWasm.Compiler.Tests;

internal sealed class RecordingReachableMethodBatchObserver : IReachableMethodBatchObserver
{
    private readonly List<ReachableMethodBatchObservation> _observations = [];

    public IReadOnlyList<ReachableMethodBatchObservation> Observations => _observations;

    public void Observe(ReachableMethodBatchObservation observation)
    {
        _observations.Add(observation);
    }
}
