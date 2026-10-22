using System;
using System.Linq;
using NetWasm.Compiler.ComponentModel.Worlds;
using NetWasm.Compiler.Core.ManagedExecutables;

namespace NetWasm.Compiler.ComponentModel;

public interface IComponentBuilder
{
    ComponentManifest Build(ComponentBuildRequest request);
}

public sealed class ComponentBuilder(
    IWitDocumentReader documents,
    IWitWorldValidator worlds,
    IComponentManifestBuilder manifests,
    IWitWorldSpecifierFormatter specifiers,
    IComponentPackager components) : IComponentBuilder
{
    private readonly IWitDocumentReader _documents = documents ??
        throw new ArgumentNullException(nameof(documents));
    private readonly IWitWorldValidator _worlds = worlds ??
        throw new ArgumentNullException(nameof(worlds));
    private readonly IComponentManifestBuilder _manifests = manifests ??
        throw new ArgumentNullException(nameof(manifests));
    private readonly IWitWorldSpecifierFormatter _specifiers = specifiers ??
        throw new ArgumentNullException(nameof(specifiers));
    private readonly IComponentPackager _components = components ??
        throw new ArgumentNullException(nameof(components));

    public ComponentManifest Build(ComponentBuildRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var document = _documents.Read(request.WitPath);
        var world = document.SelectWorld(request.World);
        _worlds.Validate(document, world);
        var manifest = _manifests.Build(
            document,
            world,
            request.Target,
            request.ManifestInputs);
        _components.Package(new(
            request.CoreModulePath,
            request.WitPath,
            _specifiers.Format(world),
            request.OutputPath,
            request.Target,
            request.RuntimeModulePath,
            SelectEntryPoint(request.ManagedExecutableEntryPoint, document, world),
            request.Optimization,
            request.StructuredDiagnostics)
        { OptimizationOverrides = request.OptimizationOverrides });
        return manifest;
    }

    // Only a world that exports netwasm:runtime/process lets a host observe an asynchronous Main.
    // In any other world, such as wasi:cli/command, the compiler enters an asynchronous Main
    // synchronously through AsyncEntryPoint.Run, which runs its event loop to completion, so the
    // component links it like a synchronous entry point.
    private static ManagedExecutableEntryPointAbi? SelectEntryPoint(
        ManagedExecutableEntryPointAbi? entryPoint,
        WitDocument document,
        WitWorld world) =>
        entryPoint is { CompletionShape: ManagedExecutableCompletionShape.Asynchronous } &&
        !world.Exports.Any(export => export.InterfaceId is { } id &&
            document.Interfaces[id] is { Name: "process" } exported &&
            (exported.Package == "netwasm:runtime" ||
             exported.Package.StartsWith("netwasm:runtime@", StringComparison.Ordinal)))
            ? entryPoint with { CompletionShape = ManagedExecutableCompletionShape.Synchronous }
            : entryPoint;
}
