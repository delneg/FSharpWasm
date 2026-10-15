using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using NetWasm.Compiler.ComponentModel.Inlining;

namespace NetWasm.Compiler.ComponentModel.Optimization;

public sealed class WasmOptimizationSettingsResolver : IWasmOptimizationSettingsResolver
{
    private static readonly FrozenDictionary<FinalWasmOptimization, WasmInlineAnnotationSelection> Presets =
        new Dictionary<FinalWasmOptimization, WasmInlineAnnotationSelection>
        {
            [FinalWasmOptimization.None] = WasmInlineAnnotationSelection.SizeAware,
            [FinalWasmOptimization.O0] = WasmInlineAnnotationSelection.SizeAware,
            [FinalWasmOptimization.O1] = WasmInlineAnnotationSelection.Preserve,
            [FinalWasmOptimization.O2] = WasmInlineAnnotationSelection.Preserve,
            [FinalWasmOptimization.O3] = WasmInlineAnnotationSelection.Preserve,
            [FinalWasmOptimization.Os] = WasmInlineAnnotationSelection.SizeAware,
            [FinalWasmOptimization.Oz] = WasmInlineAnnotationSelection.SizeAware,
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<FinalWasmOptimization, LoopInvariantFieldHoistingPolicy>
        FieldHoistingPresets =
        new Dictionary<FinalWasmOptimization, LoopInvariantFieldHoistingPolicy>
        {
            [FinalWasmOptimization.None] = LoopInvariantFieldHoistingPolicy.Disabled,
            [FinalWasmOptimization.O0] = LoopInvariantFieldHoistingPolicy.Disabled,
            [FinalWasmOptimization.O1] = LoopInvariantFieldHoistingPolicy.Disabled,
            [FinalWasmOptimization.O2] = LoopInvariantFieldHoistingPolicy.Conservative,
            [FinalWasmOptimization.O3] = LoopInvariantFieldHoistingPolicy.Conservative,
            [FinalWasmOptimization.Os] = LoopInvariantFieldHoistingPolicy.Disabled,
            [FinalWasmOptimization.Oz] = LoopInvariantFieldHoistingPolicy.Disabled,
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<ManagedInliningHints, WasmInlineAnnotationSelection> Hints =
        new Dictionary<ManagedInliningHints, WasmInlineAnnotationSelection>
        {
            [ManagedInliningHints.IgnoreAggressive] = WasmInlineAnnotationSelection.SizeAware,
            [ManagedInliningHints.Preserve] = WasmInlineAnnotationSelection.Preserve,
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<FinalWasmOptimization,
        StaticDataViewEliminationPolicy> StaticDataViewPresets =
        new Dictionary<FinalWasmOptimization, StaticDataViewEliminationPolicy>
        {
            [FinalWasmOptimization.None] = StaticDataViewEliminationPolicy.Disabled,
            [FinalWasmOptimization.O0] = StaticDataViewEliminationPolicy.Disabled,
            [FinalWasmOptimization.O1] = StaticDataViewEliminationPolicy.Disabled,
            [FinalWasmOptimization.O2] = StaticDataViewEliminationPolicy.Conservative,
            [FinalWasmOptimization.O3] = StaticDataViewEliminationPolicy.Conservative,
            [FinalWasmOptimization.Os] = StaticDataViewEliminationPolicy.Disabled,
            [FinalWasmOptimization.Oz] = StaticDataViewEliminationPolicy.Disabled,
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<FinalWasmOptimization,
        AddressTakenValuePromotionPolicy> AddressTakenValuePromotionPresets =
        new Dictionary<FinalWasmOptimization, AddressTakenValuePromotionPolicy>
        {
            [FinalWasmOptimization.None] = AddressTakenValuePromotionPolicy.Disabled,
            [FinalWasmOptimization.O0] = AddressTakenValuePromotionPolicy.Disabled,
            [FinalWasmOptimization.O1] = AddressTakenValuePromotionPolicy.Disabled,
            [FinalWasmOptimization.O2] = AddressTakenValuePromotionPolicy.Disabled,
            [FinalWasmOptimization.O3] = AddressTakenValuePromotionPolicy.Conservative,
            [FinalWasmOptimization.Os] = AddressTakenValuePromotionPolicy.Disabled,
            [FinalWasmOptimization.Oz] = AddressTakenValuePromotionPolicy.Disabled,
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<FinalWasmOptimization,
        ManagedMethodInliningPolicy> ManagedMethodInliningPresets =
        new Dictionary<FinalWasmOptimization, ManagedMethodInliningPolicy>
        {
            [FinalWasmOptimization.None] = ManagedMethodInliningPolicy.Disabled,
            [FinalWasmOptimization.O0] = ManagedMethodInliningPolicy.Disabled,
            [FinalWasmOptimization.O1] = ManagedMethodInliningPolicy.Disabled,
            [FinalWasmOptimization.O2] = ManagedMethodInliningPolicy.Disabled,
            [FinalWasmOptimization.O3] = ManagedMethodInliningPolicy.Conservative,
            [FinalWasmOptimization.Os] = ManagedMethodInliningPolicy.Disabled,
            [FinalWasmOptimization.Oz] = ManagedMethodInliningPolicy.Disabled,
        }.ToFrozenDictionary();

    private static readonly FrozenDictionary<FinalWasmOptimization,
        BranchPrefixDuplicationPolicy> BranchPrefixDuplicationPresets =
        new Dictionary<FinalWasmOptimization, BranchPrefixDuplicationPolicy>
        {
            [FinalWasmOptimization.None] = BranchPrefixDuplicationPolicy.Disabled,
            [FinalWasmOptimization.O0] = BranchPrefixDuplicationPolicy.Disabled,
            [FinalWasmOptimization.O1] = BranchPrefixDuplicationPolicy.Speed,
            [FinalWasmOptimization.O2] = BranchPrefixDuplicationPolicy.Speed,
            [FinalWasmOptimization.O3] = BranchPrefixDuplicationPolicy.Speed,
            [FinalWasmOptimization.Os] = BranchPrefixDuplicationPolicy.SizeAware,
            [FinalWasmOptimization.Oz] = BranchPrefixDuplicationPolicy.SizeAware,
        }.ToFrozenDictionary();

    public WasmOptimizationSettings Resolve(
        FinalWasmOptimization preset,
        WasmOptimizationOverrides? options = null)
    {
        if (!Presets.TryGetValue(preset, out var presetSelection) ||
            !FieldHoistingPresets.TryGetValue(preset, out var fieldHoisting) ||
            !StaticDataViewPresets.TryGetValue(preset, out var staticDataViews) ||
            !AddressTakenValuePromotionPresets.TryGetValue(
                preset,
                out var addressTakenValuePromotion) ||
            !ManagedMethodInliningPresets.TryGetValue(
                preset,
                out var managedMethodInlining) ||
            !BranchPrefixDuplicationPresets.TryGetValue(
                preset,
                out var branchPrefixDuplication))
        {
            throw new ArgumentOutOfRangeException(nameof(preset));
        }
        var hint = options?.ManagedInliningHints ?? ManagedInliningHints.Auto;
        var managedSelection = presetSelection;
        if (hint != ManagedInliningHints.Auto &&
            !Hints.TryGetValue(hint, out managedSelection))
        {
            throw new ArgumentOutOfRangeException(nameof(options));
        }

        var fieldOverride = options?.LoopInvariantFieldHoisting ??
            ManagedLoopInvariantFieldHoisting.Auto;
        fieldHoisting = fieldOverride switch
        {
            ManagedLoopInvariantFieldHoisting.Auto => fieldHoisting,
            ManagedLoopInvariantFieldHoisting.Disabled =>
                LoopInvariantFieldHoistingPolicy.Disabled,
            ManagedLoopInvariantFieldHoisting.Enabled =>
                LoopInvariantFieldHoistingPolicy.Conservative,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var staticDataOverride = options?.StaticDataViewElimination ??
            ManagedStaticDataViewElimination.Auto;
        staticDataViews = staticDataOverride switch
        {
            ManagedStaticDataViewElimination.Auto => staticDataViews,
            ManagedStaticDataViewElimination.Disabled =>
                StaticDataViewEliminationPolicy.Disabled,
            ManagedStaticDataViewElimination.Enabled =>
                StaticDataViewEliminationPolicy.Conservative,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var valuePromotionOverride = options?.AddressTakenValuePromotion ??
            ManagedAddressTakenValuePromotion.Auto;
        addressTakenValuePromotion = valuePromotionOverride switch
        {
            ManagedAddressTakenValuePromotion.Auto => addressTakenValuePromotion,
            ManagedAddressTakenValuePromotion.Disabled =>
                AddressTakenValuePromotionPolicy.Disabled,
            ManagedAddressTakenValuePromotion.Enabled =>
                AddressTakenValuePromotionPolicy.Conservative,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var methodInliningOverride = options?.ManagedMethodInlining ??
            ManagedMethodInlining.Auto;
        managedMethodInlining = methodInliningOverride switch
        {
            ManagedMethodInlining.Auto => managedMethodInlining,
            ManagedMethodInlining.Disabled => ManagedMethodInliningPolicy.Disabled,
            ManagedMethodInlining.Enabled => ManagedMethodInliningPolicy.Conservative,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var branchPrefixDuplicationOverride =
            options?.BranchPrefixDuplication ??
            ManagedBranchPrefixDuplication.Auto;
        branchPrefixDuplication = branchPrefixDuplicationOverride switch
        {
            ManagedBranchPrefixDuplication.Auto => branchPrefixDuplication,
            ManagedBranchPrefixDuplication.Disabled =>
                BranchPrefixDuplicationPolicy.Disabled,
            ManagedBranchPrefixDuplication.Enabled =>
                BranchPrefixDuplicationPolicy.Speed,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var scalarOverride = options?.ScalarCallPreservation ?? ManagedScalarCallPreservation.Auto;
        var scalarCalls = scalarOverride switch
        {
            ManagedScalarCallPreservation.Auto => preset == FinalWasmOptimization.O3
                ? ScalarCallPreservationPolicy.Conservative : ScalarCallPreservationPolicy.Disabled,
            ManagedScalarCallPreservation.Disabled => ScalarCallPreservationPolicy.Disabled,
            ManagedScalarCallPreservation.Enabled => ScalarCallPreservationPolicy.Conservative,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var arrayBoundsOverride = options?.ArrayBoundsCheckElimination ?? ManagedArrayBoundsCheckElimination.Auto;
        var arrayBounds = arrayBoundsOverride switch
        {
            ManagedArrayBoundsCheckElimination.Auto => preset == FinalWasmOptimization.O3
                ? ArrayBoundsCheckEliminationPolicy.Conservative : ArrayBoundsCheckEliminationPolicy.Disabled,
            ManagedArrayBoundsCheckElimination.Disabled => ArrayBoundsCheckEliminationPolicy.Disabled,
            ManagedArrayBoundsCheckElimination.Enabled => ArrayBoundsCheckEliminationPolicy.Conservative,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var unrollingOverride = options?.LoopUnrolling ?? ManagedLoopUnrolling.Auto;
        var unrolling = unrollingOverride switch
        {
            ManagedLoopUnrolling.Auto => preset == FinalWasmOptimization.O3
                ? LoopUnrollingPolicy.Bounded : LoopUnrollingPolicy.Disabled,
            ManagedLoopUnrolling.Disabled => LoopUnrollingPolicy.Disabled,
            ManagedLoopUnrolling.Enabled => LoopUnrollingPolicy.Bounded,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        var wholeProgramDefaults = preset is FinalWasmOptimization.O2 or FinalWasmOptimization.O3 or
            FinalWasmOptimization.Os or FinalWasmOptimization.Oz;
        var constantOverride = options?.ConstantArgumentPropagation ?? ManagedConstantArgumentPropagation.Auto;
        var constantArguments = constantOverride switch
        {
            ManagedConstantArgumentPropagation.Auto => wholeProgramDefaults
                ? ConstantArgumentPropagationPolicy.Conservative : ConstantArgumentPropagationPolicy.Disabled,
            ManagedConstantArgumentPropagation.Disabled => ConstantArgumentPropagationPolicy.Disabled,
            ManagedConstantArgumentPropagation.Enabled => ConstantArgumentPropagationPolicy.Conservative,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };
        var populationOverride = options?.StaticArrayPopulationElimination ?? ManagedStaticArrayPopulationElimination.Auto;
        var staticPopulation = populationOverride switch
        {
            ManagedStaticArrayPopulationElimination.Auto => wholeProgramDefaults
                ? StaticArrayPopulationEliminationPolicy.Conservative : StaticArrayPopulationEliminationPolicy.Disabled,
            ManagedStaticArrayPopulationElimination.Disabled => StaticArrayPopulationEliminationPolicy.Disabled,
            ManagedStaticArrayPopulationElimination.Enabled => StaticArrayPopulationEliminationPolicy.Conservative,
            _ => throw new ArgumentOutOfRangeException(nameof(options)),
        };

        if (preset == FinalWasmOptimization.None &&
            (hint != ManagedInliningHints.Auto ||
             fieldOverride == ManagedLoopInvariantFieldHoisting.Enabled ||
             staticDataOverride == ManagedStaticDataViewElimination.Enabled ||
             valuePromotionOverride == ManagedAddressTakenValuePromotion.Enabled ||
             methodInliningOverride == ManagedMethodInlining.Enabled ||
             scalarOverride == ManagedScalarCallPreservation.Enabled ||
             arrayBoundsOverride == ManagedArrayBoundsCheckElimination.Enabled ||
             unrollingOverride == ManagedLoopUnrolling.Enabled ||
             constantOverride == ManagedConstantArgumentPropagation.Enabled ||
             populationOverride == ManagedStaticArrayPopulationElimination.Enabled ||
             branchPrefixDuplicationOverride ==
                ManagedBranchPrefixDuplication.Enabled))
        {
            throw new ArgumentException(
                "Managed optimization options require final optimization.", nameof(options));
        }

        return new(
            preset,
            managedSelection,
            presetSelection,
            fieldHoisting,
            staticDataViews,
            addressTakenValuePromotion,
            managedMethodInlining,
            branchPrefixDuplication,
            scalarCalls,
            arrayBounds,
            unrolling,
            constantArguments,
            staticPopulation);
    }
}
