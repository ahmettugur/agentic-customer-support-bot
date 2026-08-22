# LlmCallUsageConfiguration

**Dosya:** `EfCore/Configurations/Observability/LlmCallUsageConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<LlmCallUsageEntity>`
**Entity:** [LlmCallUsageEntity](../../Entities/Observability/LlmCallUsageEntity.md)

## 1. Ne İşe Yarar

`LlmCallUsageEntity`'nin `observability.llm_call_usage` tablosuna eşlemesini ve iki index'ini
tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri (`CostUsd` → `numeric(12,8)` yüksek hassasiyet); `CalledAt` üzerinde azalan
index, `Model` üzerinde arama index'i.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Bağımsız — başka bir Configuration'a FK ile bağlı değil.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Dosya başındaki yorum bunu açıkça belirtiyor: iki index "analytics sorgularında tarih/model
filtrelemesi için" var — admin panelindeki maliyet raporları hem zaman aralığına (`CalledAt`)
hem modele (`Model`) göre filtrelenip gruplanabilir olmalı.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<LlmCallUsageEntity>)` | `Id` PK (identity); `Model`/`Provider`/`InputTokens`/`OutputTokens`/`CostUsd`(`numeric(12,8)`)/`DurationMs`/`CalledAt` zorunlu; `CalledAt` azalan index, `Model` index. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [LlmCallUsageEntity](../../Entities/Observability/LlmCallUsageEntity.md)
- [README](../README.md)
