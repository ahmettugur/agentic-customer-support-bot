# ICustomerProfileStore

**Kaynak:** `Ports/Outbound/Persistence/ICustomerProfileStore.cs`
**İmplementasyonlar:** [`InMemoryCustomerProfileStore`](../../../../CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryCustomerProfileStore.md), [`PostgresCustomerProfileStore`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/PostgresCustomerProfileStore.md)

## 1. Ne İşe Yarar

Müşteri başına kalıcı profil verisinin (`CustomerProfile`) depolanması için secondary port.

## 2. Hangi Amaçla Kullanılır

`CustomerProfileService` her etkileşimden sonra profili günceller (`GetOrCreate` + `Upsert`);
admin panelindeki müşteri listesi `List`/`Count` ile beslenir.

## 3. Sorumlulukları

- **Üstlendiği:** Profil CRUD'u ve listeleme.
- **Üstlenmediği:** Profilin nasıl sentezleneceği/yorumlanacağı — o
  [`ICustomerUnderstandingService`](../ICustomerUnderstandingService.md)'in işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`InMemoryCustomerProfileStore` (test) ve `PostgresCustomerProfileStore` (prod) implemente eder.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`GetOrCreate`, `Get`'ten ayrı bir metot olarak var: çağıranların çoğu "profil yoksa boş bir
tane oluştur" davranışını ister (her etkileşimde profil güncellenir), `Get` ise yalnızca var
olanı okumak isteyen nadir durumlar (örn. "profili var mı diye bak, yoksa hiç dokunma") içindir.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `CustomerProfile? Get(string customerId)` | Var olan profili döner, yoksa `null`. |
| `CustomerProfile GetOrCreate(string customerId)` | Var olan profili döner, yoksa boş bir tane oluşturup ekler. |
| `void Upsert(CustomerProfile profile)` | Profili tamamen replace eder. |
| `bool Delete(string customerId)` | Profili siler. |
| `IReadOnlyList<CustomerProfile> List(int take = 100)` | Tüm profilleri son etkileşime göre azalan sırada listeler (admin UI). |
| `int Count { get; }` | Toplam profil sayısı. |

## 7. Bağımlılıklar

Port arayüzü `CustomerSupportBot.Domain.Model.Memory.CustomerProfile`'a bağımlıdır.
