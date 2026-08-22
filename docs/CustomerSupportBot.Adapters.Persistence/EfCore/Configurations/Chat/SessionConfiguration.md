# SessionConfiguration

**Dosya:** `EfCore/Configurations/Chat/SessionConfiguration.cs`
**Uyguladığı Arayüz:** `IEntityTypeConfiguration<SessionEntity>`
**Entity:** [SessionEntity](../../Entities/Chat/SessionEntity.md)

## 1. Ne İşe Yarar

`SessionEntity`'nin `chat.sessions` tablosuna eşlemesini tanımlar; en önemlisi `StateJson`
alanının PostgreSQL `jsonb` kolon tipiyle eşlenmesini sağlar.

## 2. Hangi Amaçla Kullanılır

`CustomerSupportDbContext.OnModelCreating` tarafından otomatik uygulanır.

## 3. Sorumlulukları

Kolon eşlemeleri; `LastActivity` üzerinde **azalan (descending)** index.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`MessageConfiguration` bu tabloya `Cascade` foreign key ile bağlıdır (bkz.
[MessageConfiguration](MessageConfiguration.md)).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`ix_sessions_last_activity` **azalan sırada** tanımlanmıştır (`IsDescending()`) — "en son
etkinlik gösteren oturumlar" sorgusu (ör. temizlik/timeout taraması, admin panelinde son
aktif oturumlar) genelde `ORDER BY last_activity DESC` şeklinde çalışır; index'in sıralamayla
aynı yönde olması Postgres'in ek bir sort adımına gerek duymadan index'i doğrudan kullanmasını
sağlar.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Configure(EntityTypeBuilder<SessionEntity>)` | `SessionId` PK; `CreatedAt`/`LastActivity` zorunlu `timestamptz`; `StateJson` → `jsonb` kolon, zorunlu; `LastActivity` üzerinde azalan index. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [SessionEntity](../../Entities/Chat/SessionEntity.md)
- [MessageConfiguration](MessageConfiguration.md)
- [README](../README.md)
