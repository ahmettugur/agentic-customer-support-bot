# UiHintEmitter

## Ne İşe Yarar
Agent workflow'u çalışırken UI'a gönderilecek hint event'lerini session bazlı toplayan ve drain eden servisdir.

## Hangi Amaçla Kullanılır
Tool'lar veya agent'lar çalışırken (ör. "Sipariş aranıyor...", "Şikayet kaydediliyor...") gibi UI ipuçları üretir. Bu event'ler SSE stream'ine enjekte edilir.

## Sorumlulukları
- `Emit(event)` ile gelen hint event'lerini session bazlı `ConcurrentQueue`'da toplamak.
- Event'i `IApprovalContextAccessor` üzerinden okunan ambient session/agent bilgisiyle etiketlemek.
- `DrainPending(sessionId)` ile birikmiş event'leri toplu olarak vermek ve kuyruğu temizlemek.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Implements**: `IUiHintEmitter` (Inbound port).
- **DI ile inject edilen**: `IApprovalContextAccessor` — ambient session/agent context.
- **Kullanan sınıflar**: `CustomerSupportToolsService` (tool çalıştığında hint emit eder), `ChatPortService` (drain eder ve SSE'ye yazar).

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Agent workflow asenkron çalışırken UI'a ara bildirimler göndermek için kullanılır. `ConcurrentDictionary` + `ConcurrentQueue` ile thread-safe; paralel alt görevlerden eş zamanlı çağrılabilir. Agent bilgisi ambient context'ten okunur — frontend hangi agent'ın hint gönderdiğini tahmin etmek zorunda kalmaz.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `Emit(evt)` | Hint event'ini session'ın kuyruğuna ekler; agent adıyla etiketler. |
| `DrainPending(sessionId)` | Birikmiş event'leri toplu olarak verir ve kuyruğu temizler. |

## Bağımlılıklar
- `IApprovalContextAccessor` — Ambient session/agent context.
