using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using NetWasm.Compiler.ComponentModel.Binary;

namespace NetWasm.Compiler.ComponentModel;

public interface IWasmCoreModuleExportEditor
{
    void Rewrite(string inputPath, string outputPath, WasmExportSelection selection);
}

public sealed record WasmInternalExport(string Name, byte Kind);

public sealed record WasmExportSelection(string? ComponentPrefix, ImmutableArray<WasmInternalExport> RemovedExports);

public sealed class WasmCoreModuleExportEditor(
    IFileExistence files,
    IByteFileReader reader,
    IByteFileWriter writer) : IWasmCoreModuleExportEditor
{
    private readonly IFileExistence _files = files ??
        throw new ArgumentNullException(nameof(files));
    private readonly IByteFileReader _reader = reader ??
        throw new ArgumentNullException(nameof(reader));
    private readonly IByteFileWriter _writer = writer ??
        throw new ArgumentNullException(nameof(writer));

    public void Rewrite(string inputPath, string outputPath, WasmExportSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);
        if (selection.RemovedExports.IsDefault)
            throw ComponentException.Invalid("core module export selection is uninitialized");
        if (selection.ComponentPrefix is { } prefix)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(prefix);
            if (!selection.RemovedExports.IsEmpty)
                throw ComponentException.Invalid("core module export selection combines incompatible policies");
        }
        var removed = new Dictionary<string, byte>(StringComparer.Ordinal);
        foreach (var export in selection.RemovedExports)
        {
            if (export is null || string.IsNullOrWhiteSpace(export.Name) || export.Kind > 4 ||
                !removed.TryAdd(export.Name, export.Kind))
                throw ComponentException.Invalid("core module internal export selection is invalid");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        RequireFile(inputPath);

        var module = _reader.Read(inputPath);
        var reader = new WasmBinaryReader(module);
        reader.RequireHeader();
        using var output = new MemoryStream(module.Length);
        output.Write(WasmBinaryFormat.Header);
        var memoryExports = 0;
        var matched = new HashSet<string>(StringComparer.Ordinal);
        while (!reader.IsAtEnd)
        {
            var sectionStart = reader.Offset;
            var sectionId = reader.ReadByte();
            var sectionSize = reader.ReadUnsigned();
            var section = reader.ReadBytes(sectionSize);
            if (sectionId != WasmBinaryFormat.ExportSection)
            {
                output.Write(module.AsSpan(sectionStart, reader.Offset - sectionStart));
                continue;
            }

            var rewritten = RewriteExportSection(section, selection.ComponentPrefix, removed, matched, ref memoryExports);
            output.WriteByte(sectionId);
            WasmUnsignedLeb128.Write(output, (uint)rewritten.Length);
            output.Write(rewritten);
        }

        if (selection.ComponentPrefix is not null && memoryExports != 1)
        {
            throw ComponentException.Invalid(
                $"core module must export exactly one memory named '{selection.ComponentPrefix}_memory'");
        }
        if (matched.Count != removed.Count)
            throw ComponentException.Invalid("core module is missing a required internal export");
        _writer.Write(outputPath, output.ToArray());
    }

    private static byte[] RewriteExportSection(
        ReadOnlySpan<byte> section,
        string? prefix,
        Dictionary<string, byte> removed,
        HashSet<string> matched,
        ref int memoryExports)
    {
        var reader = new WasmBinaryReader(section);
        var count = reader.ReadUnsigned();
        var retained = new List<byte[]>((int)count);
        for (var index = 0u; index < count; index++)
        {
            var start = reader.Offset;
            var name = reader.ReadString();
            var kind = reader.ReadByte();
            reader.ReadUnsigned();
            var entry = section[start..reader.Offset].ToArray();
            if (prefix is not null && string.Equals(name, prefix + "_memory", StringComparison.Ordinal) &&
                kind == WasmBinaryFormat.MemoryExternalKind)
            {
                memoryExports++;
            }
            if (removed.TryGetValue(name, out var expectedKind))
            {
                if (kind != expectedKind || !matched.Add(name))
                    throw ComponentException.Invalid("core module internal export has a conflicting kind or identity");
            }
            else if (prefix is null || IsComponentExport(name, prefix))
            {
                retained.Add(entry);
            }
        }
        reader.RequireEnd("export section");

        using var output = new MemoryStream(section.Length);
        WasmUnsignedLeb128.Write(output, (uint)retained.Count);
        foreach (var entry in retained)
        {
            output.Write(entry);
        }
        return output.ToArray();
    }

    private static bool IsComponentExport(string name, string prefix) =>
        name.StartsWith(prefix + "_", StringComparison.Ordinal) ||
        name.StartsWith(prefix + "|", StringComparison.Ordinal);

    private void RequireFile(string path)
    {
        if (!_files.Exists(path))
        {
            throw ComponentException.Invalid($"core module '{path}' does not exist");
        }
    }

}
