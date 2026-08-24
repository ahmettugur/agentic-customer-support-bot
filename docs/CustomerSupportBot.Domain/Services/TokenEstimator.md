# TokenEstimator

- **Kaynak:** `CustomerSupportBot.Domain/Services/TokenEstimator.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Domain.Services`

## 1. Ne işe yarar?

`TokenEstimator`, harici bir tokenizer kütüphanesine (tiktoken vb.) bağımlı olmadan, karakter uzunluğu üzerinden bir metnin veya mesaj dizisinin **kaba** LLM token büyüklüğünü tahmin eden hafif bir domain yardımcı servisidir.

## 2. Hangi amaçla kullanılır?

**Yalnızca gözlemlenebilirlik/trend takibi içindir — bütçe uygulaması (enforcement) için DEĞİLDİR.**

> 🐞 **Neden eklendi, ne için eklenmedi:** `ReasoningTrace.EstimatedTokens` alanı (DB kolonuna
> kadar) önceden mevcuttu ama hiçbir yerde yazılmıyordu, hep `0` kalıyordu — "prompt büyüklüğü
> oturum uzadıkça nasıl bir eğri çiziyor?" sorusu cevapsızdı. `TokenEstimator`, `WorkflowRunner`
> tarafından yalnızca bu alanı doldurmak için çağrılır (bkz. `WorkflowRunner.cs`'teki
> `st.Trace.EstimatedTokens = TokenEstimator.Estimate(...)` satırları). Gerçek bir tokenizer
> **bilerek eklenmedi**: trend izlemek için gereken doğruluk düşük, maliyeti ise yeni bir paket
> ve model-başına sözlük yönetimi. Mutlak değer yanılabilir; aranan şey turlar arası **oran**dır.
>
> Bu sınırın kod incelemesinde (K3 analizi, bulgu 2.4) "geçmiş kırpma sayı-bazlı, token bütçesi
> yok — `TokenEstimator` yalnızca telemetri için kullanılıyor" diye eleştirilmesi üzerine
> incelendi: mevcut savunmalar (`ConversationSummaryProvider`'ın mesaj-sayısı bazlı özetlemesi
> — 8+ mesajdan sonra son 4 mesaj ham + ≤150 kelimelik özet — ve `ContextPipelineOptions.
> MaxTotalChars`'ın 12000 karakterlik toplam sınırı) pratikte context window aşımını zaten
> makul ölçüde engelliyor. `TokenEstimator`'ı ikinci bir kırpma katmanı olarak kullanmak,
> kendi belgesinin açıkça uyardığı riski (kaba/yanılabilir bir tahminle geçmişten veri
> silmek) göze almak olurdu — bu yüzden **bilinçli olarak eklenmedi**. Gerçek kalan boşluk çok
> daha dar: yalnızca TEK bir mesajın (ör. kullanıcının çok uzun bir metin yapıştırması)
> `ConversationSummaryProvider`'ın "son 4 mesaj" penceresini tek başına doldurabilmesi —
> bu, ayrı bir tekil-mesaj-boyutu kontrolü (ör. `InputLimitedAgent` benzeri) gerektirir,
> `TokenEstimator`'ın kapsamı değil.

## 3. Sorumlulukları

- **Üstlendiği:** Bir metnin veya mesaj dizisinin yaklaşık token maliyetini hesaplamak.
- **Üstlenmediği:** Bu tahmine dayanarak herhangi bir kırpma/reddetme kararı vermek — hiçbir çağıran bunu yapmaz.

## 4. Diğer katman ve bileşenlerle ilişkileri

- `WorkflowRunner` (Adapters.Agents), her turda `st.Trace.EstimatedTokens`'ı doldurmak için çağırır.
- [ReasoningTrace](../Model/ReasoningTrace.md) — hesaplanan değerin yazıldığı alan.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Yukarıdaki 🐞 bloğuna bakınız — gerçek tokenizer'ın kasıtlı olarak tercih edilmemesi ve bu sınıfın enforcement değil observability amaçlı tasarlanması.

## 6. Metotlar / Üyeler

### `Estimate(string? text)`
```csharp
public static long Estimate(string? text)
```
Tek bir metnin tahmini token sayısını döner (`text.Length / 3`; Türkçe sondan eklemeli ve aksanlı olduğu için karakter/token oranı İngilizce'nin yaygın kabul gören ~4'ünden düşük tutuldu — tahmin sistematik olarak biraz yüksek çıksın istendi, bütçe uyarısı geç kalmaktansa erken gelsin diye).

### `Estimate(IEnumerable<string?> texts)`
```csharp
public static long Estimate(IEnumerable<string?> texts)
```
Bir mesaj dizisinin tahmini toplam token sayısını döner — her metin için yukarıdaki `Estimate(string?)` + mesaj başına sabit 4 token'lık çerçeve maliyeti (rol etiketi, ayraçlar) eklenir.

## 7. Bağımlılıklar

Yok — saf, bağımlılıksız domain servisi.
