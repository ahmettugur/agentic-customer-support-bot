# ICustomerUnderstandingService

**Kaynak:** `Ports/Outbound/ICustomerUnderstandingService.cs`
**Implementasyon:** [`CustomerUnderstandingService`](../../Services/Personalization/CustomerUnderstandingService.md)

## 1. Ne İşe Yarar

Memory'nin üç kaynağını (yapısal olgular, profil, LLM-türetilmiş çıkarımlar) tek bir
`CustomerUnderstanding` nesnesinde sentezleyen port.

## 2. Hangi Amaçla Kullanılır

`CustomerProfileContextProvider` (bkz. [ContextProviders.md](../../Providers/ContextProviders.md))
bir turun bağlamını kurarken `Build(session)`'ı çağırır.

## 3. Sorumlulukları

- **Üstlendiği:** Üç kaynağı birleştirme mantığını (null-kontrolleri, "tur sıfırsa gösterme"
  kuralı, sıralama) tek bir yerde uygulamak.
- **Üstlenmediği:** Verinin kendisinin depolanması — o
  [`ICustomerProfileStore`](Persistence/ICustomerProfileStore.md)'un işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Application/Services/Personalization/CustomerUnderstandingService` implemente eder.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Tek doğruluk kaynağı olması bilinçli: sentez mantığı burada bir kez yazılır; hem bugünkü
tüketici (`CustomerProfileContextProvider`) hem de ileride eklenecek bir öneri motoru aynı
kuralları iki kez uygulamak zorunda kalmaz. `Build`'in oturumun doğrulanmış müşteri kimliği
yoksa, profili yoksa veya profil hiç etkileşim görmemişse (`TotalTurns == 0`) `null` dönmesi
kasıtlıdır — "gösterilecek bir şey yok" ile "bilgi var ama boş" ayrımını çağırana bırakmaz.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `CustomerUnderstanding? Build(AgentSession session)` | Oturumdan sentezlenmiş anlayışı üretir; koşullar sağlanmazsa `null`. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.AgentSession` ve
`CustomerSupportBot.Domain.Model.Memory.CustomerUnderstanding`'e bağımlıdır.
