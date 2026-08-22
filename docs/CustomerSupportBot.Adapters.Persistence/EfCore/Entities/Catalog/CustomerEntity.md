# CustomerEntity

**Dosya:** `EfCore/Entities/Catalog/CustomerEntity.cs`
**Şema/Tablo:** `catalog.customers`
**Configuration:** [CustomerConfiguration](../../Configurations/Catalog/CustomerConfiguration.md)

## 1. Ne İşe Yarar

Bir müşteri kaydını (ad, e-posta, telefon) temsil eden EF Core varlığıdır; `catalog.customers`
tablosunun satır karşılığıdır.

## 2. Hangi Amaçla Kullanılır

Sipariş/şikayet sorgularında müşteri kimliğinin (`Id`, tip: `long`) doğrulanmasında ve müşteri
profili görüntülemede kullanılır. Not: bu tablo müşterinin **katalog kaydı**dır — chat'e giriş
için kullanılan kimlik doğrulama hesabı (`CustomerAccountEntity`/auth) ayrı bir kavramdır, bu
ikisi `LinkedCustomerId`/`CustomerId` alanlarıyla ilişkilendirilir (bkz.
[UserEntity](../Auth/UserEntity.md)).

## 3. Sorumlulukları

- **Üstlendiği:** Müşteri kimlik bilgilerini taşımak.
- **Üstlenmediği:** Kimlik doğrulama (şifre, token) — bu `Auth` şemasındaki entity'lerin işi.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `OrderEntity.CustomerId` ve `ComplaintEntity.CustomerId` bu entity'nin `Id`'sine mantıksal
  olarak referans verir (kodda navigasyon/FK constraint tanımlı değil — bkz. not aşağıda).
- Repository katmanı Domain'deki müşteri ile ilgili modellere map'ler.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`Id` tipi `long` — müşteri sayısının `int` sınırını aşabileceği varsayımıyla geniş tutulmuştur.
`Email`/`Phone` nullable — kayıt sırasında zorunlu değildir.

> 🐞 **Dikkat:** `OrderEntity`/`ComplaintEntity`'deki `CustomerId` alanları için Configuration
> dosyalarında `HasOne`/`HasForeignKey` ile veritabanı seviyesinde bir foreign key **tanımlı
> değil** — sadece index var. Yani veritabanı, var olmayan bir `CustomerId` ile sipariş
> oluşturulmasını engellemez; bu bütünlük kontrolü uygulama katmanında yapılmalıdır.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Id` | `long` | Birincil anahtar, `UseIdentityByDefaultColumn` ile otomatik üretilir. |
| `FullName` | `string` | Zorunlu, ≤128 karakter, index'li (`ix_customers_full_name`, unique değil). |
| `Email` | `string?` | Opsiyonel, ≤256 karakter. |
| `Phone` | `string?` | Opsiyonel, ≤32 karakter. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı, navigasyon property'si yok.

## Bağlantılar

- [CustomerConfiguration](../../Configurations/Catalog/CustomerConfiguration.md)
- [OrderEntity](OrderEntity.md), [ComplaintEntity](ComplaintEntity.md)
- [README](../README.md)
