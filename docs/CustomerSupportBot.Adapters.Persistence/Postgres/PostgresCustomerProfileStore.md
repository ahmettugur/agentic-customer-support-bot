# PostgresCustomerProfileStore

**Dosya:** `Postgres/PostgresCustomerProfileStore.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`ICustomerProfileStore`](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ICustomerProfileStore.md)

## 1. Ne İşe Yarar

Müşterinin uzun ömürlü kişiselleştirme profilini (etkileşim sayısı, tercih edilen ton, LLM-türetilmiş çıkarımlar) `personalization.customer_profiles` tablosunda hibrit cache ile saklar.

## 2. Hangi Amaçla Kullanılır

`CustomerProfileContextProvider` her turda `GetOrCreate`/`Get` ile profili okuyup bağlama ekler; kişiselleştirme servisleri konuşma sonunda `Upsert` ile profili günceller (yeni çıkarımlar, güncellenen ton).

## 3. Sorumlulukları

- Üstlendiği: profil CRUD'u, cache senkronu.
- Üstlenmediği: profildeki çıkarımların (`InferredTrait`) LLM ile üretilmesi (bkz. `Services/Personalization`).

## 4. İlişkiler

- `ICustomerProfileStore` portunu implemente eder.
- `IMessageBusPort`, `IDbContextFactory<CustomerSupportDbContext>` enjekte edilir.
- [`CustomerProfileContextProvider`](../../CustomerSupportBot.Application/Services/Providers/CustomerProfileContextProvider.md) tarafından tüketilir.

## 5. Tasarım Yaklaşımı

`GetOrCreate`, profili yoksa oluşturup kalıcı hale getirir — çağıran tarafın "profil var mı" diye önce kontrol edip sonra oluşturması gerekmez, tek çağrıda hem okuma hem "ilk kez görülüyor" davranışı sağlanır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `CustomerProfile? Get(string customerId)` | Cache'ten, yoksa `null`. |
| `CustomerProfile GetOrCreate(string customerId)` | Yoksa yeni boş profil oluşturup kaydeder, döner. |
| `void Upsert(CustomerProfile profile)` | Cache + DB UPSERT + Redis yayını. |
| `bool Delete(string customerId)` | Profili siler (örn. KVKK/GDPR silme talebi). |
| `IReadOnlyList<CustomerProfile> List(int take = 100)` | Cache'ten sınırlı liste (admin görünümü). |
| `int Count { get; }` | Cache'teki toplam profil sayısı. |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `IMessageBusPort`
- `ILogger<PostgresCustomerProfileStore>`

## Bağlantılar

- [ICustomerProfileStore](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ICustomerProfileStore.md)
- [CustomerProfileContextProvider](../../CustomerSupportBot.Application/Services/Providers/CustomerProfileContextProvider.md)
