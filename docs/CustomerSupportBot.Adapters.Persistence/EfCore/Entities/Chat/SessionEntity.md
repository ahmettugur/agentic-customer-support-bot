# SessionEntity

**Dosya:** `EfCore/Entities/Chat/SessionEntity.cs`
**Şema/Tablo:** `chat.sessions`
**Configuration:** [SessionConfiguration](../../Configurations/Chat/SessionConfiguration.md)

## 1. Ne İşe Yarar

Domain katmanındaki `AgentSession`/`SessionState`'in veritabanı karşılığıdır. Bir sohbet
oturumunun kimliğini ve tüm durumunu (JSONB olarak serileştirilmiş) tutar.

## 2. Hangi Amaçla Kullanılır

`PostgresSessionManager` her turda oturumu bu tablodan okur/yazar; çoklu pod (multi-pod)
deployment'ta oturum durumunun tüm pod'lar arasında kalıcı ve tutarlı olmasını sağlar (Redis
pub/sub cache-sync bunun üzerine kuruludur — bkz. `ContextPipeline`/`PostgresSessionManager`
dokümanları).

## 3. Sorumlulukları

- **Üstlendiği:** Oturum kimliği, oluşturulma/son etkinlik zamanı, `SessionState`'in serileşmiş
  JSON hali.
- **Üstlenmediği:** `SessionState`'in alan alan yapısını bilmek — bu tamamen opak bir JSON
  string'dir, serialize/deserialize işini `SessionStateMapper` (Application/Adapters katmanı)
  yapar.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`MessageEntity.SessionId` bu entity'ye **gerçek foreign key** ile bağlıdır (`Cascade` silme —
bkz. [MessageConfiguration](../../Configurations/Chat/MessageConfiguration.md)). `PostgresSessionManager`
(Postgres klasörü) bu entity'yi okuyup Domain'deki `AgentSession`'a çevirir.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`StateJson` alanı `jsonb` kolon tipiyle tutulur (düz `text` değil) — Postgres'in JSONB'si
ikili/indexlenebilir bir format olduğundan hem depolama hem (gerekirse) JSON-içi sorgu
performansı açısından `text`'ten üstündür. `SessionState`'in onlarca alanı için ayrı ayrı kolon
açmak yerine tek bir JSONB kolon seçilmesi, oturum durumunun sık sık şekil değiştirmesi
(yeni alanlar eklenmesi) durumunda migration yükünü azaltır — pahasına, bu alan üzerinde
SQL seviyesinde tip-güvenli sorgu yazılamaz.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `SessionId` | `string` | Birincil anahtar, oturumun benzersiz kimliği. |
| `CreatedAt` | `DateTime` | Oturumun ilk oluşturulma zamanı. |
| `LastActivity` | `DateTime` | Son etkileşim zamanı — temizlik/timeout kararlarında kullanılır. |
| `StateJson` | `string` | `SessionState`'in serileşmiş JSON hali (`jsonb` kolon), varsayılan `"{}"`. |

## 7. Bağımlılıklar

Yok — saf veri sınıfı.

## Bağlantılar

- [SessionConfiguration](../../Configurations/Chat/SessionConfiguration.md)
- [MessageEntity](MessageEntity.md)
- [README](../README.md)
