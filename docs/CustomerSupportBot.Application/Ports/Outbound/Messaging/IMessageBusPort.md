# IMessageBusPort

**Kaynak:** `Ports/Outbound/Messaging/IMessageBusPort.cs`
**İmplementasyonlar:** [`RedisMessageBusAdapter`](../../../../CustomerSupportBot.Adapters.Redis/Messaging/RedisMessageBusAdapter.md) (prod, çoklu pod), [`InMemoryMessageBusAdapter`](../../../../CustomerSupportBot.Adapters.Persistence/InMemory/InMemoryMessageBusAdapter.md) (tek pod/test)

## 1. Ne İşe Yarar

Yatay ölçeklendirme için pod'lar arası pub/sub mesaj yolu. `Publish`/`Subscribe` ile kanal
bazlı JSON payload yayınlama/dinleme sağlar; `NodeId` bu node'un benzersiz kimliğidir.

## 2. Hangi Amaçla Kullanılır

Persistence adaptörleri (`PostgresSessionManager`, `PostgresApprovalQueue`,
`PostgresChatBridge`, `PostgresChatModeRegistry` vb.) bir pod'da yapılan değişikliği diğer
pod'lara bildirmek için bu port'u kullanır — böylece her pod kendi in-process cache'ini günceller.

## 3. Sorumlulukları

- **Üstlendiği:** Kanal bazlı yayın/dinleme ve node kimliği sağlamak.
- **Üstlenmediği:** Teslimat garantisi — pub/sub **en fazla bir kez** teslim eder; Redis
  restart'ı veya ağ kesintisi mesajı sessizce düşürebilir. Bu yüzden kalıcılığı önemli olan
  okumalar (bkz. `IApprovalQueue.GetPendingAsync`, `ISessionManager.ReloadAsync`) cache'e değil
  doğrudan veritabanına gitmelidir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- Prod: `RedisMessageBusAdapter` (Redis pub/sub).
- Tek-pod/test: `InMemoryMessageBusAdapter` — süreç içi, kayıp yaşanmaz (bu yüzden
  Redis-kaynaklı mesaj kaybı senaryolarını test etmek için bilinçli olarak İKİ AYRI
  `InMemoryMessageBusHub` örneği kullanılması gerekir, tek hub kaybı simüle etmez).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

`NodeId`'nin var olma nedeni: bir pod kendi yaptığı değişikliği pub/sub üzerinden tekrar
işlememelidir (zaten in-process state'i güncel) — abonelik handler'ları mesajın `NodeId`'sini
kendi `NodeId`'siyle karşılaştırıp kendi mesajlarını filtreler.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `void Publish(string channel, string jsonPayload)` | Belirtilen kanala JSON payload yayınlar. |
| `void Subscribe(string channel, Action<string> handler)` | Kanalı dinler; mesaj geldiğinde handler senkron çağrılır. |
| `string NodeId { get; }` | Bu node'un benzersiz kimliği. |

## 7. Bağımlılıklar

Yok — port arayüzü bağımlılıksızdır.
