namespace NetWasm.Compiler.Core.Types;

public interface IMethodInitializationTriggerClassifier
{
    bool Classify(MethodDefinitionModel method);
}
