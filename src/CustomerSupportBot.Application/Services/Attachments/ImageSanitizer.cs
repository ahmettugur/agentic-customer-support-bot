// Application/Services/Attachments/ImageSanitizer.cs
// Yüklenen fotoğrafın türünü dosya imzasından tespit eder ve meta verisini siler.

using System.Buffers.Binary;

namespace CustomerSupportBot.Application.Services.Attachments;

public enum ImageKind
{
    Unknown,
    Jpeg,
    Png
}

/// <summary>
/// Saf, bağımlılıksız görüntü temizliği.
///
/// <para>
/// <b>Tür</b>, dosya adına ya da istemcinin bildirdiği içerik türüne değil dosyanın ilk baytlarına
/// (imza) göre belirlenir — <c>.jpg</c> uzantılı bir SVG/HTML kabul edilmez.
/// </para>
///
/// <para>
/// <b>Meta veri:</b> telefon fotoğrafları EXIF içinde konum (GPS), cihaz, zaman; XMP ve yorum
/// alanlarında başka bilgiler taşır. Bunlar saklanmadan silinir. JPEG'de APP1–APP15 (APP2 ICC renk
/// profili ve APP14 Adobe renk dönüşümü hariç — kişisel veri taşımaz, silinmeleri renkleri bozar) ve
/// COM silinir; PNG'de metin (<c>tEXt</c>, <c>zTXt</c>, <c>iTXt</c>), <c>eXIf</c> ve <c>tIME</c>.
/// Yalnızca EXIF yön bilgisi (Orientation) korunur: silinirse dikey çekilmiş fotoğraflar yan görünür.
/// Bunun için yalnızca o etiketi içeren yeni, küçük bir EXIF bölümü yazılır.
/// </para>
///
/// Bozuk/kesik dosyada <c>null</c> döner.
/// </summary>
public static class ImageSanitizer
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] ExifHeader = "Exif\0\0"u8.ToArray();
    private static readonly HashSet<string> PngDroppedChunks = ["tEXt", "zTXt", "iTXt", "eXIf", "tIME"];

    private const ushort OrientationTag = 0x0112;

    public static ImageKind Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ImageKind.Jpeg;
        if (data.Length >= 8 && data[..8].SequenceEqual(PngSignature)) return ImageKind.Png;
        return ImageKind.Unknown;
    }

    public static byte[]? StripMetadata(byte[] data, ImageKind kind) => kind switch
    {
        ImageKind.Jpeg => StripJpeg(data),
        ImageKind.Png => StripPng(data),
        _ => null
    };

    // ─── JPEG ─────────────────────────────────────────────────────────────────

    private static byte[]? StripJpeg(byte[] data)
    {
        if (Detect(data) != ImageKind.Jpeg) return null;

        using var output = new MemoryStream(data.Length);
        output.Write(data, 0, 2);   // SOI

        ushort? orientation = null;
        var orientationWritten = false;
        var pos = 2;

        while (true)
        {
            if (pos + 4 > data.Length || data[pos] != 0xFF) return null;
            var marker = data[pos + 1];

            // Doldurma baytları (FF FF …) — atla.
            if (marker == 0xFF) { pos++; continue; }

            // Parametresiz işaretçiler (RSTn, TEM) burada beklenmez ama bozuk sayılmaz.
            if (marker is 0x01 or >= 0xD0 and <= 0xD7) { output.Write(data, pos, 2); pos += 2; continue; }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(pos + 2));
            if (length < 2 || pos + 2 + length > data.Length) return null;
            var segment = data.AsSpan(pos, 2 + length);
            var payload = data.AsSpan(pos + 4, length - 2);

            if (marker == 0xE1)
            {
                // EXIF → yalnızca yön bilgisini al; XMP ve diğer APP1'ler tamamen düşer.
                if (payload.StartsWith(ExifHeader)) orientation ??= ReadOrientation(payload[ExifHeader.Length..]);
            }
            else if (marker == 0xDA)
            {
                // SOS: buradan sonrası görüntü verisi — APP0'dan sonra, ilk tablo segmentinden önce
                // yazılmamışsa yön bilgisini şimdi yaz, kalanı olduğu gibi kopyala.
                if (!orientationWritten) WriteOrientationSegment(output, orientation);
                output.Write(data, pos, data.Length - pos);
                return output.ToArray();
            }
            else if (IsDroppedJpegMarker(marker))
            {
                // atla
            }
            else
            {
                // Yön bilgisini JFIF (APP0) sonrasında ilk korunan segmentten önce yaz.
                if (!orientationWritten && marker != 0xE0)
                {
                    WriteOrientationSegment(output, orientation);
                    orientationWritten = true;
                }
                output.Write(segment);
            }

            pos += 2 + length;
        }
    }

    private static bool IsDroppedJpegMarker(byte marker) =>
        marker == 0xFE                                     // COM
        || (marker >= 0xE1 && marker <= 0xEF && marker != 0xE2 && marker != 0xEE);   // APPn (ICC, Adobe hariç)

    /// <summary>TIFF yapısından IFD0'daki Orientation değerini okur (2–8; 1 varsayılandır, yazılmaz).</summary>
    private static ushort? ReadOrientation(ReadOnlySpan<byte> tiff)
    {
        if (tiff.Length < 8) return null;
        bool big;
        if (tiff[0] == 'I' && tiff[1] == 'I') big = false;
        else if (tiff[0] == 'M' && tiff[1] == 'M') big = true;
        else return null;

        ushort U16(ReadOnlySpan<byte> s) => big ? BinaryPrimitives.ReadUInt16BigEndian(s) : BinaryPrimitives.ReadUInt16LittleEndian(s);
        uint U32(ReadOnlySpan<byte> s) => big ? BinaryPrimitives.ReadUInt32BigEndian(s) : BinaryPrimitives.ReadUInt32LittleEndian(s);

        var ifd = U32(tiff[4..]);
        if (ifd + 2 > (uint)tiff.Length) return null;
        var count = U16(tiff[(int)ifd..]);
        for (var i = 0; i < count; i++)
        {
            var entry = (int)ifd + 2 + i * 12;
            if (entry + 12 > tiff.Length) return null;
            if (U16(tiff[entry..]) != OrientationTag) continue;
            var value = U16(tiff[(entry + 8)..]);
            return value is >= 2 and <= 8 ? value : null;
        }
        return null;
    }

    /// <summary>Yalnızca Orientation etiketi içeren en küçük EXIF APP1 bölümü (little-endian TIFF).</summary>
    private static void WriteOrientationSegment(Stream output, ushort? orientation)
    {
        if (orientation is not { } value) return;

        Span<byte> tiff = stackalloc byte[26];
        "II"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff[2..], 42);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff[4..], 8);           // IFD0 ofseti
        BinaryPrimitives.WriteUInt16LittleEndian(tiff[8..], 1);           // 1 giriş
        BinaryPrimitives.WriteUInt16LittleEndian(tiff[10..], OrientationTag);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff[12..], 3);          // SHORT
        BinaryPrimitives.WriteUInt32LittleEndian(tiff[14..], 1);          // adet
        BinaryPrimitives.WriteUInt16LittleEndian(tiff[18..], value);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff[22..], 0);          // sonraki IFD yok

        Span<byte> header = stackalloc byte[4];
        header[0] = 0xFF; header[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(header[2..], (ushort)(2 + ExifHeader.Length + tiff.Length));
        output.Write(header);
        output.Write(ExifHeader);
        output.Write(tiff);
    }

    /// <summary>Temizlenmiş JPEG'in yön bilgisi (EXIF yoksa ya da 1 ise <c>null</c>).</summary>
    public static ushort? ReadJpegOrientation(byte[] jpeg)
    {
        var pos = 2;
        while (pos + 4 <= jpeg.Length && jpeg[pos] == 0xFF)
        {
            var marker = jpeg[pos + 1];
            if (marker == 0xDA) break;
            var length = BinaryPrimitives.ReadUInt16BigEndian(jpeg.AsSpan(pos + 2));
            if (pos + 2 + length > jpeg.Length) break;
            var payload = jpeg.AsSpan(pos + 4, length - 2);
            if (marker == 0xE1 && payload.StartsWith(ExifHeader))
                return ReadOrientation(payload[ExifHeader.Length..]);
            pos += 2 + length;
        }
        return null;
    }

    // ─── PNG ──────────────────────────────────────────────────────────────────

    private static byte[]? StripPng(byte[] data)
    {
        if (Detect(data) != ImageKind.Png) return null;

        using var output = new MemoryStream(data.Length);
        output.Write(PngSignature);

        var pos = PngSignature.Length;
        while (pos < data.Length)
        {
            if (pos + 12 > data.Length) return null;
            var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(pos));
            if (length > int.MaxValue - 12 || pos + 12 + (int)length > data.Length) return null;

            var chunkLength = 12 + (int)length;
            var type = System.Text.Encoding.ASCII.GetString(data, pos + 4, 4);
            if (!PngDroppedChunks.Contains(type))
                output.Write(data, pos, chunkLength);

            pos += chunkLength;
            if (type == "IEND") return output.ToArray();
        }

        return null;   // IEND yok — kesik dosya
    }
}
