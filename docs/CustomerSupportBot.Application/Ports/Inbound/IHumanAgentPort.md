# IHumanAgentPort ve RerouteResult

**Dosya:** `Ports/Inbound/IHumanAgentPort.cs`
**Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## 1. Ne işe yarar?

İnsan temsilci (human agent) kayıtlarının CRUD'u ve bir eskalasyonun başka bir agent'a yeniden yönlendirilmesi (reroute) için primary port.

## 2. Hangi amaçla kullanılır?

Admin panelinin "temsilciler" sayfası bu portla agent ekler/günceller/siler; bir eskalasyon üzerinde "başka birine ata" işlemi `RerouteEscalation` ile yapılır.

## 3. Sorumlulukları

- **Üstlendiği:** Agent CRUD, yük sayacı artırma/azaltma (`IncrementLoad`/`DecrementLoad`), reroute.
- **Üstlenmediği:** Eskalasyonun kendisinin CRUD'u — bu `IEscalationPort`'tadır; bu port sadece agent tarafını ve reroute'u kapsar.

## 4. Diğer katman/bileşenlerle ilişkileri

- Implementasyonu Application/Services altında; `HumanAgent` kayıtlarını hem statik konfigürasyondan hem de dinamik DB kayıtlarından "merge" ederek döner (`GetAllMergedAsync`).
- Admin endpoint'leri tüketicisidir.

## 5. Kullanılma nedeni ve tasarım yaklaşımı

`RerouteResult` ayrı bir kayıt olarak tanımlanmıştır çünkü reroute işlemi hem başarı (`Updated` dolu) hem hata (`Error` dolu) durumunu tek bir tipte taşımalıdır — `bool` dönüş tipi hatanın nedenini kaybederdi.

## 6. Tipler ve Üyeler

### `RerouteResult(EscalationRequest? Updated, string? Error)`
Reroute işleminin sonucu — başarılıysa `Updated` dolu, başarısızsa `Error` dolu.

### `IHumanAgentPort`

| Metot | Açıklama |
|---|---|
| `Task<IReadOnlyList<HumanAgent>> GetAllMergedAsync(CancellationToken ct = default)` | Tüm agent'ları (statik+dinamik birleştirilmiş) döner. |
| `HumanAgent? GetAgent(string id)` | Tek agent. |
| `HumanAgent CreateAgent(HumanAgent agent)` | Yeni agent oluşturur. |
| `HumanAgent? UpdateAgent(string id, HumanAgentInput input)` | Agent günceller; yoksa `null`. |
| `bool DeleteAgent(string id)` | Agent siler. |
| `bool IncrementLoad(string id)` | Agent'ın aktif yük sayacını 1 artırır. |
| `bool DecrementLoad(string id)` | Agent'ın aktif yük sayacını 1 azaltır. |
| `RerouteResult RerouteEscalation(string escalationId, string? agentId, string? reason)` | Bir eskalasyonu başka bir agent'a (veya `agentId=null` ise atamayı kaldırarak) yeniden yönlendirir. |

## 7. Bağımlılıklar

`CustomerSupportBot.Domain.Model.HumanAgent`, `EscalationRequest`.

## Bağlantılar

- [IEscalationPort](IEscalationPort.md)
