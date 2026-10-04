# ImageSanitizer

- **Kaynak:** `CustomerSupportBot.Application/Services/Attachments/ImageSanitizer.cs`
- **Tür:** `public static class` (bağımlılıksız, saf)

## Ne işe yarar?

- `Detect(bytes)` — türü dosya adına ya da istemcinin bildirdiği türe değil **ilk baytlara** göre
  belirler (`.jpg` uzantılı bir SVG/HTML kabul edilmez).
- `StripMetadata(bytes, kind)` — meta veriyi siler; bozuk/kesik dosyada `null`.
  - **JPEG:** APP1 (EXIF/XMP — GPS konumu, cihaz, zaman), APP3–APP13, APP15 ve COM silinir.
    APP0 (JFIF), APP2 (ICC renk profili) ve APP14 (Adobe renk dönüşümü) korunur — kişisel veri
    taşımazlar, silinmeleri renkleri bozar. Görüntü verisi (SOS sonrası) olduğu gibi kopyalanır.
  - **PNG:** `tEXt`, `zTXt`, `iTXt`, `eXIf`, `tIME` parçaları silinir; `IEND` zorunludur.
- **Yön bilgisi korunur:** EXIF Orientation (2–8) silinirse telefonla dikey çekilmiş fotoğraf yan
  görünür. Yalnızca bu etiketi taşıyan yeni, küçük bir EXIF bölümü yazılır (`ReadJpegOrientation`).

Gerçek bir kodlayıcının (macOS `sips`) ürettiği JPEG ve PNG ile denendi: konum/telefon metni ve
Photoshop APP13 bölümü silindi, yön bilgisi kaldı, iki dosya da çözülebilir durumda.
