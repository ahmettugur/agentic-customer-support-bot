# IReplanService

- **Kaynak:** `Services/Reasoning/IReplanService.cs`
- **Tür:** `public interface`
- **Namespace:** `CustomerSupportBot.Application.Services.Reasoning`

## 1. Ne İşe Yarar

Application katmanı içi (dış porta çıkmayan) bir strateji arayüzü — bir oturum için "yeniden
planlama" (replan) use case'ini tanımlar: son kullanıcı mesajını (veya admin notunu) yeniden
değerlendirmek, yeni bir bot yanıtı üretmek ve bunu canlı olarak müşteriye yayınlamak.

## 2. Hangi Amaçla Kullanılır

Admin panelinden bir oturum için "Yeniden Planla" tetiklendiğinde (`ForceReplanNextTurn`
bayrağı set edildiğinde), arka planda bu use case'i çalıştırmak — kullanıcı hiçbir şey
yazmadan, admin müdahalesiyle botun yeni bir yanıt üretmesini sağlamak.

## 3. Sorumlulukları

Tek metotlu bir arayüz: `ExecuteAsync`. Somut iş mantığı (`ReasonAsync`/`RunAsync` çağrıları,
`ChatBridge` yayını) implementasyonun ([`ReplanService`](ReplanService.md)) işidir — bu
arayüz yalnızca sözleşmeyi tanımlar.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- Tek implementasyonu: [`ReplanService`](ReplanService.md).
- Muhtemel tüketicisi: admin "Yeniden Planla" endpoint'i veya bunu tetikleyen bir arka plan
  işi (Api katmanı).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Ayrı bir arayüz olmasının sebebi test edilebilirlik ve gevşek bağlılık — çağıran taraf
(Api endpoint'i) `ReplanService`'in somut bağımlılıklarına (`ISessionManager`, `IChatBridge`,
`IAgentTeamPort`, `IReasoningPort`) değil, tek bir `ExecuteAsync` sözleşmesine bağımlı olur.
"Application-internal" olması (dış Ports/Inbound klasöründe değil, Services/Reasoning'de
tanımlanmış olması) bilinçli: bu bir dış port değil, iç bir strateji soyutlamasıdır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `ExecuteAsync(sessionId, ct)` | Arka planda son müşteri mesajı (veya admin notu) için reasoning + workflow koşturur; bot yanıtını `ChatBridge` üzerinden bot mesajı olarak yayınlar. |

## 7. Bağımlılıklar

Arayüz olduğu için kendi bağımlılığı yok.

## Bağlantılar

- [ReplanService.md](ReplanService.md) — tek implementasyon
