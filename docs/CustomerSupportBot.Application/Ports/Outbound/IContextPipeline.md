# IContextPipeline

**Kaynak:** `Ports/Outbound/IContextPipeline.cs`
**Implementasyon:** [`ContextPipeline`](../../Chat/ContextPipeline.md)

## 1. Ne İşe Yarar

Kayıtlı tüm `IContextProvider`'ları çalıştırıp birleştirilmiş bağlam metnini döndüren port'un
sözleşmesi. Tek metot: `BuildContextAsync`.

## 2. Hangi Amaçla Kullanılır

Adapter katmanı (`WorkflowRunner`/`ChatPortService`) bir tur başlamadan önce bu port üzerinden
bağlamı (müşteri profili, semantik hafıza, konuşma özeti vb.) kurar ve workflow prompt'una
ekler.

## 3. Sorumlulukları

- **Üstlendiği:** Bağlam kurulumunun tek giriş noktası olmak.
- **Üstlenmediği:** Provider'ların iç detayları (paralellik, timeout, bütçe) — bunlar
  implementasyonun (`ContextPipeline`) sorumluluğu, port yalnızca sözleşmeyi tanımlar.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Tam davranış detayı için bkz. [ContextPipeline.md](../../Chat/ContextPipeline.md) ve
[ContextProviders.md](../../Providers/ContextProviders.md).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Dönüş tipi düz `string` değil [`ContextResult`](ContextResult.md)'tır — çağıranın hangi
provider'ın katkı yaptığını bilmesi gerekebiliyor (bkz. `ContextResult` dokümanı).

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task<ContextResult> BuildContextAsync(AgentSession session, string currentQuery, CancellationToken ct = default)` | Bağlamı kurar; birleşik metin + provider bazlı rapor döner. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.AgentSession`'a ve aynı klasördeki
`ContextResult`'a bağımlıdır.
