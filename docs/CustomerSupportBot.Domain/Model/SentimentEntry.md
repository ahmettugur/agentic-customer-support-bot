# SentimentEntry

- **Kaynak:** `CustomerSupportBot.Domain/Model/AgentSession.cs` (aynı dosyada `AgentSession` ve `SessionState` ile birlikte tanımlı)
- **Tür:** `public class` (mutable)
- **Namespace:** `CustomerSupportBot.Domain.Model`

## 1. Ne İşe Yarar

Bir konuşmanın **tek bir turu** için tespit edilen duygu (sentiment) kaydını temsil eder —
etiket, skor ve zaman damgası.

## 2. Hangi Amaçla Kullanılır

`SessionStateExtractor.DetectSentiment` her kullanıcı turunda (LLM sinyali varsa ondan, yoksa
`WellKnown.SentimentKeywords` tablosundan) bir `SentimentEntry` üretir ve
[SessionState](AgentSession.md)'in duygu geçmişi listesine ekler. Ardışık negatif tur sayacı bu
kayıtlardan hesaplanır ve eşiği aştığında (`WellKnown.SentimentThresholds.AutoEscalationConsecutiveNegative`)
otomatik eskalasyon tetiklenir.

## 3. Sorumlulukları

- ✅ Bir turun duygu etiketini, skorunu ve zamanını taşımak
- ❌ Duygu tespitini yapmak — bu `SessionStateExtractor.DetectSentiment`'in işi
- ❌ Eskalasyon kararını vermek — bu duygu geçmişini tüketen ayrı bir mantığın işi

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim üretir:** `SessionStateExtractor.DetectSentiment` (Domain katmanı, saf/deterministik veya
  LLM sinyaline dayalı)
- **Kim tüketir:** Otomatik eskalasyon mantığı, admin panelindeki duygu grafikleri/analitik

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 💡 Her turun ayrı bir `SentimentEntry` olarak saklanması, sadece "son duygu" değil **duygu
> trendinin** (ör. 3 tur üst üste negatif) izlenmesini sağlar — otomatik eskalasyon eşiği tek bir
> anlık kötü mesaja değil, ardışık kalıba dayanır.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Turn` | `int` | Bu duygu kaydının ait olduğu tur numarası |
| `Label` | `string` | Duygu etiketi — `WellKnown.Sentiments` değerlerinden biri, varsayılan `Neutral` |
| `Score` | `double` | 0.0–1.0 arası duygu skoru, varsayılan `0.5` |
| `Timestamp` | `DateTime` | Kaydın oluşturulduğu an (UTC) |

## 7. Bağımlılıklar

Yok — saf domain modeli, dış bağımlılığı yok.

## Bağlantılar

- [AgentSession.md](AgentSession.md) — Bu kaydı tutan `SessionState`
- [../WellKnown.md](../WellKnown.md) — `Sentiments`/`SentimentKeywords`/`SentimentThresholds` sabitleri
- [../Services/SessionStateExtractor.md](../Services/SessionStateExtractor.md) — Üretici servis
