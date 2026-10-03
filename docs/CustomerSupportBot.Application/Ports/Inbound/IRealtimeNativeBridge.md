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

> 🐞 **Bilinen kısıt (finding-12, bilinçli olarak ertelendi):** `OpenAiRealtimeClientAdapter.ConfigureNativeSessionAsync` içinde `create_response=true` ayarı, modelin `IInputGuard.Inspect` tamamlanmadan ses/tool çıktısı üretmeye başlamasına izin verebilir. Doğru çözüm `create_response=false` + manuel `response.create` tetiklemesi gerektirir ama bu native ses modunun gecikme karakteristiğini değiştireceğinden bilinçli olarak kullanıcı kararına bırakılmıştır, henüz düzeltilmedi.

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
