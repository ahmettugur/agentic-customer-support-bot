# PostgresHumanAgentRegistry

**Dosya:** `Postgres/PostgresHumanAgentRegistry.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`IHumanAgentRegistry`](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IHumanAgentRegistry.md)

## 1. Ne İşe Yarar

Canlı müşteri temsilcilerinin kayıt (skill, dil, aktiflik) ve anlık yük (`CurrentLoad`) bilgisini `hitl.human_agents` tablosunda tutan, `InMemoryHumanAgentRegistry`'nin DB-backed karşılığı olan hibrit cache + Redis pub/sub adaptörü.

## 2. Hangi Amaçla Kullanılır

Admin panelindeki temsilci yönetimi ekranı (`Create`/`Update`/`Delete`) ve eskalasyon yönlendirme mantığı (`GetActive`, `IncrementLoad`/`DecrementLoad` — bir temsilciye sohbet atanıp bırakıldığında) burayı kullanır. `GetLinkedUsersAsync`, admin/agent login hesabıyla (`UserEntity.LinkedAgentId`) bu kaydı eşleştirir.

## 3. Sorumlulukları

- Üstlendiği: temsilci CRUD'u, aktif yük sayacı, skill/dil etiketlerinin normalize edilmesi (küçük harf, trim, distinct), cross-pod cache senkronu.
- Üstlenmediği: hangi temsilcinin bir sohbete atanacağına dair yönlendirme MANTIĞI (bu Application/Adapters.Agents katmanındaki routing servisinin işi — bu sınıf yalnızca veriyi sağlar/günceller).

## 4. İlişkiler

- `IHumanAgentRegistry` portunu implemente eder.
- `IMessageBusPort` (Redis `csbot:humanagent:upserted`/`csbot:humanagent:deleted`), `IDbContextFactory<CustomerSupportDbContext>` enjekte edilir.
- `GetLinkedUsersAsync` ile `EfCore` `Users` tablosuna (`UserEntity.LinkedAgentId`) da sorgu atar.

## 5. Tasarım Yaklaşımı

`HumanAgent` kaydı küçük ve sınırlı boyutlu olduğu için (approval kuyruğundaki gibi delta/id-only yayın yerine) her değişiklikte **tam kayıt** Redis'e yayınlanır — sadeliği tercih eden bilinçli bir seçim.

> 🐞 **`EnsureHydrated` neden `_hydrated`'ı yalnızca try bloğu BAŞARILI olursa `true` yapıyor:** Aksi hâlde geçici bir DB hatası bu registry'yi process ömrü boyunca "hydrate edildi ama boş" bırakırdı ve bir sonraki çağrı DB'yi tekrar denemeden geçerdi — tüm temsilciler kalıcı olarak kaybolmuş görünürdü.

`IncrementLoad`/`DecrementLoad`, cache'teki `HumanAgent` nesnesi üzerinde `lock (a)` ile korunur (aynı temsilciye eşzamanlı iki atama/bırakma yarışını önler), ardından DB'ye `ExecuteUpdate` ile yalnızca yük alanları yazılır (tüm nesneyi ezmez).

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `IReadOnlyList<HumanAgent> GetAll()` | Cache'ten, aktifler önce, sonra isme göre sıralı. |
| `IReadOnlyList<HumanAgent> GetActive()` | Cache'ten yalnızca aktifler. |
| `HumanAgent? Get(string id)` | Cache'ten tek kayıt. |
| `HumanAgent Create(HumanAgent agent)` | Id yoksa üretir (8 karakter), skill/dil etiketlerini normalize eder, varsayılan `MaxConcurrentLoad=5`; DB + cache + Redis yayını. |
| `HumanAgent? Update(string id, HumanAgentInput input)` | Yalnızca `input`'ta dolu alanları kısmi günceller (patch semantiği). |
| `bool Delete(string id)` | Cache'ten kaldırır, DB'den siler, Redis'e `deleted` yayınlar. |
| `bool IncrementLoad(string id)` / `bool DecrementLoad(string id)` | `lock` korumalı yük sayacı güncellemesi + DB `ExecuteUpdate`. |
| `Task<IReadOnlyList<HumanAgent>> GetLinkedUsersAsync(CancellationToken ct)` | `Users` tablosundan `Role="Agent"` ve `LinkedAgentId != null` olan aktif hesapları `HumanAgent` şekline projekte eder. |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `IMessageBusPort`
- `ILogger<PostgresHumanAgentRegistry>`

## Bağlantılar

- [IHumanAgentRegistry](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/IHumanAgentRegistry.md)
- [PostgresApprovalQueue](PostgresApprovalQueue.md) — benzer hibrit cache deseni
