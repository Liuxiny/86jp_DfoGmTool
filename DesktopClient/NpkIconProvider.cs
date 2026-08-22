using System.Collections.Concurrent;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Jp86.GmClient;

// 仅实现物品图标所需的 NPK/IMG 只读路径。格式处理参考 pvfUtility 的
// NpkCoder 与各版本 Handler，避免把它的 DevExpress 等编辑器依赖带入发布件。
public sealed class NpkIconProvider
{
    private const string NpkSignature = "NeoplePack_Bill";
    private static readonly byte[] PathKey = BuildPathKey();
    private readonly ConcurrentDictionary<string, NpkEntry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, ImageSource?> _memoryCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _indexGate = new(1, 1);
    private string _directory = "";
    private bool _indexed;

    public void SetDirectory(string? directory)
    {
        directory = directory?.Trim() ?? "";
        if (string.Equals(directory, _directory, StringComparison.OrdinalIgnoreCase)) return;
        _directory = directory;
        _indexed = false;
        _entries.Clear();
        _memoryCache.Clear();
    }

    public async Task<ImageSource?> GetIconAsync(string iconPath, int iconIndex, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(iconPath) || iconIndex < 0 || !Directory.Exists(_directory)) return null;
        var key = iconPath.Replace('\\', '/').TrimStart('/').ToLowerInvariant() + "#" + iconIndex;
        if (_memoryCache.TryGetValue(key, out var cached)) return cached;
        await EnsureIndexAsync(cancellationToken).ConfigureAwait(false);
        if (!_entries.TryGetValue(iconPath.Replace('\\', '/').TrimStart('/'), out var entry))
        {
            return null;
        }
        var image = await Task.Run(() => Decode(entry, iconIndex), cancellationToken).ConfigureAwait(false);
        if (image is Freezable freezable && freezable.CanFreeze) freezable.Freeze();
        if (image != null) _memoryCache[key] = image;
        return image;
    }

    private async Task EnsureIndexAsync(CancellationToken cancellationToken)
    {
        if (_indexed) return;
        await _indexGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_indexed) return;
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            foreach (var file in Directory.EnumerateFiles(_directory, "*.npk", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { ReadNpkIndex(file); } catch { /* 单个损坏 NPK 不阻止其他图标。 */ }
            }
            _indexed = true;
        }
        finally { _indexGate.Release(); }
    }

    private void ReadNpkIndex(string filePath)
    {
        using var stream = File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (!string.Equals(ReadCString(stream, Encoding.ASCII), NpkSignature, StringComparison.Ordinal)) return;
        var count = ReadInt32(stream);
        if (count <= 0 || count > 200000) return;
        for (var i = 0; i < count; i++)
        {
            var offset = ReadInt32(stream);
            var length = ReadInt32(stream);
            var path = ReadEncryptedPath(stream).Replace('\\', '/').TrimStart('/').ToLowerInvariant();
            if (offset > 0 && length > 0 && path.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
                _entries.TryAdd(path, new NpkEntry(filePath, offset, length, path));
        }
    }

    private static ImageSource? Decode(NpkEntry entry, int requestedIndex)
    {
        try
        {
            using var stream = File.Open(entry.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            stream.Position = entry.Offset;
            var signature = ReadCString(stream, Encoding.ASCII);
            if (signature == "Neople Image File") return DecodeVersion1(stream, requestedIndex);
            if (signature != "Neople Img File") return null;
            var indexLength = ReadInt64(stream);
            var version = ReadInt32(stream);
            var count = ReadInt32(stream);
            if (count <= 0 || requestedIndex >= count || count > 100000) return null;
            return version switch
            {
                2 => DecodeIndexedFrames(stream, indexLength, count, requestedIndex, null),
                4 => DecodeVersion4(stream, indexLength, count, requestedIndex),
                6 => DecodeVersion6(stream, indexLength, count, requestedIndex),
                _ => null,
            };
        }
        catch { return null; }
    }

    private static ImageSource? DecodeVersion1(Stream stream, int requestedIndex)
    {
        _ = ReadInt32(stream);
        stream.Position += 2;
        _ = ReadInt32(stream);
        var count = ReadInt32(stream);
        if (count <= 0 || requestedIndex >= count) return null;
        var frames = new FrameMeta[count];
        for (var i = 0; i < count; i++)
        {
            var type = ReadInt32(stream);
            if (type == 17) frames[i] = new FrameMeta { Type = type, Link = ReadInt32(stream) };
            else
            {
                var frame = ReadFrameMeta(stream, type);
                frame.DataOffset = stream.Position;
                stream.Position += EffectiveLength(frame);
                frames[i] = frame;
            }
        }
        return DecodeFrame(stream, frames, requestedIndex, null);
    }

    private static ImageSource? DecodeVersion4(Stream stream, long indexLength, int count, int requestedIndex)
    {
        var palette = ReadPalette(stream, ReadInt32(stream));
        return DecodeIndexedFrames(stream, indexLength, count, requestedIndex, palette);
    }

    private static ImageSource? DecodeVersion6(Stream stream, long indexLength, int count, int requestedIndex)
    {
        byte[]? palette = null;
        var tableCount = ReadInt32(stream);
        if (tableCount < 0 || tableCount > 1000) return null;
        for (var table = 0; table < tableCount; table++)
        {
            var current = ReadPalette(stream, ReadInt32(stream));
            palette ??= current;
        }
        return DecodeIndexedFrames(stream, indexLength, count, requestedIndex, palette);
    }

    private static ImageSource? DecodeIndexedFrames(Stream stream, long indexLength, int count, int requestedIndex, byte[]? palette)
    {
        var metadataStart = stream.Position;
        var dataStart = metadataStart + indexLength;
        var frames = new FrameMeta[count];
        for (var i = 0; i < count; i++)
        {
            var type = ReadInt32(stream);
            frames[i] = type == 17
                ? new FrameMeta { Type = type, Link = ReadInt32(stream) }
                : ReadFrameMeta(stream, type);
        }
        if (stream.Position > dataStart) return null;
        var cursor = dataStart;
        foreach (var frame in frames)
        {
            if (frame.Type == 17) continue;
            frame.DataOffset = cursor;
            cursor += EffectiveLength(frame);
        }
        return DecodeFrame(stream, frames, requestedIndex, palette);
    }

    private static FrameMeta ReadFrameMeta(Stream stream, int type) => new()
    {
        Type = type,
        Compression = ReadInt32(stream),
        Width = ReadInt32(stream),
        Height = ReadInt32(stream),
        Length = ReadInt32(stream),
        X = ReadInt32(stream),
        Y = ReadInt32(stream),
        FrameWidth = ReadInt32(stream),
        FrameHeight = ReadInt32(stream),
    };

    private static int EffectiveLength(FrameMeta frame)
    {
        if (frame.Compression != 5) return frame.Length;
        var bytesPerPixel = frame.Type == 16 ? 4 : 2;
        return checked(frame.Width * frame.Height * bytesPerPixel);
    }

    private static ImageSource? DecodeFrame(Stream stream, FrameMeta[] frames, int index, byte[]? palette)
    {
        var followed = 0;
        while (frames[index].Type == 17 && followed++ < frames.Length)
        {
            index = frames[index].Link;
            if (index < 0 || index >= frames.Length) return null;
        }
        var frame = frames[index];
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Width > 4096 || frame.Height > 4096) return null;
        var length = EffectiveLength(frame);
        if (length <= 0 || length > 128 * 1024 * 1024) return null;
        stream.Position = frame.DataOffset;
        var packed = ReadBytes(stream, length);
        byte[] raw;
        if (frame.Compression == 6)
        {
            using var input = new MemoryStream(packed);
            using var zlib = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            zlib.CopyTo(output);
            raw = output.ToArray();
        }
        else if (frame.Compression == 5) raw = packed;
        else return null;

        byte[] bgra;
        if (palette != null && frame.Type == 14 && raw.Length >= frame.Width * frame.Height)
        {
            bgra = new byte[frame.Width * frame.Height * 4];
            for (var pixel = 0; pixel < frame.Width * frame.Height; pixel++)
            {
                var color = (raw[pixel] % (palette.Length / 4)) * 4;
                Buffer.BlockCopy(palette, color, bgra, pixel * 4, 4);
            }
        }
        else bgra = ConvertToBgra(raw, frame.Width, frame.Height, frame.Type);
        if (bgra.Length == 0) return null;
        return BitmapSource.Create(frame.Width, frame.Height, 96, 96, PixelFormats.Bgra32, null, bgra, frame.Width * 4);
    }

    private static byte[] ConvertToBgra(byte[] raw, int width, int height, int type)
    {
        var pixels = width * height;
        var output = new byte[pixels * 4];
        if (type == 16 && raw.Length >= output.Length)
        {
            Buffer.BlockCopy(raw, 0, output, 0, output.Length);
            return output;
        }
        if ((type != 14 && type != 15) || raw.Length < pixels * 2) return Array.Empty<byte>();
        for (var i = 0; i < pixels; i++)
        {
            var low = raw[i * 2];
            var high = raw[i * 2 + 1];
            if (type == 14)
            {
                var a = (high >> 7) * 255;
                var r5 = (high >> 2) & 31;
                var g5 = (low >> 5) | ((high & 3) << 3);
                var b5 = low & 31;
                output[i * 4] = (byte)((b5 << 3) | (b5 >> 2));
                output[i * 4 + 1] = (byte)((g5 << 3) | (g5 >> 2));
                output[i * 4 + 2] = (byte)((r5 << 3) | (r5 >> 2));
                output[i * 4 + 3] = (byte)a;
            }
            else
            {
                output[i * 4] = (byte)((low & 15) * 17);
                output[i * 4 + 1] = (byte)((low >> 4) * 17);
                output[i * 4 + 2] = (byte)((high & 15) * 17);
                output[i * 4 + 3] = (byte)((high >> 4) * 17);
            }
        }
        return output;
    }

    private static byte[] ReadPalette(Stream stream, int count)
    {
        if (count <= 0 || count > 65536) return Array.Empty<byte>();
        var output = new byte[count * 4];
        for (var i = 0; i < count; i++)
        {
            var rgba = ReadBytes(stream, 4);
            output[i * 4] = rgba[2];
            output[i * 4 + 1] = rgba[1];
            output[i * 4 + 2] = rgba[0];
            output[i * 4 + 3] = rgba[3];
        }
        return output;
    }

    private static string ReadEncryptedPath(Stream stream)
    {
        var bytes = ReadBytes(stream, 256);
        var length = 0;
        while (length < bytes.Length && (bytes[length] ^ PathKey[length]) != 0) length++;
        var plain = new byte[length];
        for (var i = 0; i < length; i++) plain[i] = (byte)(bytes[i] ^ PathKey[i]);
        return Encoding.GetEncoding(936).GetString(plain);
    }

    private static byte[] BuildPathKey()
    {
        var key = new byte[256];
        var prefix = Encoding.UTF8.GetBytes("puchikon@neople dungeon and fighter ");
        Buffer.BlockCopy(prefix, 0, key, 0, prefix.Length);
        var dnf = Encoding.ASCII.GetBytes("DNF");
        for (var i = prefix.Length; i < 255; i++) key[i] = dnf[i % 3];
        return key;
    }

    private static string ReadCString(Stream stream, Encoding encoding)
    {
        using var buffer = new MemoryStream();
        int value;
        while ((value = stream.ReadByte()) > 0) buffer.WriteByte((byte)value);
        return encoding.GetString(buffer.ToArray());
    }

    private static int ReadInt32(Stream stream) => BitConverter.ToInt32(ReadBytes(stream, 4));
    private static long ReadInt64(Stream stream) => BitConverter.ToInt64(ReadBytes(stream, 8));
    private static byte[] ReadBytes(Stream stream, int count)
    {
        var bytes = new byte[count];
        var offset = 0;
        while (offset < count)
        {
            var read = stream.Read(bytes, offset, count - offset);
            if (read == 0) throw new EndOfStreamException();
            offset += read;
        }
        return bytes;
    }

    private sealed record NpkEntry(string FilePath, int Offset, int Length, string InternalPath);
    private sealed class FrameMeta
    {
        public int Type, Compression, Width, Height, Length, X, Y, FrameWidth, FrameHeight, Link;
        public long DataOffset;
    }
}
