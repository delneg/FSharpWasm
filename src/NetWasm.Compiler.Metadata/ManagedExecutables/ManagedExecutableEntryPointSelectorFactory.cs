namespace NetWasm.Compiler.Metadata.ManagedExecutables;

public sealed class ManagedExecutableEntryPointSelectorFactory : IManagedExecutableEntryPointSelectorFactory
{
    public IManagedExecutableEntryPointSelector Create() =>
        new ManagedExecutableEntryPointSelector(new RawCilInstructionReader());
}
