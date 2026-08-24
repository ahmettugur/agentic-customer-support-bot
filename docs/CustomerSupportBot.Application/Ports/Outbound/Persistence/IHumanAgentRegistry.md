# IHumanAgentRegistry

**Kaynak:** `Ports/Outbound/Persistence/IHumanAgentRegistry.cs`
**İmplementasyonlar:** [`InMemoryHumanAgentRegistry`](../../../../CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryHumanAgentRegistry.md), [`PostgresHumanAgentRegistry`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/PostgresHumanAgentRegistry.md)

## 1. Ne İşe Yarar

Müşteri temsilcisi kaydı ve yük (load) yönetimi için secondary port.

## 2. Hangi Amaçla Kullanılır

Admin panelinde temsilci ekleme/düzenleme (`Create`/`Update`/`Delete`);
[`ISkillsBasedRouter`](../ISkillsBasedRouter.md) uygun temsilciyi seçerken `GetActive`'i okur;
bir eskalasyon atandığında/kapandığında `IncrementLoad`/`DecrementLoad` çağrılır.

## 3. Sorumlulukları

- **Üstlendiği:** Temsilci CRUD'u, aktif/pasif ayrımı, güncel yük sayacı.
- **Üstlenmediği:** Yönlendirme kararı — o [`ISkillsBasedRouter`](../ISkillsBasedRouter.md)'ın işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`GetLinkedUsersAsync`, Auth tablosundaki `Agent` rolü + `LinkedAgentId`'si olan kullanıcıları
döner ve registry ile merge edilerek tam temsilci listesi oluşturulur — yani bir temsilci hem
kendi `HumanAgent` kaydına hem de bir login hesabına (`UserInfo`) sahip olabilir, bu metot
ikisini birleştirir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`GetAll` (admin UI, pasifler dahil) ile `GetActive` (router, yalnızca müsait olanlar) ayrımı
bilinçlidir: admin bir temsilciyi geçici olarak pasife alabilmeli ama kaydı silmeden — router
bu durumda ona yeni eskalasyon atamamalıdır.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `IReadOnlyList<HumanAgent> GetAll()` | Tüm kayıtlı temsilciler (admin UI). |
| `IReadOnlyList<HumanAgent> GetActive()` | Yalnızca aktif temsilciler (router). |
| `HumanAgent? Get(string id)` | Tekil sorgu. |
| `HumanAgent Create(HumanAgent agent)` | Yeni temsilci oluşturur. |
| `HumanAgent? Update(string id, HumanAgentInput input)` | Kısmi güncelleme (null olmayan alanlar uygulanır). |
| `bool Delete(string id)` | Temsilciyi siler — bağlı eskalasyonlar dokunulmaz. |
| `bool IncrementLoad(string id)` | Atamadan sonra yükü +1 yapar, `LastAssignedAt`'i günceller. |
| `bool DecrementLoad(string id)` | Eskalasyon kapanınca yükü -1 yapar (min 0). |
| `Task<IReadOnlyList<HumanAgent>> GetLinkedUsersAsync(CancellationToken ct = default)` | Auth tablosundaki bağlı kullanıcıları döner (InMemory implementasyonu boş liste döner). |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.HumanAgent`/`HumanAgentInput`'a bağımlıdır.
