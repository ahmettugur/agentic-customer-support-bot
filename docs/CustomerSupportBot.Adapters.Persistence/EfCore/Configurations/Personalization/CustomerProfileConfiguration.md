# CustomerProfileConfiguration

**Dosya:** `EfCore/Configurations/Personalization/CustomerProfileConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<CustomerProfileEntity>`
**Entity:** [CustomerProfileEntity](../../Entities/Personalization/CustomerProfileEntity.md)

## 1. Ne İşe Yarar

`CustomerProfileEntity`'nin `personalization.customer_profiles` tablosuna eşlemesini
tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri (çoğu koleksiyon alanı `jsonb`); `LastInteractionAt` üzerinde azalan index.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Bağımsız — `CustomerId` gerçek FK ile bağlı değil.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`ix_customer_profiles_last_interaction_at` (azalan) — "en son etkileşimde bulunan müşteriler"
sorgusu (ör. admin panelinde aktif müşteri listesi, ya da profil temizleme/arşivleme
taramaları) için.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<CustomerProfileEntity>)` | `CustomerId` PK; `PreferredLanguage`/`PreferredTone`/`IntentFrequencyJson`(`jsonb`)/`ProductInterestsJson`(`jsonb`)/`RecentRatingsJson`(`jsonb`)/`TraitsJson`(`jsonb`)/`TotalSessions`/`TotalTurns`/`CreatedAt`/`LastInteractionAt` zorunlu; `Summary`/`AdminNote`/`LastConsolidatedAt` opsiyonel; `LastInteractionAt` azalan index. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [CustomerProfileEntity](../../Entities/Personalization/CustomerProfileEntity.md)
- [README](../README.md)
