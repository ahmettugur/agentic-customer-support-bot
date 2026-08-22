# IdExtractor

- **Kaynak:** `CustomerSupportBot.Domain/Services/IdExtractor.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Domain.Services`

## Ne işe yarar?

`IdExtractor`, kullanıcı mesajından veya sohbet metninden deterministik olarak (hiçbir LLM çağrısı olmadan, tamamen regex ve Türkçe bağlam analizi ile) `order_id`, `complaint_id` ve `customer_id` numaralarını (en az 4 haneli) çıkaran saf domain servisidir.

## Hangi amaçla kullanılır`?

- Kullanıcının "1041 numaralı siparişim nerede?" veya "şikayetim 1003 hakkında bilgi ver" gibi mesajlarından varlık ID'lerini yakalamak.
- **Negatif Lookbehind ile Zehirlenmeyi Önleme:** "Sipariş numaram 1041" cümlesinde "numaram" kelimesinin tek başına müşteri kimliği sanılmasını engelleyerek kalıcı oturum durumunun (`state.CustomerId`) yanlış bir sipariş numarasıyla zehirlenmesini önlemek (`(?<!\bsipari[sş]\w*\s)(?<!\bşikayet\w*\s)\bnumaram\b`).
- Uzman ajan prompt'larına enjekte edilecek varlık ipucu mesajını (`BuildHintMessage`) üretmek.

## Sorumlulukları

- **Üstlendiği:**
  - `Extract` ile metindeki sayıları bağlam kelimelerine (sipariş, şikayet, müşteri) yakınlığına göre eşleştirmek.
  - `BuildHintMessage` ile çıkarılan ID'leri sistem mesajı formatına (`order_id MEVCUT: 1041`) dönüştürmek.

## Metotlar ve İç Çalışma Mantıkları

### 1. `Extract`
```csharp
public static ExtractedIds Extract(string text)
```
- **Ne işe yarar?:** Metinden tüm ID türlerini çıkarır (`ExtractedIds`).
- **İç Mantığı:**
  1. `\b(\d{4,})\b` deseniyle tüm 4+ haneli sayılar bulunur.
  2. Tek sayı varsa ve sipariş kelimesi geçiyorsa `OrderId`, şikayet kelimesi geçiyorsa `ComplaintId`, müşteri kelimesi geçiyorsa veya bağlam yoksa `CustomerId` olarak atanır.
  3. Birden fazla sayı varsa her bir sayının kelime indeksine en yakın bağlam kelimesi (Mesafe algoritması) bulunarak ilgili ID alanına atanır.

### 2. `BuildHintMessage`
```csharp
public static string? BuildHintMessage(ExtractedIds ids)
```
- **Ne işe yarar?:** Çıkarılan ID'leri ajanların göreceği sistem istemi formatına çevirir (Örn: `"KULLANICI MESAJINDAN ÇIKARILAN ID'LER:\n- order_id MEVCUT: 1041\nBu ID'leri tool çağrılarında doğrudan kullanın."`).

## Bağımlılıklar

- [ExtractedIds](../Model/ExtractedIds.md)
- `System.Text.RegularExpressions.Regex`
