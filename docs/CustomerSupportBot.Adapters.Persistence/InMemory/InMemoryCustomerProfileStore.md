# InMemoryCustomerProfileStore

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryCustomerProfileStore.cs`
- **Port:** `ICustomerProfileStore` (`CustomerSupportBot.Domain.Model.Memory`)
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.InMemory`

## 1. Ne İşe Yarar

Müşteri bazlı, uzun ömürlü kişiselleştirme profilini (`CustomerProfile`) `ConcurrentDictionary<string, CustomerProfile>` ile bellekte tutan basit anahtar-değer deposudur.

## 2. Hangi Amaçla Kullanıldığı

`PostgresCustomerProfileStore`'un tek-process/test karşılığı. LLM'in ürettiği müşteri çıkarımlarının (`CustomerUnderstanding`, `InferredTrait`) saklandığı yerdir.

## 3. Sorumlulukları

- `Get`/`GetOrCreate` — profil okuma, yoksa boş bir `CustomerProfile` oluşturma.
- `Upsert` — profili günceller; `LastInteractionAt` doldurulmamışsa `UtcNow` ile doldurur.
- `Delete`, `List` (en son etkileşime göre sıralı, `take` sınırlı), `Count`.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `PostgresCustomerProfileStore` (`../Postgres/PostgresCustomerProfileStore.md`) ile aynı arayüzü uygular.
- `CustomerProfile` modeli Domain katmanındadır (`../../CustomerSupportBot.Domain/Model/Memory/CustomerProfile.md`).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`StringComparer.OrdinalIgnoreCase` ile anahtarlanır — müşteri ID'lerinin büyük/küçük harf duyarlılığından kaynaklanan kayıp aramaları önlemek için. `GetOrCreate`/`Upsert` boş `customerId` için `ArgumentException` fırlatır — sessiz "hiçbir şey yapma" yerine erken hata, çağıran kodun hatayı fark etmesini sağlar.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Get(customerId)` | Profili döner, yoksa `null`; boş ID için de `null`. |
| `GetOrCreate(customerId)` | Yoksa yeni boş profil oluşturur; boş ID için `ArgumentException`. |
| `Upsert(profile)` | Ekler/günceller; `CustomerId` boşsa `ArgumentException`. |
| `Delete(customerId)` | Siler, başarıysa `true`. |
| `List(take)` | Son etkileşime göre azalan sırada, en fazla `take` kayıt (varsayılan 100). |
| `Count` | Toplam kayıt sayısı (property). |

## 7. Bağımlılıklar

- Yok — saf bellek içi implementasyon, dış servise bağımlı değil.
