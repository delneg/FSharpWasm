using System;

namespace NetWasm.Runtime.Pack.Materialization;

internal interface IRuntimeArtifactWriter
{
    void Write(string path, ReadOnlyMemory<byte> bytes);
}
