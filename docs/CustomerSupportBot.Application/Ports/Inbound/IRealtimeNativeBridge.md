# IRealtimeNativeBridge

**Dosya:** `Ports/Inbound/IRealtimeNativeBridge.cs`
**Tür:** `interface`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

Sesli sohbetin "native mod"u için primary port: `gpt-realtime` gibi bir model doğrudan kendisi konuşur (STT/TTS ayrı adım değildir) ve yalnızca okuma-amaçlı (read-only) tool'ları çağırabilir.

## 2. Hangi amaçla kullanılır?

WebSocket üzerinden gelen bir sesli oturumu, köprü moduna göre daha düşük gecikmeli ama daha kısıtlı (yalnızca okuma tool'ları) bir deneyimle işlemek için kullanılır.

## 3. Sorumlulukları

- **Üstlendiği:** Modelin kendi native ses üretimini ve sınırlı tool-çağırma yeteneğini yönetmek.
- **Üstlenmediği:** Yazma/yan-etkili işlemler (sipariş verme, iade vb.) — native modda model yalnızca okuma tool'larına erişir; onay gerektiren aksiyonlar bu modda tetiklenmez.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu `OpenAiRealtimeClientAdapter` (Adapters.AI/Realtime) üzerinden çalışır.
- `IBrowserChannel` (Outbound port) üzerinden tarayıcıyla WebSocket iletişimi kurar.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

`authenticatedCustomerId` parametresi burada da aynı güvenlik gerekçesiyle zorunludur (bkz. [IRealtimeBridge](IRealtimeBridge.md)). Native modun bilinen bir kısıtı vardır:

> 🐞 **Bilinen kısıt (finding-12, bilinçli olarak ertelendi):** `OpenAiRealtimeClientAdapter.ConfigureNativeSessionAsync` içinde `create_response=true` ayarı, modelin `IInputGuard.Inspect` tamamlanmadan ses/tool çıktısı üretmeye başlamasına izin verebilir. Doğru çözüm `create_response=false` + manuel `response.create` tetiklemesi gerektirir ama bu native ses modunun gecikme karakteristiğini değiştireceğinden bilinçli olarak kullanıcı kararına bırakılmıştır, henüz düzeltilmedi.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `Task RunAsync(IBrowserChannel channel, string sessionId, string? authenticatedCustomerId, CancellationToken ct)` | Native modda ses oturumunu, bağlantı kapanana kadar işler. |

## 7. Bağımlılıklar

`CustomerSupportBot.Application.Ports.Outbound.IBrowserChannel`.

## Bağlantılar

- [IRealtimeBridge](IRealtimeBridge.md) — köprü mod karşılığı.
- [OpenAiRealtimeClientAdapter](../../Adapters.AI/Realtime/OpenAiRealtimeClientAdapter.md)
- [IInputGuard](IInputGuard.md)
