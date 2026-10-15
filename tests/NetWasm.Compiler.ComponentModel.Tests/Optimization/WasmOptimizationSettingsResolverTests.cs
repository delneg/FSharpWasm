using NetWasm.Compiler.ComponentModel.Inlining;
using NetWasm.Compiler.ComponentModel.Optimization;

namespace NetWasm.Compiler.ComponentModel.Tests.Optimization;

public sealed class WasmOptimizationSettingsResolverTests
{
    private static bool WholeProgramDefault(FinalWasmOptimization preset) => preset switch
    {
        FinalWasmOptimization.None or FinalWasmOptimization.O0 or FinalWasmOptimization.O1 => false,
        FinalWasmOptimization.O2 or FinalWasmOptimization.O3 or FinalWasmOptimization.Os or FinalWasmOptimization.Oz => true,
        _ => throw new ArgumentOutOfRangeException(nameof(preset)),
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WholeProgramOverridesAreIndependentOfEachOtherAndThePreset(bool enabled)
    {
        var resolver = Create();
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>().Where(value => value != FinalWasmOptimization.None))
        {
            var baseline = resolver.Resolve(preset);
            var constants = resolver.Resolve(preset, new(ConstantArgumentPropagation: enabled
                ? ManagedConstantArgumentPropagation.Enabled : ManagedConstantArgumentPropagation.Disabled));
            var population = resolver.Resolve(preset, new(StaticArrayPopulationElimination: enabled
                ? ManagedStaticArrayPopulationElimination.Enabled : ManagedStaticArrayPopulationElimination.Disabled));
            Assert.Equal(baseline with
            {
                ConstantArgumentPropagation = enabled ? ConstantArgumentPropagationPolicy.Conservative : ConstantArgumentPropagationPolicy.Disabled,
            }, constants);
            Assert.Equal(baseline with
            {
                StaticArrayPopulationElimination = enabled ? StaticArrayPopulationEliminationPolicy.Conservative : StaticArrayPopulationEliminationPolicy.Disabled,
            }, population);
        }
    }

    [Fact]
    public void WholeProgramOverridesRejectInvalidValuesAndEnabledNone()
    {
        var resolver = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(ConstantArgumentPropagation: (ManagedConstantArgumentPropagation)255)));
        Assert.Throws<ArgumentOutOfRangeException>(() => resolver.Resolve(FinalWasmOptimization.Oz,
            new(StaticArrayPopulationElimination: (ManagedStaticArrayPopulationElimination)255)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(ConstantArgumentPropagation: ManagedConstantArgumentPropagation.Enabled)));
        Assert.Throws<ArgumentException>(() => resolver.Resolve(FinalWasmOptimization.None,
            new(StaticArrayPopulationElimination: ManagedStaticArrayPopulationElimination.Enabled)));
        Assert.Equal(resolver.Resolve(FinalWasmOptimization.None), resolver.Resolve(FinalWasmOptimization.None,
            new(ConstantArgumentPropagation: ManagedConstantArgumentPropagation.Disabled,
                StaticArrayPopulationElimination: ManagedStaticArrayPopulationElimination.Disabled)));
    }

    [Theory]
    [InlineData(FinalWasmOptimization.None, WasmInlineAnnotationSelection.SizeAware,
        LoopInvariantFieldHoistingPolicy.Disabled, StaticDataViewEliminationPolicy.Disabled,
        AddressTakenValuePromotionPolicy.Disabled, ManagedMethodInliningPolicy.Disabled,
        BranchPrefixDuplicationPolicy.Disabled)]
    [InlineData(FinalWasmOptimization.O0, WasmInlineAnnotationSelection.SizeAware,
        LoopInvariantFieldHoistingPolicy.Disabled, StaticDataViewEliminationPolicy.Disabled,
        AddressTakenValuePromotionPolicy.Disabled, ManagedMethodInliningPolicy.Disabled,
        BranchPrefixDuplicationPolicy.Disabled)]
    [InlineData(FinalWasmOptimization.O1, WasmInlineAnnotationSelection.Preserve,
        LoopInvariantFieldHoistingPolicy.Disabled, StaticDataViewEliminationPolicy.Disabled,
        AddressTakenValuePromotionPolicy.Disabled, ManagedMethodInliningPolicy.Disabled,
        BranchPrefixDuplicationPolicy.Speed)]
    [InlineData(FinalWasmOptimization.O2, WasmInlineAnnotationSelection.Preserve,
        LoopInvariantFieldHoistingPolicy.Conservative, StaticDataViewEliminationPolicy.Conservative,
        AddressTakenValuePromotionPolicy.Disabled, ManagedMethodInliningPolicy.Disabled,
        BranchPrefixDuplicationPolicy.Speed)]
    [InlineData(FinalWasmOptimization.O3, WasmInlineAnnotationSelection.Preserve,
        LoopInvariantFieldHoistingPolicy.Conservative, StaticDataViewEliminationPolicy.Conservative,
        AddressTakenValuePromotionPolicy.Conservative, ManagedMethodInliningPolicy.Conservative,
        BranchPrefixDuplicationPolicy.Speed)]
    [InlineData(FinalWasmOptimization.Os, WasmInlineAnnotationSelection.SizeAware,
        LoopInvariantFieldHoistingPolicy.Disabled, StaticDataViewEliminationPolicy.Disabled,
        AddressTakenValuePromotionPolicy.Disabled, ManagedMethodInliningPolicy.Disabled,
        BranchPrefixDuplicationPolicy.SizeAware)]
    [InlineData(FinalWasmOptimization.Oz, WasmInlineAnnotationSelection.SizeAware,
        LoopInvariantFieldHoistingPolicy.Disabled, StaticDataViewEliminationPolicy.Disabled,
        AddressTakenValuePromotionPolicy.Disabled, ManagedMethodInliningPolicy.Disabled,
        BranchPrefixDuplicationPolicy.SizeAware)]
    public void PreservesPresetDefaults(
        FinalWasmOptimization preset,
        WasmInlineAnnotationSelection selection,
        LoopInvariantFieldHoistingPolicy fieldHoisting,
        StaticDataViewEliminationPolicy staticDataViews,
        AddressTakenValuePromotionPolicy valuePromotion,
        ManagedMethodInliningPolicy methodInlining,
        BranchPrefixDuplicationPolicy branchPrefixDuplication)
    {
        var resolver = Create();
        var expected = new WasmOptimizationSettings(
            preset,
            selection,
            selection,
            fieldHoisting,
            staticDataViews,
            valuePromotion,
            methodInlining,
            branchPrefixDuplication,
            preset == FinalWasmOptimization.O3
                ? ScalarCallPreservationPolicy.Conservative
                : ScalarCallPreservationPolicy.Disabled,
            preset == FinalWasmOptimization.O3
                ? ArrayBoundsCheckEliminationPolicy.Conservative
                : ArrayBoundsCheckEliminationPolicy.Disabled,
            preset == FinalWasmOptimization.O3 ? LoopUnrollingPolicy.Bounded : LoopUnrollingPolicy.Disabled,
            WholeProgramDefault(preset) ? ConstantArgumentPropagationPolicy.Conservative : ConstantArgumentPropagationPolicy.Disabled,
            WholeProgramDefault(preset) ? StaticArrayPopulationEliminationPolicy.Conservative : StaticArrayPopulationEliminationPolicy.Disabled);
        var actual = resolver.Resolve(preset);
        Assert.Equal(expected, actual);
        Assert.Equal(preset, actual.BackendPreset);
        Assert.Equal(selection, actual.ManagedInlineAnnotations);
        Assert.Equal(selection, actual.RuntimeInlineAnnotations);
        Assert.Equal(expected, resolver.Resolve(preset, new()));
    }

    [Theory]
    [InlineData(ManagedInliningHints.IgnoreAggressive, WasmInlineAnnotationSelection.SizeAware)]
    [InlineData(ManagedInliningHints.Preserve, WasmInlineAnnotationSelection.Preserve)]
    public void ExplicitHintsOverrideEveryEnabledPreset(
        ManagedInliningHints hint, WasmInlineAnnotationSelection selection)
    {
        var resolver = Create();
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>().Where(value => value != FinalWasmOptimization.None))
        {
            var runtimeSelection = preset is FinalWasmOptimization.O1 or
                FinalWasmOptimization.O2 or FinalWasmOptimization.O3
                ? WasmInlineAnnotationSelection.Preserve
                : WasmInlineAnnotationSelection.SizeAware;
            var fieldHoisting = preset is FinalWasmOptimization.O2 or
                FinalWasmOptimization.O3
                ? LoopInvariantFieldHoistingPolicy.Conservative
                : LoopInvariantFieldHoistingPolicy.Disabled;
            var staticDataViews = preset is FinalWasmOptimization.O2 or
                FinalWasmOptimization.O3
                ? StaticDataViewEliminationPolicy.Conservative
                : StaticDataViewEliminationPolicy.Disabled;
            var valuePromotion = preset == FinalWasmOptimization.O3
                ? AddressTakenValuePromotionPolicy.Conservative
                : AddressTakenValuePromotionPolicy.Disabled;
            var methodInlining = preset == FinalWasmOptimization.O3
                ? ManagedMethodInliningPolicy.Conservative
                : ManagedMethodInliningPolicy.Disabled;
            var branchPrefixDuplication = preset switch
            {
                FinalWasmOptimization.O0 =>
                    BranchPrefixDuplicationPolicy.Disabled,
                FinalWasmOptimization.O1 or FinalWasmOptimization.O2 or
                    FinalWasmOptimization.O3 =>
                    BranchPrefixDuplicationPolicy.Speed,
                _ => BranchPrefixDuplicationPolicy.SizeAware,
            };
            Assert.Equal(
                new(
                    preset,
                    selection,
                    runtimeSelection,
                    fieldHoisting,
                    staticDataViews,
                    valuePromotion,
                    methodInlining,
                    branchPrefixDuplication,
                    preset == FinalWasmOptimization.O3
                        ? ScalarCallPreservationPolicy.Conservative
                        : ScalarCallPreservationPolicy.Disabled,
                    preset == FinalWasmOptimization.O3
                        ? ArrayBoundsCheckEliminationPolicy.Conservative
                        : ArrayBoundsCheckEliminationPolicy.Disabled,
                    preset == FinalWasmOptimization.O3 ? LoopUnrollingPolicy.Bounded : LoopUnrollingPolicy.Disabled,
                    WholeProgramDefault(preset) ? ConstantArgumentPropagationPolicy.Conservative : ConstantArgumentPropagationPolicy.Disabled,
                    WholeProgramDefault(preset) ? StaticArrayPopulationEliminationPolicy.Conservative : StaticArrayPopulationEliminationPolicy.Disabled),
                resolver.Resolve(preset, new(hint)));
        }
    }

    [Theory]
    [InlineData(ManagedLoopInvariantFieldHoisting.Disabled,
        LoopInvariantFieldHoistingPolicy.Disabled)]
    [InlineData(ManagedLoopInvariantFieldHoisting.Enabled,
        LoopInvariantFieldHoistingPolicy.Conservative)]
    public void ExplicitFieldHoistingOverridesEveryEnabledPreset(
        ManagedLoopInvariantFieldHoisting fieldHoisting,
        LoopInvariantFieldHoistingPolicy expected)
    {
        var resolver = Create();
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>()
                     .Where(value => value != FinalWasmOptimization.None))
        {
            Assert.Equal(
                expected,
                resolver.Resolve(
                    preset,
                    new(LoopInvariantFieldHoisting: fieldHoisting))
                    .LoopInvariantFieldHoisting);
        }
    }

    [Fact]
    public void DisabledFieldHoistingIsAcceptedWithoutFinalOptimization()
    {
        var settings = Create().Resolve(
            FinalWasmOptimization.None,
            new(LoopInvariantFieldHoisting:
                ManagedLoopInvariantFieldHoisting.Disabled));

        Assert.Equal(
            LoopInvariantFieldHoistingPolicy.Disabled,
            settings.LoopInvariantFieldHoisting);
    }

    [Fact]
    public void EnabledFieldHoistingRequiresFinalOptimization()
    {
        var error = Assert.Throws<ArgumentException>(() => Create().Resolve(
            FinalWasmOptimization.None,
            new(LoopInvariantFieldHoisting:
                ManagedLoopInvariantFieldHoisting.Enabled)));

        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData(ManagedInliningHints.IgnoreAggressive)]
    [InlineData(ManagedInliningHints.Preserve)]
    public void RejectsIneffectiveOverridesWithNone(ManagedInliningHints hint)
    {
        var error = Assert.Throws<ArgumentException>(() => Create().Resolve(FinalWasmOptimization.None, new(hint)));
        Assert.Equal("options", error.ParamName);
    }

    [Fact]
    public void RejectsUnknownPreset()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve((FinalWasmOptimization)255));
        Assert.Equal("preset", error.ParamName);
    }

    [Theory]
    [InlineData(FinalWasmOptimization.None)]
    [InlineData(FinalWasmOptimization.O3)]
    public void RejectsUnknownOverride(FinalWasmOptimization preset)
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve(preset, new((ManagedInliningHints)255)));
        Assert.Equal("options", error.ParamName);
    }

    [Fact]
    public void RejectsUnknownFieldHoistingOverride()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve(
            FinalWasmOptimization.O3,
            new(LoopInvariantFieldHoisting:
                (ManagedLoopInvariantFieldHoisting)255)));

        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData(ManagedStaticDataViewElimination.Disabled,
        StaticDataViewEliminationPolicy.Disabled)]
    [InlineData(ManagedStaticDataViewElimination.Enabled,
        StaticDataViewEliminationPolicy.Conservative)]
    public void ExplicitStaticDataViewPolicyOverridesEveryEnabledPreset(
        ManagedStaticDataViewElimination option,
        StaticDataViewEliminationPolicy expected)
    {
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>()
                     .Where(value => value != FinalWasmOptimization.None))
        {
            Assert.Equal(
                expected,
                Create().Resolve(
                    preset,
                    new(StaticDataViewElimination: option))
                    .StaticDataViewElimination);
        }
    }

    [Fact]
    public void DisabledStaticDataViewEliminationIsAcceptedWithoutFinalOptimization()
    {
        var settings = Create().Resolve(
            FinalWasmOptimization.None,
            new(StaticDataViewElimination:
                ManagedStaticDataViewElimination.Disabled));

        Assert.Equal(
            StaticDataViewEliminationPolicy.Disabled,
            settings.StaticDataViewElimination);
    }

    [Fact]
    public void EnabledStaticDataViewEliminationRequiresFinalOptimization()
    {
        var error = Assert.Throws<ArgumentException>(() => Create().Resolve(
            FinalWasmOptimization.None,
            new(StaticDataViewElimination:
                ManagedStaticDataViewElimination.Enabled)));

        Assert.Equal("options", error.ParamName);
    }

    [Fact]
    public void RejectsUnknownStaticDataViewOverride()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve(
            FinalWasmOptimization.O3,
            new(StaticDataViewElimination:
                (ManagedStaticDataViewElimination)255)));

        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData(ManagedAddressTakenValuePromotion.Disabled,
        AddressTakenValuePromotionPolicy.Disabled)]
    [InlineData(ManagedAddressTakenValuePromotion.Enabled,
        AddressTakenValuePromotionPolicy.Conservative)]
    public void ExplicitAddressTakenValuePromotionOverridesEveryEnabledPreset(
        ManagedAddressTakenValuePromotion option,
        AddressTakenValuePromotionPolicy expected)
    {
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>()
                     .Where(value => value != FinalWasmOptimization.None))
        {
            Assert.Equal(
                expected,
                Create().Resolve(
                    preset,
                    new(AddressTakenValuePromotion: option))
                    .AddressTakenValuePromotion);
        }
    }

    [Fact]
    public void DisabledAddressTakenValuePromotionIsAcceptedWithoutFinalOptimization()
    {
        var settings = Create().Resolve(
            FinalWasmOptimization.None,
            new(AddressTakenValuePromotion:
                ManagedAddressTakenValuePromotion.Disabled));

        Assert.Equal(
            AddressTakenValuePromotionPolicy.Disabled,
            settings.AddressTakenValuePromotion);
    }

    [Fact]
    public void EnabledAddressTakenValuePromotionRequiresFinalOptimization()
    {
        var error = Assert.Throws<ArgumentException>(() => Create().Resolve(
            FinalWasmOptimization.None,
            new(AddressTakenValuePromotion:
                ManagedAddressTakenValuePromotion.Enabled)));

        Assert.Equal("options", error.ParamName);
    }

    [Fact]
    public void RejectsUnknownAddressTakenValuePromotionOverride()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve(
            FinalWasmOptimization.O3,
            new(AddressTakenValuePromotion:
                (ManagedAddressTakenValuePromotion)255)));

        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData(ManagedMethodInlining.Disabled, ManagedMethodInliningPolicy.Disabled)]
    [InlineData(ManagedMethodInlining.Enabled, ManagedMethodInliningPolicy.Conservative)]
    public void ExplicitManagedMethodInliningOverridesEveryEnabledPreset(
        ManagedMethodInlining option,
        ManagedMethodInliningPolicy expected)
    {
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>()
                     .Where(value => value != FinalWasmOptimization.None))
        {
            Assert.Equal(
                expected,
                Create().Resolve(
                    preset,
                    new(ManagedMethodInlining: option))
                    .ManagedMethodInlining);
        }
    }

    [Fact]
    public void DisabledManagedMethodInliningIsAcceptedWithoutFinalOptimization()
    {
        var settings = Create().Resolve(
            FinalWasmOptimization.None,
            new(ManagedMethodInlining: ManagedMethodInlining.Disabled));

        Assert.Equal(
            ManagedMethodInliningPolicy.Disabled,
            settings.ManagedMethodInlining);
    }

    [Fact]
    public void EnabledManagedMethodInliningRequiresFinalOptimization()
    {
        var error = Assert.Throws<ArgumentException>(() => Create().Resolve(
            FinalWasmOptimization.None,
            new(ManagedMethodInlining: ManagedMethodInlining.Enabled)));

        Assert.Equal("options", error.ParamName);
    }

    [Fact]
    public void RejectsUnknownManagedMethodInliningOverride()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve(
            FinalWasmOptimization.O3,
            new(ManagedMethodInlining: (ManagedMethodInlining)255)));

        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData(ManagedBranchPrefixDuplication.Disabled,
        BranchPrefixDuplicationPolicy.Disabled)]
    [InlineData(ManagedBranchPrefixDuplication.Enabled,
        BranchPrefixDuplicationPolicy.Speed)]
    public void ExplicitBranchPrefixDuplicationOverridesEveryEnabledPreset(
        ManagedBranchPrefixDuplication option,
        BranchPrefixDuplicationPolicy expected)
    {
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>()
                     .Where(value => value != FinalWasmOptimization.None))
        {
            Assert.Equal(
                expected,
                Create().Resolve(
                    preset,
                    new(BranchPrefixDuplication: option))
                    .BranchPrefixDuplication);
        }
    }

    [Fact]
    public void DisabledBranchPrefixDuplicationIsAcceptedWithoutFinalOptimization()
    {
        var settings = Create().Resolve(
            FinalWasmOptimization.None,
            new(BranchPrefixDuplication:
                ManagedBranchPrefixDuplication.Disabled));

        Assert.Equal(
            BranchPrefixDuplicationPolicy.Disabled,
            settings.BranchPrefixDuplication);
    }

    [Fact]
    public void EnabledBranchPrefixDuplicationRequiresFinalOptimization()
    {
        var error = Assert.Throws<ArgumentException>(() => Create().Resolve(
            FinalWasmOptimization.None,
            new(BranchPrefixDuplication:
                ManagedBranchPrefixDuplication.Enabled)));

        Assert.Equal("options", error.ParamName);
    }

    [Fact]
    public void RejectsUnknownBranchPrefixDuplicationOverride()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve(
            FinalWasmOptimization.O3,
            new(BranchPrefixDuplication:
                (ManagedBranchPrefixDuplication)255)));

        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData(ManagedScalarCallPreservation.Disabled, ScalarCallPreservationPolicy.Disabled)]
    [InlineData(ManagedScalarCallPreservation.Enabled, ScalarCallPreservationPolicy.Conservative)]
    public void ExplicitScalarCallPreservationOverridesEveryEnabledPreset(
        ManagedScalarCallPreservation option,
        ScalarCallPreservationPolicy expected)
    {
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>()
                     .Where(value => value != FinalWasmOptimization.None))
        {
            Assert.Equal(expected, Create().Resolve(preset,
                new(ScalarCallPreservation: option)).ScalarCallPreservation);
        }
    }

    [Fact]
    public void DisabledScalarCallPreservationIsAcceptedWithoutFinalOptimization()
    {
        Assert.Equal(ScalarCallPreservationPolicy.Disabled,
            Create().Resolve(FinalWasmOptimization.None,
                new(ScalarCallPreservation: ManagedScalarCallPreservation.Disabled))
                .ScalarCallPreservation);
    }

    [Fact]
    public void EnabledScalarCallPreservationRequiresFinalOptimization()
    {
        var error = Assert.Throws<ArgumentException>(() => Create().Resolve(
            FinalWasmOptimization.None,
            new(ScalarCallPreservation: ManagedScalarCallPreservation.Enabled)));
        Assert.Equal("options", error.ParamName);
    }

    [Fact]
    public void RejectsUnknownScalarCallPreservationOverride()
    {
        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve(
            FinalWasmOptimization.O3,
            new(ScalarCallPreservation: (ManagedScalarCallPreservation)255)));
        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData(ManagedArrayBoundsCheckElimination.Disabled, ArrayBoundsCheckEliminationPolicy.Disabled)]
    [InlineData(ManagedArrayBoundsCheckElimination.Enabled, ArrayBoundsCheckEliminationPolicy.Conservative)]
    public void ExplicitArrayBoundsSelectionOverridesEachEnabledPreset(
        ManagedArrayBoundsCheckElimination option, ArrayBoundsCheckEliminationPolicy expected)
    {
        foreach (var preset in Enum.GetValues<FinalWasmOptimization>()
                     .Where(value => value != FinalWasmOptimization.None))
        {
            Assert.Equal(expected, Create().Resolve(preset,
                new(ArrayBoundsCheckElimination: option)).ArrayBoundsCheckElimination);
        }
    }

    [Fact]
    public void DisabledArrayOptimizationsAreAcceptedWithoutFinalOptimization()
    {
        var settings = Create().Resolve(FinalWasmOptimization.None,
            new(ArrayBoundsCheckElimination: ManagedArrayBoundsCheckElimination.Disabled,
                LoopUnrolling: ManagedLoopUnrolling.Disabled));

        Assert.Equal(ArrayBoundsCheckEliminationPolicy.Disabled, settings.ArrayBoundsCheckElimination);
        Assert.Equal(LoopUnrollingPolicy.Disabled, settings.LoopUnrolling);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EnabledArrayOptimizationsRequireFinalOptimization(bool unrolling)
    {
        var options = unrolling
            ? new WasmOptimizationOverrides(LoopUnrolling: ManagedLoopUnrolling.Enabled)
            : new WasmOptimizationOverrides(ArrayBoundsCheckElimination: ManagedArrayBoundsCheckElimination.Enabled);

        var error = Assert.Throws<ArgumentException>(() => Create().Resolve(FinalWasmOptimization.None, options));

        Assert.Equal("options", error.ParamName);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsUnknownArrayOptimizationSelections(bool unrolling)
    {
        var options = unrolling
            ? new WasmOptimizationOverrides(LoopUnrolling: (ManagedLoopUnrolling)255)
            : new WasmOptimizationOverrides(ArrayBoundsCheckElimination: (ManagedArrayBoundsCheckElimination)255);

        var error = Assert.Throws<ArgumentOutOfRangeException>(() => Create().Resolve(FinalWasmOptimization.O3, options));

        Assert.Equal("options", error.ParamName);
    }

    private static IWasmOptimizationSettingsResolver Create() =>
        Assert.IsAssignableFrom<IWasmOptimizationSettingsResolver>(new WasmOptimizationSettingsResolver());
}
