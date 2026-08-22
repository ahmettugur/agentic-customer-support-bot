# InMemorySessionManager

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/InMemory/InMemorySessionManager.cs`
- **Port:** [`ISessionManager`](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ISessionManager.md)
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.InMemory`

## 1. Ne İşe Yarar

`ISessionManager` portunun bellek içi implementasyonudur. Oturum nesnesini (`AgentSession`, doğrulanmış kimlik + `SessionState` içerir) ve konuşma geçmişini (`ConversationMessage` listesi) tamamen `ConcurrentDictionary` içinde tutar; hiçbir veritabanı veya ağ çağrısı yapmaz.

## 2. Hangi Amaçla Kullanıldığı

Postgres/Redis altyapısı olmadan çalışması gereken senaryolarda (birim testleri, bazı entegrasyon test factory'leri) `PostgresSessionManager`'ın yerini alır. Production DI kaydında (`PersistenceAdapterServiceCollectionExtensions.AddPersistenceAdapters`) bu sınıf **kaydedilmez** — sadece test projelerinin kendi DI kurulumlarında kullanılır.

## 3. Sorumlulukları

- Oturum oluşturma/getirme (`GetOrCreateAsync`, `GetAsync`, `GetAllAsync`).
- Mesaj geçmişi ekleme (`AddExchangeAsync`, `AppendUserMessageAsync`, `AppendAssistantMessageAsync`).
- Tur sonunda `SessionStateExtractor.ExtractAndApply` çağrısıyla oturum durumunu (niyet, kimlikler, duygu) güncelleme.
- `MutateStateAsync` ile atomik olmayan okuma-değiştirme-yazma akışlarını `IAppDistributedLock` altında çalıştırma.

**Üstlenmediği:** Kalıcılık (process yeniden başlarsa tüm veri kaybolur), çoklu-pod senkronizasyonu (Redis pub/sub yok — zaten tek process varsayımıyla çalışır).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `PostgresSessionManager` (`../Postgres/HitlAndChat.md`) ile **aynı arayüzü** uygular; production'da onun yerini alan doğrudan karşılığıdır.
- `IAppDistributedLock`'u constructor injection ile alır — tek process'te bile `MutateStateAsync` sırasında race'i önlemek için kullanılır (gerçek dağıtık kilit gerekmese de arayüz aynı kalsın diye).
- `SessionStateExtractor` (Domain katmanı, saf C#) ile birlikte çalışır — state çıkarma mantığı Domain'de, saklama burada.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

Hexagonal mimaride bir portun birden fazla adaptörü olabilir; bu, Postgres bağımlılığı olmadan hızlı test/geliştirme akışı sağlar. `ExtractAndUpdateStateCoreAsync` içindeki `lock (session)` — `PostgresSessionManager`'daki aynı gerekçeyle konmuştur: aynı oturuma ait state mutasyonlarının (ör. eşzamanlı iki tool çağrısının dönüşü) birbirini ezmemesi için.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `ReloadAsync(sessionId, ct)` | Tek-process'te cache zaten tek kaynak olduğundan `GetOrCreateAsync`'e delege eder. |
| `GetOrCreateAsync(sessionId, ct)` | Oturum yoksa yeni `AgentSession` oluşturur (`sessionId` null ise yeni GUID üretir). |
| `GetAsync(sessionId, ct)` | Oturumu döner, yoksa `null`. |
| `UpdateAsync(session, ct)` | `LastActivity`'i günceller ve dictionary'e yazar. |
| `GetAllAsync(ct)` | Tüm oturumları döner. |
| `ExtractAndUpdateStateAsync(sessionId, userMessage, botResponse, ct)` | Tek bir kullanıcı/bot değişimi için state çıkarımı tetikler. |
| `MutateStateAsync(sessionId, mutator, ct)` | `IAppDistributedLock` altında keyfi bir state mutasyonu uygular. |
| `GetHistoryAsync(sessionId, ct)` | Konuşma geçmişinin bir kopyasını döner (`lock` altında). |
| `AddExchangeAsync(sessionId, userQuery, assistantResponse, signals, ct)` | Kullanıcı+asistan mesaj çiftini geçmişe ekler, ardından state çıkarımı yapar. Eklemeden ÖNCE geçmişin anlık görüntüsünü alır — `SessionStateExtractor`'ın "önceki tur" bağlamı doğru hesaplaması için. |
| `ClearSessionAsync(sessionId, ct)` | Oturumu ve geçmişini tamamen siler. |
| `AppendAssistantMessageAsync(sessionId, text, ct)` | Asistan mesajı ekler; son mesaj boş bir placeholder ise (Human mod aktarımı senaryosu) onu doldurur, aksi halde yeni mesaj ekler. |
| `AppendUserMessageAsync(sessionId, text, ct)` | Kullanıcı mesajı ekler. |
| `GetAllSessionsAsync(forCustomerId, ct)` | Oturum listesini özet (`SessionInfo`) olarak döner; `forCustomerId` verilirse sadece o müşteriye ait oturumlar filtrelenir. |

## 7. Bağımlılıklar

- `IAppDistributedLock` — `MutateStateAsync` sırasında kilit.
