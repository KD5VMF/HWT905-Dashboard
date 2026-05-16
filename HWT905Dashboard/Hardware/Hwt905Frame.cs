using System;
using System.Text;

namespace HWT905Dashboard.Hardware;

public sealed class Hwt905Frame
{
    public Hwt905Frame(byte[] bytes)
    {
        Bytes = bytes;
        Type = bytes[1];
        S0 = ToInt16(bytes[2], bytes[3]);
        S1 = ToInt16(bytes[4], bytes[5]);
        S2 = ToInt16(bytes[6], bytes[7]);
        S3 = ToInt16(bytes[8], bytes[9]);
    }

    public byte[] Bytes { get; }
    public int Type { get; }
    public short S0 { get; }
    public short S1 { get; }
    public short S2 { get; }
    public short S3 { get; }

    public string ToAsciiOrHex()
    {
        try
        {
            var text = Encoding.ASCII.GetString(Bytes, 2, 8).Trim('\0', ' ', '\r', '\n');
            bool printable = true;
            foreach (var ch in text)
            {
                if (ch < 32 || ch > 126)
                {
                    printable = false;
                    break;
                }
            }
            if (printable && !string.IsNullOrWhiteSpace(text))
                return text;
        }
        catch { }
        return BitConverter.ToString(Bytes).Replace('-', ' ');
    }

    private static short ToInt16(byte low, byte high)
    {
        return unchecked((short)(low | (high << 8)));
    }
}
