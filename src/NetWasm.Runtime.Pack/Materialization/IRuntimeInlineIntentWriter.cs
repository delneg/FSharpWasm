using System;
using System.Collections.Immutable;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeInlineIntentWriter
{
    byte[] Write(
        ReadOnlyMemory<byte> module,
        ImmutableArray<RuntimeFunctionInlineIntent> intents);
}
