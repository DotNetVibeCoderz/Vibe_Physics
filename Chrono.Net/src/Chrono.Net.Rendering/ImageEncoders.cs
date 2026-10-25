using System.Buffers.Binary;
using System.IO.Compression;

namespace Chrono.Net.Rendering;

/// <summary>Dependency-free PNG writer for <see cref="FrameBuffer"/> images.</summary>
public static class PngEncoder
{
    public static void Save(FrameBuffer fb, string path)
    {
        using var fs = File.Create(path);
        Write(fb, fs);
    }

    public static byte[] Encode(FrameBuffer fb)
    {
        using var ms = new MemoryStream();
        Write(fb, ms);
        return ms.ToArray();
    }

    public static void Write(FrameBuffer fb, Stream output)
    {
        output.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, fb.Width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr[4..], fb.Height);
        ihdr[8] = 8;  // bit depth
        ihdr[9] = 2;  // RGB
        WriteChunk(output, "IHDR", ihdr);

        using var raw = new MemoryStream();
        using (var z = new ZLibStream(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + fb.Width * 3];
            for (int y = 0; y < fb.Height; y++)
            {
                row[0] = 0;
                for (int x = 0; x < fb.Width; x++)
                {
                    uint p = fb.Pixels[y * fb.Width + x];
                    row[1 + x * 3] = (byte)(p >> 16);
                    row[2 + x * 3] = (byte)(p >> 8);
                    row[3 + x * 3] = (byte)p;
                }
                z.Write(row);
            }
        }
        WriteChunk(output, "IDAT", raw.ToArray());
        WriteChunk(output, "IEND", []);
    }

    private static void WriteChunk(Stream s, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> header = stackalloc byte[8];
        BinaryPrimitives.WriteInt32BigEndian(header, data.Length);
        for (int i = 0; i < 4; i++) header[4 + i] = (byte)type[i];
        s.Write(header);
        s.Write(data);
        uint crc = Crc32(header[4..], 0xFFFFFFFFu);
        crc = Crc32(data, crc) ^ 0xFFFFFFFFu;
        Span<byte> c = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(c, crc);
        s.Write(c);
    }

    private static readonly uint[] CrcTable = BuildCrcTable();

    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint n = 0; n < 256; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[n] = c;
        }
        return t;
    }

    private static uint Crc32(ReadOnlySpan<byte> data, uint crc)
    {
        foreach (byte b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }
}

/// <summary>Animated GIF writer (fixed 6×7×6 color cube + ordered dithering, LZW compressed).</summary>
public sealed class GifEncoder : IDisposable
{
    private readonly Stream _stream;
    private readonly int _width, _height;
    private bool _headerWritten;

    public GifEncoder(Stream stream, int width, int height)
    {
        _stream = stream;
        _width = width;
        _height = height;
    }

    public static GifEncoder Create(string path, int width, int height) => new(File.Create(path), width, height);

    /// <summary>Adds a frame; <paramref name="delayCs"/> is the frame duration in hundredths of a second.</summary>
    public void AddFrame(FrameBuffer fb, int delayCs = 4)
    {
        if (fb.Width != _width || fb.Height != _height) throw new ArgumentException("Frame size mismatch.");
        if (!_headerWritten) WriteHeader();

        // Graphic control extension
        _stream.Write([0x21, 0xF9, 0x04, 0x00, (byte)(delayCs & 255), (byte)(delayCs >> 8), 0x00, 0x00]);
        // Image descriptor
        Span<byte> desc = stackalloc byte[10];
        desc[0] = 0x2C;
        BinaryPrimitives.WriteUInt16LittleEndian(desc[5..], (ushort)_width);
        BinaryPrimitives.WriteUInt16LittleEndian(desc[7..], (ushort)_height);
        _stream.Write(desc);

        var indices = new byte[_width * _height];
        ReadOnlySpan<int> bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];
        for (int y = 0; y < _height; y++)
        for (int x = 0; x < _width; x++)
        {
            uint p = fb.Pixels[y * _width + x];
            float d = (bayer[(y & 3) * 4 + (x & 3)] / 16f - 0.5f);
            int r = Quant((p >> 16) & 255, 5, d), g = Quant((p >> 8) & 255, 6, d), b = Quant(p & 255, 5, d);
            indices[y * _width + x] = (byte)(r * 42 + g * 6 + b);
        }
        LzwEncode(indices, 8);
    }

    private static int Quant(uint v, int levels, float dither) =>
        Math.Clamp((int)MathF.Round(v / 255f * levels + dither), 0, levels);

    private void WriteHeader()
    {
        _headerWritten = true;
        _stream.Write("GIF89a"u8);
        Span<byte> lsd = stackalloc byte[7];
        BinaryPrimitives.WriteUInt16LittleEndian(lsd, (ushort)_width);
        BinaryPrimitives.WriteUInt16LittleEndian(lsd[2..], (ushort)_height);
        lsd[4] = 0xF7; // global color table, 8 bits, 256 entries
        _stream.Write(lsd);
        var palette = new byte[256 * 3];
        for (int r = 0; r < 6; r++)
        for (int g = 0; g < 7; g++)
        for (int b = 0; b < 6; b++)
        {
            int i = (r * 42 + g * 6 + b) * 3;
            palette[i] = (byte)(r * 51); palette[i + 1] = (byte)(g * 255 / 6); palette[i + 2] = (byte)(b * 51);
        }
        _stream.Write(palette);
        // Loop forever (NETSCAPE2.0)
        _stream.Write([0x21, 0xFF, 0x0B, .. "NETSCAPE2.0"u8, 0x03, 0x01, 0x00, 0x00, 0x00]);
    }

    private void LzwEncode(byte[] data, int minCodeSize)
    {
        _stream.WriteByte((byte)minCodeSize);
        int clear = 1 << minCodeSize, eoi = clear + 1;
        var dict = new Dictionary<int, int>(4096);
        int codeSize = minCodeSize + 1, next = eoi + 1;
        var block = new byte[255];
        int blockLen = 0, bitBuf = 0, bitCount = 0;

        void Emit(int code)
        {
            bitBuf |= code << bitCount;
            bitCount += codeSize;
            while (bitCount >= 8)
            {
                block[blockLen++] = (byte)bitBuf;
                bitBuf >>= 8;
                bitCount -= 8;
                if (blockLen == 255) { _stream.WriteByte(255); _stream.Write(block); blockLen = 0; }
            }
        }

        Emit(clear);
        int prefix = data[0];
        for (int i = 1; i < data.Length; i++)
        {
            int k = data[i];
            int key = (prefix << 8) | k;
            if (dict.TryGetValue(key, out int code)) { prefix = code; continue; }
            Emit(prefix);
            if (next < 4096)
            {
                dict[key] = next++;
                if (next > (1 << codeSize) && codeSize < 12) codeSize++;
            }
            else
            {
                Emit(clear);
                dict.Clear();
                codeSize = minCodeSize + 1;
                next = eoi + 1;
            }
            prefix = k;
        }
        Emit(prefix);
        Emit(eoi);
        if (bitCount > 0) { block[blockLen++] = (byte)bitBuf; if (blockLen == 255) { _stream.WriteByte(255); _stream.Write(block); blockLen = 0; } }
        if (blockLen > 0) { _stream.WriteByte((byte)blockLen); _stream.Write(block, 0, blockLen); }
        _stream.WriteByte(0);
    }

    public void Dispose()
    {
        if (_headerWritten) _stream.WriteByte(0x3B);
        _stream.Dispose();
    }
}

/// <summary>One-call helpers to render a <see cref="RenderWorld"/> to image files.</summary>
public static class Snapshot
{
    /// <summary>Renders with 2× supersampling and saves a PNG.</summary>
    public static void SavePng(RenderWorld world, OrbitCamera camera, string path, int width = 1280, int height = 720,
        RenderOptions? options = null, int supersample = 2)
    {
        var fb = RenderFrame(world, camera, width, height, options, supersample);
        PngEncoder.Save(fb, path);
    }

    public static FrameBuffer RenderFrame(RenderWorld world, OrbitCamera camera, int width, int height,
        RenderOptions? options = null, int supersample = 2)
    {
        var renderer = new SoftwareRenderer { Options = options ?? new RenderOptions() };
        var fb = new FrameBuffer(width * supersample, height * supersample);
        world.Update();
        renderer.Render(world, camera, fb);
        return supersample > 1 ? fb.Downsample(supersample) : fb;
    }
}
