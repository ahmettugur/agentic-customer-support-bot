# IRealtimeBridge

**Dosya:** `Ports/Inbound/IRealtimeBridge.cs`
**Tür:** `interface`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

Sesli sohbetin "köprü modu" için primary port: gelen ses metne çevrilir (STT), metin normal bot pipeline'ından (reasoning + workflow) geçer, çıkan yanıt sese çevrilir (TTS). Realtime model burada yalnızca STT/TTS motoru gibi davranır, botun asıl karar mantığı normal agent pipeline'ıdır.

## 2. Hangi amaçla kullanılır?

WebSocket üzerinden gelen bir tarayıcı ses bağlantısını (`IBrowserChannel`) bu port işler ve tüm oturum boyunca (bağlantı kapanana kadar) çalışır.

## 3. Sorumlulukları

- **Üstlendiği:** Ses ⇄ metin dönüşümünü ve normal chat pipeline'ına köprülemeyi yönetmek.
- **Üstlenmediği:** Modelin kendisinin doğrudan konuşup tool çağırması — bu davranış `IRealtimeNativeBridge`'dedir (native mod), bu port için geçerli değildir.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu `RealtimeBridgeService` (Application/Services/Realtime).
- `IBrowserChannel` (Outbound port) üzerinden tarayıcıyla WebSocket iletişimi kurar.
- Api katmanındaki realtime WebSocket endpoint'i tüketicisidir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

Köprü ve native olmak üzere iki ayrı mod (iki ayrı arayüz) olmasının nedeni, farklı gecikme/güvenilirlik dengeleri sunmalarıdır: köprü modu botun tüm reasoning/onay/tool mantığından geçtiği için güvenlik ve tutarlılık açısından daha sağlam ama daha yüksek gecikmelidir; native mod daha doğal/hızlı konuşma sağlar ama modelin doğrudan (salt-okunur) tool çağırmasına izin verir.

`authenticatedCustomerId` parametresi güvenlik açısından kritiktir:

> 🔒 Login'li müşterinin JWT claim'inden gelen kimliği. Oturuma bir kez bağlanır ve sipariş tool'ları bunu kullanır — bu değer olmadan her sipariş sorgusu sahiplik kontrolüne takılıp "bulunamadı" döner.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task RunAsync(IBrowserChannel channel, string sessionId, string? authenticatedCustomerId, CancellationToken ct)` | Köprü modunda ses oturumunu, bağlantı kapanana kadar işler. |

## 7. Bağımlılıklar

`CustomerSupportBot.Application.Ports.Outbound.IBrowserChannel`.

## Bağlantılar

- [IRealtimeNativeBridge](IRealtimeNativeBridge.md) — native mod karşılığı.
- [OpenAiRealtimeClientAdapter](../../Adapters.AI/Realtime/OpenAiRealtimeClientAdapter.md)
