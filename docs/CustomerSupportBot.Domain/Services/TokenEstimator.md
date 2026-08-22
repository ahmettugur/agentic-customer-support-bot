# TokenEstimator

- **Kaynak:** `CustomerSupportBot.Domain/Services/TokenEstimator.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Domain.Services`

## Ne işe yarar?

`TokenEstimator`, harici bir tokenizer kütüphanesine (tiktoken vb.) ihtiyaç duymadan, Türkçe ve İngilizce metinler için karakter ve kelime uzunluğu üzerinden yaklaşık LLM token sayısını hesaplayan hafif bir domain yardımcı servisidir.

## Hangi amaçla kullanılır`?

- RAG bağlam boyutunu kontrol altında tutmak.
- Sohbet geçmişinin token bütçesini aşmasını engellemek ve ne zaman özetleme yapılacağına karar vermek.

## Metotlar ve İç Çalışma Mantıkları

### 1. `Estimate`
```csharp
public static int Estimate(string? text)
```
- **Ne işe yarar?:** Verilen metnin yaklaşık token adedini döner.
- **İç Mantığı:** Metin boşsa `0` döner. Türkçe metinlerde yaklaşık olarak `karakter sayısı / 3.5` veya `kelime sayısı * 1.3` formülü üzerinden kestirim yapar.

### 2. `EstimateMessages`
```csharp
public static int EstimateMessages(IEnumerable<ConversationMessage> messages)
```
- **Ne işe yarar?:** Mesaj listesindeki tüm metinlerin ve rol etiketlerinin toplam token maliyetini hesaplar.

## Bağımlılıklar

- [ConversationMessage](../Model/ConversationMessage.md)
