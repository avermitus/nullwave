using System.IO.Compression;
using System.Text;

namespace Nullwave.Util;

public static class PngWriter
{
    public static void Write(string path, int width, int height, byte[] rgba, bool flipY = true)
    {
        if (rgba.Length < width * height * 4)
            throw new ArgumentException("RGBA buffer is too small");
        string? dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

        using var fs = File.Create(path);
        fs.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        byte[] ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)width);
        WriteBE(ihdr, 4, (uint)height);
        ihdr[8] = 8;
        ihdr[9] = 6;
        ihdr[10] = 0;
        ihdr[11] = 0;
        ihdr[12] = 0;
        WriteChunk(fs, "IHDR", ihdr);

        using var raw = new MemoryStream();
        for (int y = 0; y < height; y++)
        {
            raw.WriteByte(0);
            int srcY = flipY ? height - 1 - y : y;
            raw.Write(rgba, srcY * width * 4, width * 4);
        }

        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.Fastest, true))
        {
            raw.Position = 0;
            raw.CopyTo(z);
        }
        WriteChunk(fs, "IDAT", compressed.ToArray());
        WriteChunk(fs, "IEND", []);
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        byte[] length = new byte[4];
        WriteBE(length, 0, (uint)data.Length);
        stream.Write(length);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        uint crc = Crc32(typeBytes, data);
        byte[] crcBytes = new byte[4];
        WriteBE(crcBytes, 0, crc);
        stream.Write(crcBytes);
    }

    private static void WriteBE(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value >> 24);
        buf[offset + 1] = (byte)(value >> 16);
        buf[offset + 2] = (byte)(value >> 8);
        buf[offset + 3] = (byte)value;
    }

    private static uint Crc32(byte[] a, byte[] b)
    {
        uint crc = 0xFFFFFFFFu;
        for (int i = 0; i < a.Length; i++) crc = Step(crc, a[i]);
        for (int i = 0; i < b.Length; i++) crc = Step(crc, b[i]);
        return crc ^ 0xFFFFFFFFu;
    }

    private static uint Step(uint crc, byte b)
    {
        crc ^= b;
        for (int k = 0; k < 8; k++)
            crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        return crc;
    }
}
