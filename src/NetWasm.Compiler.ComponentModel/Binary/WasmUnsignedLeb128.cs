using System.IO;

namespace NetWasm.Compiler.ComponentModel.Binary;

internal static class WasmUnsignedLeb128
{
    public static void Write(Stream output, uint value)
    {
        do
        {
            var next = (byte)(value & 0x7f);
            value >>= 7;
            if (value != 0)
            {
                next |= 0x80;
            }
            output.WriteByte(next);
        }
        while (value != 0);
    }
}
