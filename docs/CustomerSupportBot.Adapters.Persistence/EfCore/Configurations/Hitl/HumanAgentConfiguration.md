# HumanAgentConfiguration

**Dosya:** `EfCore/Configurations/Hitl/HumanAgentConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<HumanAgentEntity>`
**Entity:** [HumanAgentEntity](../../Entities/Hitl/HumanAgentEntity.md)

## 1. Ne İşe Yarar

`HumanAgentEntity`'nin `hitl.human_agents` tablosuna eşlemesini tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri; `IsActive` üzerinde filtreli index.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Bağımsız — başka bir Configuration'a FK ile bağlı değil.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`ix_human_agents_active` filtreli index'i — bkz. [HumanAgentEntity](../../Entities/Hitl/HumanAgentEntity.md)
🐞 notu: routing sorguları her zaman aktif temsilciler arasından seçim yaptığı için sadece
`IsActive = true` satırları kapsayan küçük bir index yeterli ve daha hızlıdır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<HumanAgentEntity>)` | `Id` PK; `DisplayName`/`SkillsJson`(`jsonb`)/`LanguagesJson`(`jsonb`)/`IsActive`/`MaxConcurrentLoad`/`CurrentLoad`/`Priority`/`CreatedAt` zorunlu; `Email`/`LastAssignedAt` opsiyonel; `IsActive` üzerinde filtreli index. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [HumanAgentEntity](../../Entities/Hitl/HumanAgentEntity.md)
- [README](../README.md)
