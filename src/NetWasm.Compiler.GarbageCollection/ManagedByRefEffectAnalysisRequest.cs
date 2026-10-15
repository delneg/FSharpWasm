using System.Collections.Generic;
using NetWasm.Compiler.ControlFlow;
using NetWasm.Compiler.Core;

namespace NetWasm.Compiler.GarbageCollection;

public sealed record ManagedByRefEffectAnalysisRequest(
    IReadOnlyDictionary<EntityKey, ManagedMethodBody> Methods,
    IReadOnlyDictionary<string, ManagedMethodBody> ConstructedMethods,
    IReadOnlyDictionary<string, DispatchCallSiteModel> DispatchCallSites);
