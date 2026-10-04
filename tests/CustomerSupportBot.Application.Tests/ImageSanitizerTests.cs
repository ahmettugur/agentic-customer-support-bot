// Yüklenen fotoğrafın türü (dosya imzasından) ve meta veri temizliği.
//
// Telefon fotoğrafları EXIF içinde konum (GPS), cihaz ve zaman bilgisi taşır; bunlar saklanmadan
// silinir. Yön (Orientation) korunur — silinirse dikey çekilmiş fotoğraflar yan görünür.

using System.Buffers.Binary;
using System.Text;
using CustomerSupportBot.Application.Services.Attachments;

namespace CustomerSupportBot.Application.Tests;

public class ImageSanitizerTests
{
    // ─── JPEG yardımcıları ────────────────────────────────────────────────────

    private static byte[] Segment(byte marker, byte[] payload)
    {
        var s = new byte[4 + payload.Length];
        s[0] = 0xFF; s[1] = marker;
        BinaryPrimitives.WriteUInt16BigEndian(s.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(s, 4);
        return s;
    }

    /// <summary>EXIF APP1: IFD0'da Orientation + GPS IFD işaretçisi + Make ("Secret Phone").</summary>
    private static byte[] ExifPayload(ushort orientation, bool bigEndian = false)
    {
        var tiff = new List<byte>();
        void U16(ushort v) { var b = new byte[2]; if (bigEndian) BinaryPrimitives.WriteUInt16BigEndian(b, v); else BinaryPrimitives.WriteUInt16LittleEndian(b, v); tiff.AddRange(b); }
        void U32(uint v) { var b = new byte[4]; if (bigEndian) BinaryPrimitives.WriteUInt32BigEndian(b, v); else BinaryPrimitives.WriteUInt32LittleEndian(b, v); tiff.AddRange(b); }

        tiff.AddRange(bigEndian ? "MM"u8.ToArray() : "II"u8.ToArray());
        U16(42); U32(8);
        U16(3);                                   // 3 giriş
        U16(0x010F); U16(2); U32(13); U32(50);    // Make, ASCII, 13 bayt, ofset 50
        U16(0x0112); U16(3); U32(1); U16(orientation); U16(0);
        U16(0x8825); U16(4); U32(1); U32(63);     // GPS IFD işaretçisi
        U32(0);                                   // sonraki IFD yok
        tiff.AddRange("Secret Phone\0"u8.ToArray());
        tiff.AddRange("GPS-41.0N-29.0E"u8.ToArray());
        return [.. "Exif\0\0"u8.ToArray(), .. tiff];
    }

    private static byte[] Jpeg(params byte[][] segments)
    {
        var scan = new byte[] { 0x12, 0x34, 0xFF, 0x00, 0x56 };   // entropi kodlu veri (FF00 kaçışlı)
        return [0xFF, 0xD8, .. segments.SelectMany(s => s), .. Segment(0xDA, [0x01, 0x02, 0x03]), .. scan, 0xFF, 0xD9];
    }

    private static readonly byte[] App0Jfif = Segment(0xE0, "JFIF\0\x01\x01\0\0\x01\0\x01\0\0"u8.ToArray());
    private static readonly byte[] Dqt = Segment(0xDB, Enumerable.Repeat((byte)7, 65).ToArray());
    private static readonly byte[] Comment = Segment(0xFE, "kamera: secret"u8.ToArray());
    private static readonly byte[] Xmp = Segment(0xE1, "http://ns.adobe.com/xap/1.0/\0<x:gps>41N</x:gps>"u8.ToArray());

    // ─── PNG yardımcıları ─────────────────────────────────────────────────────

    private static byte[] Chunk(string type, byte[] data)
    {
        var c = new byte[12 + data.Length];
        BinaryPrimitives.WriteUInt32BigEndian(c, (uint)data.Length);
        Encoding.ASCII.GetBytes(type).CopyTo(c, 4);
        data.CopyTo(c, 8);
        BinaryPrimitives.WriteUInt32BigEndian(c.AsSpan(8 + data.Length), Crc32(c.AsSpan(4, 4 + data.Length)));
        return c;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++) crc = (crc & 1) != 0 ? 0xEDB88320u ^ (crc >> 1) : crc >> 1;
        }
        return ~crc;
    }

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static byte[] Png(params byte[][] chunks) => [.. PngSignature, .. chunks.SelectMany(c => c)];

    private static readonly byte[] Ihdr = Chunk("IHDR", [0, 0, 0, 1, 0, 0, 0, 1, 8, 6, 0, 0, 0]);
    private static readonly byte[] Idat = Chunk("IDAT", [0x78, 0x9C, 0x63, 0x00, 0x01]);
    private static readonly byte[] Iend = Chunk("IEND", []);

    private static bool Contains(byte[] haystack, string needle) =>
        haystack.AsSpan().IndexOf(Encoding.ASCII.GetBytes(needle)) >= 0;

    // ─── Tür tespiti ──────────────────────────────────────────────────────────

    [Fact]
    public void Detects_ByFileSignature()
    {
        ImageSanitizer.Detect(Jpeg(App0Jfif)).Should().Be(ImageKind.Jpeg);
        ImageSanitizer.Detect(Png(Ihdr, Idat, Iend)).Should().Be(ImageKind.Png);
        ImageSanitizer.Detect("GIF89a"u8.ToArray()).Should().Be(ImageKind.Unknown);
        ImageSanitizer.Detect("<svg onload=alert(1)>"u8.ToArray()).Should().Be(ImageKind.Unknown);
        ImageSanitizer.Detect([0xFF]).Should().Be(ImageKind.Unknown);
    }

    // ─── JPEG ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Jpeg_StripsExifXmpAndComments_ButKeepsImageData()
    {
        var input = Jpeg(App0Jfif, Segment(0xE1, ExifPayload(6)), Xmp, Comment, Dqt);

        var output = ImageSanitizer.StripMetadata(input, ImageKind.Jpeg)!;

        Contains(output, "Secret Phone").Should().BeFalse("cihaz bilgisi silinmeli");
        Contains(output, "GPS").Should().BeFalse("konum silinmeli");
        Contains(output, "kamera").Should().BeFalse("yorum silinmeli");
        Contains(output, "<x:gps>").Should().BeFalse("XMP silinmeli");
        Contains(output, "JFIF").Should().BeTrue();
        output.AsSpan().IndexOf(Dqt).Should().BeGreaterThan(0, "görüntü tabloları korunmalı");
        output[^7..].Should().Equal(0x12, 0x34, 0xFF, 0x00, 0x56, 0xFF, 0xD9);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Jpeg_KeepsOnlyTheOrientation(bool bigEndian)
    {
        var output = ImageSanitizer.StripMetadata(Jpeg(App0Jfif, Segment(0xE1, ExifPayload(6, bigEndian))), ImageKind.Jpeg)!;

        ImageSanitizer.ReadJpegOrientation(output).Should().Be(6, "dikey fotoğraf yan görünmemeli");
    }

    [Fact]
    public void Jpeg_WithoutOrientation_HasNoExifAtAll()
    {
        var output = ImageSanitizer.StripMetadata(Jpeg(App0Jfif, Segment(0xE1, ExifPayload(1))), ImageKind.Jpeg)!;

        ImageSanitizer.ReadJpegOrientation(output).Should().BeNull("varsayılan yön (1) için EXIF gerekmez");
        Contains(output, "Exif").Should().BeFalse();
    }

    [Fact]
    public void Jpeg_Truncated_IsRejected()
    {
        var broken = Jpeg(App0Jfif)[..8];
        ImageSanitizer.StripMetadata(broken, ImageKind.Jpeg).Should().BeNull();
    }

    // ─── PNG ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Png_StripsTextExifAndTimeChunks()
    {
        var input = Png(Ihdr,
            Chunk("tEXt", "Author\0Ayse Yilmaz"u8.ToArray()),
            Chunk("eXIf", "MM\0*GPS"u8.ToArray()),
            Chunk("tIME", [7, 234, 10, 4, 1, 2, 3]),
            Chunk("iTXt", "Comment\0\0\0\0\0secret"u8.ToArray()),
            Idat, Iend);

        var output = ImageSanitizer.StripMetadata(input, ImageKind.Png)!;

        output.Should().Equal(Png(Ihdr, Idat, Iend));
    }

    [Fact]
    public void Png_Truncated_IsRejected()
    {
        var broken = Png(Ihdr, Idat, Iend)[..20];
        ImageSanitizer.StripMetadata(broken, ImageKind.Png).Should().BeNull();
    }
}
