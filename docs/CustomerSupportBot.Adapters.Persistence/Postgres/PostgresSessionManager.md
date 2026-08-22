# PostgresSessionManager

**Dosya:** `Postgres/PostgresSessionManager.cs`
**Namespace:** `CustomerSupportBot.Adapters.Persistence.Postgres`
**Port:** [`ISessionManager`](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ISessionManager.md)

## 1. Ne İşe Yarar

`AgentSession` (durum) ve konuşma geçmişini (`ConversationMessage` listesi) `chat.sessions`/`chat.messages` tablolarında saklayan, tamamen async, hibrit cache + Redis pub/sub tabanlı çok-pod uyumlu oturum yöneticisi.

## 2. Hangi Amaçla Kullanılır

Her chat turunda çağrılan merkezi sınıf: `GetOrCreateAsync`/`GetAsync` oturumu getirir, `AddExchangeAsync` bir kullanıcı+asistan mesaj çiftini kalıcı hale getirir, `ExtractAndUpdateStateAsync`/`MutateStateAsync` oturum durumunu (duygu, niyet, kimlik) günceller, `GetHistoryAsync` ajana verilecek konuşma bağlamını üretir.

## 3. Sorumlulukları

- Üstlendiği: oturum/mesaj CRUD'u, cache-DB tutarlılığı, cross-pod delta yayını, hydration race'lerine karşı koruma.
- Üstlenmediği: durum çıkarım KURALLARI (regex/duygu/niyet — bu [`SessionStateExtractor`](../../CustomerSupportBot.Domain/Services/SessionStateExtractor.md), Domain katmanında; bu sınıf yalnızca çağırır ve sonucu kalıcı hale getirir).

## 4. İlişkiler

- `ISessionManager` portunu implemente eder.
- `IAppDistributedLock` (`MutateStateAsync` için `session:{id}` kilidi), `IMessageBusPort` (Redis: `csbot:session:updated`, `csbot:session:history`, `csbot:session:cleared`), `IDbContextFactory<CustomerSupportDbContext>` enjekte edilir.
- [`SessionStateExtractor`](../../CustomerSupportBot.Domain/Services/SessionStateExtractor.md) (Domain) ile durum çıkarımı yapar.

## 5. Tasarım Yaklaşımı

> 🐞 **Hydration yarışı — `_hydratedSessions` neden `ConcurrentDictionary<string, byte>` (bayrak) değil `ConcurrentDictionary<string, Lazy<Task>>` (işin kendisi):** Eski bayrak deseninde, DB okuması BAŞLAMADAN bayrak konuyordu; aynı oturuma eşzamanlı gelen ikinci istek `TryAdd`'den `false` alıp hemen dönüyor ve DB okuması sürerken **boş** bir session ile devam ediyordu. Bu boş `State` üzerinden yapılan bir yazma, DB'deki gerçek durumu (müşteri kimliği dahil!) `{}` ile EZİYORDU — aynı oturuma art arda gelen iki isteğin (çift tıklama, iki sekme) bunu tetiklemesi yeterliydi. Düzeltme: kayıt artık "hydrate ediliyor" bayrağı değil, hydrate işinin `Lazy<Task>`'ı — ikinci çağıran AYNI Task'ı bekler, hydrate tamamlanmadan devam etmez. Hata durumunda girdi kaldırılır (aksi hâlde `Lazy` başarısız Task'ı sonsuza dek yeniden fırlatırdı).

> 🐞 **`ReconcileHistoryIfStaleAsync` — `GetHistoryAsync`'in stale pub/sub mesajını tespit edip kurtarması:** Geçmiş, pod'lar arasında Redis pub/sub ile yalnızca DELTA olarak yayılır (`PublishHistoryAppended`). Pub/sub en-fazla-bir-kez teslimattır; bir mesaj kaybolursa bu pod'un cache'i o andan itibaren KALICI olarak eksik kalır (hydrate yalnızca oturum ilk görüldüğünde bir kez çalışır). Eksik geçmiş, `GetHistoryAsync`'in ajana verdiği bağlamın kendisi olduğundan sessizce **ajanın konuşmayı yanlış anlamasına** yol açar — approval kuyruğundaki "bir liste eksik eleman içerir" türü zararsız bir kayıptan farklıdır. Çözüm: her `GetHistoryAsync` çağrısında ucuz bir `LongCountAsync` ile yerel/DB mesaj sayısı karşılaştırılır; yalnızca DB daha ileriyse (gerçekten kayıp varsa) `HydrateSessionAsync` ile TAM metin yeniden çekilir — pahalı tam-metin sorgusu her turda değil, yalnızca gerçekten gerektiğinde çalışır.

> 🐞 **Neden geçmiş delta, oturum durumu ise tam olarak yayınlanıyor:** `AgentSession.State` küçük ve sınırlı boyutta olduğundan her güncellemede tamamı gönderilir (`ChatModeRegistry`/`EscalationSink` ile aynı desen). Mesaj geçmişi ise turdan tura büyüyen bir liste olduğundan tamamını her seferinde göndermek israf olurdu; bunun yerine yalnızca yeni eklenen mesaj(lar) yayınlanır ve bu pod'da o oturum hiç görülmediyse delta yok sayılır — ilk gerçek erişimde zaten DB'den TAM geçmiş çekilecektir, yani delta kaybı kendi kendini onarır.

`ExtractAndUpdateStateCoreAsync`, `ConsecutiveNegativeTurns` gibi oku-değiştir-yaz alanları korumak için `lock (session)` kullanır — `GetOrCreateAsync`/`GetAsync` aynı `sessionId` için her zaman AYNI `AgentSession` referansını döndürdüğünden nesnenin kendisi güvenli bir kilit anahtarıdır.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Task<AgentSession> GetOrCreateAsync(string? sessionId, CancellationToken ct)` | Hydrate eder, yoksa yeni oturum oluşturup DB'ye yazar; eşzamanlı yarışta kazanan nesneyi döner. |
| `Task<AgentSession?> GetAsync(string sessionId, CancellationToken ct)` | Hydrate eder, cache'ten okur. |
| `Task UpdateAsync(AgentSession session, CancellationToken ct)` | Cache + DB UPSERT + Redis tam-state yayını. |
| `Task<IReadOnlyList<AgentSession>> GetAllAsync(CancellationToken ct)` | Tüm oturum metadata'sını (en fazla son 500) hydrate edip döner. |
| `Task ExtractAndUpdateStateAsync(string sessionId, string userMessage, string botResponse, CancellationToken ct)` | `SessionStateExtractor` ile durumu günceller ve kaydeder. |
| `Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct)` | Distributed lock altında keyfi bir state mutasyonunu uygular ve kaydeder. |
| `Task<List<ConversationMessage>> GetHistoryAsync(string sessionId, CancellationToken ct)` | Hydrate + stale-reconcile sonrası cache'ten geçmişin kopyasını döner. |
| `Task AddExchangeAsync(string sessionId, string userQuery, string assistantResponse, TurnSignals? signals, CancellationToken ct)` | Kullanıcı+asistan mesaj çiftini cache+DB'ye ekler, delta yayınlar, durumu günceller. |
| `Task AppendAssistantMessageAsync(string sessionId, string text, CancellationToken ct)` | Son mesaj boş bir asistan mesajıysa yerinde UPDATE eder (streaming placeholder doldurma), değilse ekler. |
| `Task AppendUserMessageAsync(string sessionId, string text, CancellationToken ct)` | Tek başına kullanıcı mesajı ekler (örn. canlı devralma sırasında). |
| `Task ClearSessionAsync(string sessionId, CancellationToken ct)` | Cache + DB'den siler (cascade ile mesajlar da gider), Redis'e yayınlar. |
| `Task<List<SessionInfo>> GetAllSessionsAsync(string? forCustomerId, CancellationToken ct)` | Oturum listesini (başlık = ilk kullanıcı mesajı) döner; `forCustomerId` verilirse yalnızca o müşteriye BAĞLI (anonim olanlar hariç) oturumlar. |
| `Task<AgentSession> ReloadAsync(string sessionId, CancellationToken ct)` | Cache'i atlayıp doğrudan DB'den taze okur ve cache'i onunla değiştirir. |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `IAppDistributedLock`
- `IMessageBusPort`
- `ILogger<PostgresSessionManager>`

## Bağlantılar

- [ISessionManager](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ISessionManager.md)
- [SessionStateExtractor](../../CustomerSupportBot.Domain/Services/SessionStateExtractor.md)
- [PostgresChatBridge](PostgresChatBridge.md) — benzer delta-yayın deseni
