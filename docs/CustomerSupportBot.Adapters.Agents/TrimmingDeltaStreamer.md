# TrimmingDeltaStreamer

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/TrimmingDeltaStreamer.cs`
- **Tür:** `internal sealed class`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`TrimmingDeltaStreamer`, akış halindeki metin parçalarını (chunks), akışın tamamı beklenmeden ve başında/sonunda gereksiz boşluk kalmayacak şekilde `string.Trim()` uygulanmış gibi ilerlemeli olarak yayınlayan akış yardımcı sınıfıdır.

## Hangi amaçla kullanılır`?

[DecomposedRunner](DecomposedRunner.md) sıralı alt görevlerde gerçek LLM token akışını kullanıcıya canlı iletirken, alt görevin nihai metni `Trim()`'lendiğinde canlı akış ile nihai metin arasında boşluk farkı oluşmasını engellemek için kullanılır.

## Sorumlulukları

- **Üstlendiği:**
  - İlk içerikten önceki boşlukları atmak.
  - İçerikten sonraki boşlukları sondaki boşluk olabileceği için tutmak ve arkasından yeni içerik gelirse yayınlamak.
  - Akış bittiğinde sonda kalan boşluğu düşürerek tam bir `Trim()` sonucu üretmek.

## Metotlar / Üyeler

| Üye | Tür | İmza / Tanım | Açıklama |
|---|---|---|---|
| `Feed` | Metot | `public string Feed(string? chunk)` | Yeni gelen parçayı işler ve güvenli şekilde yayınlanacak metni döner. |

## Bağımlılıklar

- `System.Text.StringBuilder`
