using System;
using System.Collections.Immutable;
using System.Linq;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Core.Types;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Analysis;

internal sealed class DefaultComparerCallRewriter(
    ITypeDefinitionResolver typeDefinitions,
    IMethodRepository methods,
    IMethodInstanceResolver methodInstances,
    ITypeFinder types,
    ICalledMethodResolver calledMethods,
    ISymbolFormatter symbols,
    ITypeRelationshipClassifier relationships) : IMethodRewriteRule
{
    private readonly NullableTypeResolver _nullableTypes = new();

    public CilMethodBody Rewrite(CilMethodBody body)
    {
        var instructions = body.Instructions.ToArray();
        var changed = false;
        for (var index = 0; index < instructions.Length; index++)
        {
            var call = instructions[index];
            if (call.Operation != CilOperation.Call)
            {
                continue;
            }

            var target = calledMethods.Resolve(call);
            if (target is null || target.Definition.Name != "CreateDefaultComparer" ||
                target.DeclaringType.Shape != CliTypeShape.GenericInstantiation ||
                target.DeclaringType.TypeArguments is not [var comparedType])
            {
                continue;
            }

            var owner = symbols.Format(target.Definition.DeclaringType);
            var selection = Select(owner, comparedType);
            if (selection is null)
            {
                continue;
            }

            var comparerDefinition = types.FindType(selection.ComparerName);
            var comparerArguments = selection.TypeArguments;
            var comparerType = comparerArguments.IsEmpty
                ? CliTypeIdentity.FromDefinition(comparerDefinition)
                : CliTypeIdentity.GenericInstantiation(
                    CliTypeIdentity.FromDefinition(comparerDefinition),
                    comparerArguments);
            var constructor = comparerDefinition.Methods
                .Select(methods.GetMethod)
                .Single(method => method.Name == ".ctor" &&
                    !method.IsStatic &&
                    method.Signature.ParameterTypes.IsEmpty);
            var comparerConstructor = methodInstances.ResolveMethodInstance(
                constructor.Key.Assembly,
                constructor.Key.MetadataToken,
                symbols.Format(constructor),
                0,
                new CliGenericContext(comparerArguments, []));
            instructions[index] = call with
            {
                Operation = CilOperation.NewObject,
                Operand = new CilOperand.MethodInstance(comparerConstructor with
                {
                    DeclaringType = comparerType,
                }),
            };
            changed = true;
        }
        return changed ? body with { Instructions = [.. instructions] } : body;
    }

    private Selection? Select(string owner, CliTypeIdentity comparedType)
    {
        if (comparedType.ContainsGenericParameters)
        {
            return null;
        }

        var nullableType = _nullableTypes.Resolve(comparedType);
        var isEnum = IsEnum(comparedType);
        return owner switch
        {
            "System.Collections.Generic.EqualityComparer`1" when
                comparedType.CanonicalName == "primitive:string" =>
                NonGeneric("System.Collections.Generic.StringEqualityComparer"),
            "System.Collections.Generic.EqualityComparer`1" when nullableType is not null =>
                Generic("System.Collections.Generic.NullableEqualityComparer`1", nullableType),
            "System.Collections.Generic.EqualityComparer`1" when isEnum =>
                Generic("System.Collections.Generic.EnumEqualityComparer`1", comparedType),
            "System.Collections.Generic.EqualityComparer`1" when
                Implements(comparedType, "System.IEquatable`1") =>
                Generic("System.Collections.Generic.GenericEqualityComparer`1", comparedType),
            "System.Collections.Generic.EqualityComparer`1" =>
                Generic("System.Collections.Generic.ObjectEqualityComparer`1", comparedType),
            "System.Collections.Generic.Comparer`1" when nullableType is not null =>
                Generic("System.Collections.Generic.NullableComparer`1", nullableType),
            "System.Collections.Generic.Comparer`1" when isEnum =>
                Generic("System.Collections.Generic.EnumComparer`1", comparedType),
            "System.Collections.Generic.Comparer`1" when
                Implements(comparedType, "System.IComparable`1") =>
                Generic("System.Collections.Generic.GenericComparer`1", comparedType),
            "System.Collections.Generic.Comparer`1" =>
                Generic("System.Collections.Generic.ObjectComparer`1", comparedType),
            _ => null,
        };
    }

    private bool IsEnum(CliTypeIdentity type) =>
        type.IsValueType &&
        type.Shape is CliTypeShape.Named or CliTypeShape.GenericInstantiation &&
        typeDefinitions.ResolveTypeIdentity(type).IsEnum;

    private bool Implements(CliTypeIdentity comparedType, string interfaceName)
    {
        var interfaceDefinition = types.FindType(interfaceName);
        var interfaceType = CliTypeIdentity.GenericInstantiation(
            CliTypeIdentity.FromDefinition(interfaceDefinition),
            [comparedType]);
        return relationships.Classify(comparedType, interfaceType).IsAssignmentCompatible;
    }

    private static Selection NonGeneric(string comparerName) =>
        new(comparerName, []);

    private static Selection Generic(string comparerName, CliTypeIdentity argument) =>
        new(comparerName, [argument]);

    private sealed record Selection(
        string ComparerName,
        ImmutableArray<CliTypeIdentity> TypeArguments);
}
