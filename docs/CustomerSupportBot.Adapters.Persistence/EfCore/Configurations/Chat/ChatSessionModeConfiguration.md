# ChatSessionModeConfiguration

**Dosya:** `EfCore/Configurations/Chat/ChatSessionModeConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<ChatSessionModeEntity>`
**Entity:** [ChatSessionModeEntity](../../Entities/Chat/ChatSessionModeEntity.md)

## 1. Ne İşe Yarar

`ChatSessionModeEntity`'nin `chat.session_modes` tablosuna eşlemesini tanımlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri; `MessageCount` için varsayılan değer (`0`); "aktif devralımlar" sorgusunu
hızlandıran **partial index**.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`SessionId` (PK) [SessionEntity](../../Entities/Chat/SessionEntity.md) ile mantıksal olarak
eşleşir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`ix_session_modes_active` `HasFilter("mode = 'Human'")` ile tanımlanmış bir **partial index**
— bkz. [ChatSessionModeEntity](../../Entities/Chat/ChatSessionModeEntity.md) 🐞 notu: tablonun
büyük çoğunluğu `Bot` modunda olacağından, sadece `Human` satırlarını kapsayan bu index admin
panelindeki "aktif devralım listesi" sorgusunu hem küçük hem hızlı tutar.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<ChatSessionModeEntity>)` | `SessionId` PK; `Mode`/`HumanAgent` opsiyonel/≤16,≤128; `EnteredAt`/`LastActivityAt` opsiyonel `timestamptz`; `MessageCount` varsayılan `0`; `Mode` üzerinde partial index (`mode='Human'`). |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [ChatSessionModeEntity](../../Entities/Chat/ChatSessionModeEntity.md)
- [README](../README.md)
