# IRealtimeNativeBridge

**Dosya:** `Ports/Inbound/IRealtimeNativeBridge.cs`
**Tür:** `interface`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

Sesli görüşmenin primary port'u: `gpt-realtime` gibi bir model doğrudan kendisi konuşur (STT/TTS ayrı adım değildir) ve yazılı sohbetin iş tool'larının tamamını çağırabilir; yan etkili işlemler insan onayına gönderilir. Uygulamadaki tek sesli moddur (adındaki "Native", kaldırılan köprü modundan ayırt etmek için verilmişti).

## 2. Hangi amaçla kullanılır?

WebSocket (`/chat/realtime-native`) üzerinden gelen bir sesli oturumu düşük gecikmeli bir deneyimle işlemek için kullanılır.

## 3. Sorumlulukları

- **Üstlendiği:** Modelin kendi ses üretimini ve tool çağırmayı yönetmek; yan etkili tool çağrılarını onay kapısına (`SideEffectApprovalGate`) yönlendirmek.
- **Üstlenmediği:** Yan etkili işlemin kendisi — admin onayından sonra `IApprovalExecutionRouter` yürütür.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu [`RealtimeNativeService`](../../Services/Realtime/RealtimeNativeService.md)'tir; sağlayıcıyla `OpenAiRealtimeClientAdapter` (Adapters.AI/Realtime) üzerinden konuşur.
- `IBrowserChannel` (Outbound port) üzerinden tarayıcıyla WebSocket iletişimi kurar.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

`authenticatedCustomerId`, login'li müşterinin JWT claim'inden gelen kimliktir: oturuma bir kez bağlanır ve sipariş tool'ları bunu kullanır — bu değer olmadan her sipariş sorgusu sahiplik kontrolüne takılıp "bulunamadı" döner. Kimlik hiçbir zaman modelin ürettiği argümandan alınmaz. Bilinen bir kısıt vardır:

> **Girdi korumasını bekleme (finding-12, çözüldü):** `AI:Realtime:WaitForInputGuard` (varsayılan `true`) açıkken oturum `create_response=false` ile kurulur; `RealtimeNativeService` tamamlanan transkripti `IInputGuard.Inspect`'ten geçirir ve ancak geçerse `RequestResponseAsync` (`response.create`) çağırır — reddedilen girdiye model hiç ses/araç çıktısı üretmez. Önceki yanıt sürerken istek ertelenir (yanıt bitince/iptal edilince gönderilir; aksi hâlde sağlayıcı "aktif yanıt var" hatası verir). Boş transkript (gürültü) yanıtlanmaz; transkripsiyon hatasında model yine yanıtlar (fail-open). Bedeli: yanıt transkripsiyon süresi kadar geç başlar. `false` önceki davranıştır: model konuşma biter bitmez yanıtlar, red gelince yanıt kesilir.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task RunAsync(IBrowserChannel channel, string sessionId, string? authenticatedCustomerId, CancellationToken ct)` | Native modda ses oturumunu, bağlantı kapanana kadar işler. |

## 7. Bağımlılıklar

`CustomerSupportBot.Application.Ports.Outbound.IBrowserChannel`.

## Bağlantılar

- [RealtimeNativeService](../../Services/Realtime/RealtimeNativeService.md)
- [OpenAiRealtimeClientAdapter](../../../CustomerSupportBot.Adapters.AI/Realtime/OpenAiRealtimeClientAdapter.md)
- [IInputGuard](IInputGuard.md)
