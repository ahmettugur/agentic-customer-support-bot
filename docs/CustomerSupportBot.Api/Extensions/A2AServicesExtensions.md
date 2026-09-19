# A2AServicesExtensions

- **Dosya:** `Extensions/A2AServicesExtensions.cs`
- **Namespace:** `CustomerSupportBot.Api.Extensions`

## 1. Ne İşe Yarar

A2A (Agent2Agent) protokolüyle dışarı açılan 3 ajanı (`Product`, `Order`, `Complaint`) MAF'ın
hosting DI kaydına bağlayan Composition Root extension'ı. Endpoint yayınlama (routing) burada
DEĞİL, ayrı dosyada: `Endpoints/A2AEndpoints.cs`.

## 2. Hangi Amaçla Kullanılır

`Program.cs` açılışta `AddA2AAgents()` çağırır; bu, claims tabanlı oturum izolasyonunu kurar ve
3 ajanı ad üzerinden (`AddAIAgent(name, factory)`) DI'a kaydeder.

## 3. Sorumlulukları

- `services.AddHttpContextAccessor()` + `services.UseClaimsBasedAgentIsolation()` ile hosting
  store'larını (session, A2A task) çağıranın `NameIdentifier` claim'iyle bölümler — A2A wire
  `contextId`'si yalnızca konuşmayı sürdürme anahtarıdır, sahiplik kanıtı değildir.
- Ajan örneklerini **YENİDEN KURMAZ** — `AddAIAgent(name, factory)` her çağrıda
  [`A2AAgentCatalog`](../../CustomerSupportBot.Adapters.Agents/A2A/A2AAgentCatalog.md)'dan
  okur, çünkü salt-okunur tool bariyeri orada yaşar.
- Her ajanı `AddA2AServer(o => o.AgentRunMode = AgentRunMode.ReturnMessage)` ile kaydeder — yanıt
  her zaman tek bir `AgentMessage`'a toplanır, `AgentTask` olarak pollanan arka plan yürütmeye
  izin verilmez.
- **Üstlenmediği:** ajan örneklerinin kendisini kurmak (bkz. `A2AAgentCatalog`), endpoint
  routing'i (bkz. `Endpoints/A2AEndpoints.cs`), rate-limit policy tanımı (bkz.
  `ApplicationServicesExtensions`, `a2a` policy'si).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- [A2AAgentCatalog](../../CustomerSupportBot.Adapters.Agents/A2A/A2AAgentCatalog.md) — ajan
  örneklerinin gerçek kaynağı.
- [A2AAgentNames](../../CustomerSupportBot.Adapters.Agents/A2A/A2AAgentNames.md) — burada
  kaydedilen adların tek doğru kaynağı; endpoint tarafı da aynı adları kullanır.
- [A2A endpoint'leri](../Endpoints/A2A.md) — `AgentRunMode`/`AddA2AServer` burada kurulan
  ajanları HTTP'ye açar.
- `ApprovalGateService` — ajanlar DI'dan beslenirken dolaylı olarak buna bağımlıdır (tool
  onay akışı).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **Ad üzerinden geç bağlama bilinçli:** ajanlar DI'dan (`IChatClient`, prompt deposu,
  `ApprovalGateService`) beslendiği için servis kaydı anında henüz çözülemezler; `AddAIAgent(name,
  factory)` bu tavuk-yumurta sorununu çözer.
- **`AgentRunMode.ReturnMessage` bilinçli (SDK 1.22 öncesinde `DisallowBackground` adıyla aynı
  davranış):** arka plan (uzun süreli, `AgentTask` olarak pollanan) görevler dış çağıranın
  sunucuda iş biriktirmesine izin verirdi. Bu kanal salt-okunur sorgular içindir — her çağrı
  istek ömrü içinde başlar ve biter.
- **`MEAI001` yalnızca burada bastırılıyor** (proje genelinde değil): `AgentRunMode` henüz
  deneysel işaretli. Bastırmayı dar tutmak, aynı uyarının başka bir deneysel API kullanıldığında
  hâlâ görünmesini sağlar. Sürüm yükseltmelerinde bu pragma'nın hâlâ gerekli olup olmadığı
  kontrol edilmeli.
- **Sürüm hizası riski:** bu köprü paketi (`Microsoft.Agents.AI.Hosting.A2A.AspNetCore`), MAF
  çekirdeğiyle (`Adapters.Agents`) AYNI sürüm hattında tutulmalıdır — NuGet sürüm çakışmalarını
  build zamanında değil ancak ilgili kod yolu çalıştırıldığında `TypeLoadException`/
  `MissingMethodException` olarak açığa çıkarabilir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `static IServiceCollection AddA2AAgents(this IServiceCollection)` | Claims tabanlı izolasyonu kurar ve `Product`/`Order`/`Complaint` ajanlarını `ReturnMessage` modunda A2A sunucusuna bağlar. |

## 7. Bağımlılıklar

Extension metodu; constructor injection yok. Çalışma zamanında DI container'dan
`A2AAgentCatalog` çözülür (ajan factory delegate'leri içinde).

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [ApplicationServicesExtensions](ApplicationServicesExtensions.md)
- [../Endpoints/A2A](../Endpoints/A2A.md)
