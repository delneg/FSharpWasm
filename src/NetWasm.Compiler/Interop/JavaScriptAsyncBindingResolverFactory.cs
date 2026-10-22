using System;
using NetWasm.Compiler.Analysis;
using NetWasm.Compiler.Core;
using NetWasm.Compiler.Metadata;

namespace NetWasm.Compiler.Interop;

internal interface IJavaScriptAsyncBindingResolverFactory
{
    IJavaScriptAsyncBindingResolver Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IFieldRepository fields,
        IMethodRepository methodRepository,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols,
        PlatformWorldCapabilities world);
}

internal sealed class JavaScriptAsyncBindingResolverFactory :
    IJavaScriptAsyncBindingResolverFactory
{
    public IJavaScriptAsyncBindingResolver Create(
        ITypeFinder types,
        ITypeDefinitionResolver typeDefinitions,
        ITypeIdentityResolver identities,
        IFieldRepository fields,
        IMethodRepository methodRepository,
        IMethodInstanceResolver methodInstances,
        ISymbolFormatter symbols,
        PlatformWorldCapabilities world)
    {
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(typeDefinitions);
        ArgumentNullException.ThrowIfNull(identities);
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentNullException.ThrowIfNull(methodRepository);
        ArgumentNullException.ThrowIfNull(methodInstances);
        ArgumentNullException.ThrowIfNull(symbols);
        ArgumentNullException.ThrowIfNull(world);
        return new JavaScriptAsyncBindingResolver(
            types,
            typeDefinitions,
            identities,
            fields,
            methodRepository,
            methodInstances,
            symbols,
            world);
    }
}
