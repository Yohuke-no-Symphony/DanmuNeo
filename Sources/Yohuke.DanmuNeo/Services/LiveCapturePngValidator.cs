using System.Buffers.Binary;
using Yohuke.DanmuNeo.I18n;

namespace Yohuke.DanmuNeo.Services;

/// <summary>在解码前限制原帧载荷、尺寸与 PNG 块边界。</summary>
internal static class LiveCapturePngValidator
{
    internal const int MAXIMUM_BYTES = 32 * 1024 * 1024;
    internal const int MAXIMUM_DIMENSION = 8192;
    internal const long MAXIMUM_PIXELS = 34_000_000;
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    internal static (int Width, int Height) Validate(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 45 || bytes.Length > MAXIMUM_BYTES)
        {
            throw new InvalidDataException(Localization.Get("Captures.InvalidImage"));
        }
        var dimensions = ValidateHeader(bytes);
        var offset = 8;
        var hasData = false;
        while (offset <= bytes.Length - 12)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4));
            if (length > bytes.Length - offset - 12)
            {
                break;
            }
            var type = bytes.Slice(offset + 4, 4);
            hasData |= type.SequenceEqual("IDAT"u8);
            if (type.SequenceEqual("IEND"u8))
            {
                if (length == 0 && offset + 12 == bytes.Length && hasData)
                {
                    return dimensions;
                }
                break;
            }
            offset += (int)length + 12;
        }
        throw new InvalidDataException(Localization.Get("Captures.InvalidImage"));
    }

    internal static (int Width, int Height) ValidateHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 24 || !bytes[..8].SequenceEqual(Signature) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(8, 4)) != 13 ||
            !bytes.Slice(12, 4).SequenceEqual("IHDR"u8))
        {
            throw new InvalidDataException(Localization.Get("Captures.InvalidImage"));
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(16, 4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(20, 4));
        if (width == 0 || height == 0 || width > MAXIMUM_DIMENSION || height > MAXIMUM_DIMENSION ||
            (long)width * height > MAXIMUM_PIXELS)
        {
            throw new InvalidDataException(Localization.Get("Captures.ImageTooLarge"));
        }

        return ((int)width, (int)height);
    }
}
