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

> 🐞 **Bulunamayan oturum kalıcı olarak "yok" sayılıyordu:** `_hydratedSessions`, DB'de olmayan bir oturum için de "hydrate tamamlandı" girdisini tutuyordu. İlk mesajdan önce açılan bir olay akışı (`/chat/events/{id}`) oturumu bir pod'da "yok" diye mühürlüyor; oturum sonra başka bir pod'da oluştuğunda bu pod onu bir daha okumuyordu. `GetAsync` null dönüyor, `GetOrCreateAsync` ise boş state'li bir nesne üretip DB'deki gerçek state'i (müşteri sahipliği dahil) eziyordu. Düzeltme: hydrate işi artık `Lazy<Task<bool>>` (bulundu mu); bulunamadıysa girdi silinir. Bu pod'da oluşturulan oturum ise `CreatedLocally` işaretiyle "hazır" sayılır, yeniden hydrate edilip nesne referansı değiştirilmez. Silmeler anahtar+değer eşleşmesiyle yapılır, araya giren bir işaret korunur.

> 🧹 **Cache sınırı (LRU, yumuşak):** Eskiden cache'e giren hiçbir oturum ve mesaj geçmişi çıkmıyordu. Artık `maxCachedSessions` (varsayılan 2000) aşılınca en uzun süredir erişilmeyen oturumlar çıkarılır (%90'a inilir). Erişim zamanı `_lastAccess`'te tutulur; `LastActivity` kullanılmaz, çünkü o yalnızca yazmada ilerler. `minIdleBeforeEviction` (varsayılan 15 dk) içinde erişilmiş oturum sınır aşılsa bile **çıkarılmaz**: bir tur aynı nesneyi kilit ve durum taşıyıcısı olarak dakikalarca tutar. Çıkarılan oturum sonraki erişimde DB'den eksiksiz geri yüklenir. Yan etki: `GetAllAsync`/`GetAllSessionsAsync` cache'ten okuduğu için liste "en son erişilen ~N oturum" ile sınırlıdır; bu, zaten var olan "son 500" toplu yükleme sınırıyla aynı nitelikte bir davranıştır.

> 🐞 **`ReconcileHistoryIfStaleAsync` — `GetHistoryAsync`'in stale pub/sub mesajını tespit edip kurtarması:** Geçmiş, pod'lar arasında Redis pub/sub ile yalnızca DELTA olarak yayılır (`PublishHistoryAppended`). Pub/sub en-fazla-bir-kez teslimattır; bir mesaj kaybolursa bu pod'un cache'i o andan itibaren KALICI olarak eksik kalır (hydrate yalnızca oturum ilk görüldüğünde bir kez çalışır). Eksik geçmiş, `GetHistoryAsync`'in ajana verdiği bağlamın kendisi olduğundan sessizce **ajanın konuşmayı yanlış anlamasına** yol açar — approval kuyruğundaki "bir liste eksik eleman içerir" türü zararsız bir kayıptan farklıdır. Çözüm: her `GetHistoryAsync` çağrısında ucuz bir `LongCountAsync` ile yerel/DB mesaj sayısı karşılaştırılır; yalnızca DB daha ileriyse (gerçekten kayıp varsa) `HydrateSessionAsync` ile TAM metin yeniden çekilir — pahalı tam-metin sorgusu her turda değil, yalnızca gerçekten gerektiğinde çalışır.

> 🐞 **Neden geçmiş delta, oturum durumu ise tam olarak yayınlanıyor:** `AgentSession.State` küçük ve sınırlı boyutta olduğundan her güncellemede tamamı gönderilir (`ChatModeRegistry`/`EscalationSink` ile aynı desen). Mesaj geçmişi ise turdan tura büyüyen bir liste olduğundan tamamını her seferinde göndermek israf olurdu; bunun yerine yalnızca yeni eklenen mesaj(lar) yayınlanır ve bu pod'da o oturum hiç görülmediyse delta yok sayılır — ilk gerçek erişimde zaten DB'den TAM geçmiş çekilecektir, yani delta kaybı kendi kendini onarır.

`ExtractAndUpdateStateCoreAsync`, `ConsecutiveNegativeTurns` gibi oku-değiştir-yaz alanları korumak için `lock (session)` kullanır — `GetOrCreateAsync`/`GetAsync` aynı `sessionId` için her zaman AYNI `AgentSession` referansını döndürdüğünden nesnenin kendisi güvenli bir kilit anahtarıdır.

> 🐞 **Nesne kimliği bu yüzden HİÇBİR yolda değiştirilmez (`ApplySnapshot`).** `ReloadAsync`, yeniden hydrate (`HydrateSessionAsync`) ve uzak pod güncellemesi (`OnRemoteSessionUpdated`) eskiden cache'teki nesneyi YENİSİYLE değiştiriyordu. Elinde eski referansı tutan bir çağıran (tur sırasında `lock(session)` alan kod, `ChatPortService`'in tur başında aldığı oturum) artık cache'te olmayan bir nesneyi kilitliyor ve günceliyordu: yazdığı değişiklik kayboluyor, iki farklı nesne üzerindeki kilitler birbirini dışlamıyordu. Şimdi bu yollar mevcut nesnenin alanlarını **yerinde** günceller (`lock(existing)` altında); nesne yalnızca cache'te hiç yoksa eklenir.

> 🐞 **`SessionState.Revision` — eski anlık görüntüler yeniyi ezmez.** Her kalıcı yazmada (`UpdateAsync`) sayaç bir artar ve tam-state yayınıyla birlikte taşınır. Pub/sub sıra garantisi vermediği için geç gelen eski bir anlık görüntü (`Revision` < yereldeki) **yok sayılır**. Duvar saati yerine sayaç kullanılır: pod saatleri arasındaki kayma sıralamayı bozmasın. Yazma başarısız olursa sayaç geri alınır. DB tarafı aşağıdaki maddeye bakın.

> 🐞 **Eşzamanlı yazma — artık mutasyon tabanlı ve koşullu (`WriteStateAsync`).** Eskiden her yazıcı önbelleğindeki TÜM state'i yazıyordu ("son yazan kazanır"): pub/sub mesajını kaçırmış bayat bir pod, başka pod'un yaptığı değişikliği (ör. temsilci devraldığında işaretlenen `HumanInvolved`) tur sonunda siliyor, eşzamanlı artışlar kayboluyordu (testte 20 artıştan 5'i kalıyordu). Şimdi `MutateStateAsync` ve tur sonu (`AddExchangeAsync` → `SessionStateExtractor`) aynı yoldan geçer: `session:{id}` dağıtık kilidi → DB'deki state önbellekten yeniyse önce o alınır → mutasyon onun üzerine uygulanır → koşullu yazma (`StateJson` eşzamanlılık belirteci: `UPDATE … WHERE state = <okunan>`). Kilidi almayan bir yazıcı araya girerse koşul tutmaz; state zorla yeniden okunur ve mutasyon yeniden uygulanır (en fazla 5 deneme). Yeniden planlama, kimlik bağlama ve konuşma özeti de mutasyon olarak yazar; tüm state'i yazan `UpdateAsync` yalnızca mutasyonu bilinmeyen eski çağıranlar içindir. **Bilinen sınır:** DB daha yeniyse önbellekte yalnızca bellekte yapılmış, henüz yazılmamış değişiklik (`ConsumeForceReplanHint`'in temizlediği tek kullanımlık bayrak) DB state'iyle değişir — o ipucu bir tur daha uygulanabilir.

> 🐞 **Hydrate hatası artık "oturum yok" sayılmaz (`GetOrCreateAsync`).** Hydrate sonucu üç durumludur: bulundu / gerçekten yok / **hata**. Eskiden geçici bir DB hatası "yok" ile aynı ele alınıyor, `GetOrCreateAsync` boş state'li yeni bir oturum oluşturup DB'deki gerçek state'i (müşteri kimliği, sahiplik dahil) **eziyordu**. Şimdi hata durumunda `GetOrCreateAsync` `DomainException` fırlatır (bir sonraki çağrı yeniden dener); okuma yolları (`GetAsync` vb.) eskisi gibi hatayı yutup `null`/boş döner.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Task<AgentSession> GetOrCreateAsync(string? sessionId, CancellationToken ct)` | Hydrate eder, yoksa yeni oturum oluşturup DB'ye yazar; eşzamanlı yarışta kazanan nesneyi döner. Hydrate **hata** verdiyse oturum oluşturmaz, `DomainException` fırlatır. |
| `Task<AgentSession?> GetAsync(string sessionId, CancellationToken ct)` | Hydrate eder, cache'ten okur. Oturum yoksa cache'te iz bırakmaz. |
| `Task UpdateAsync(AgentSession session, CancellationToken ct)` | `State.Revision`'ı artırır; cache + DB UPSERT + Redis tam-state yayını. Yazma başarısızsa revizyon geri alınır. |
| `Task<IReadOnlyList<AgentSession>> GetAllAsync(CancellationToken ct)` | Tüm oturum metadata'sını (en fazla son 500) hydrate edip döner. |
| `Task ExtractAndUpdateStateAsync(string sessionId, string userMessage, string botResponse, CancellationToken ct)` | `SessionStateExtractor` ile durumu günceller ve kaydeder. |
| `Task MutateStateAsync(string sessionId, Action<SessionState> mutator, CancellationToken ct)` | Distributed lock altında keyfi bir state mutasyonunu uygular ve kaydeder. |
| `Task<List<ConversationMessage>> GetHistoryAsync(string sessionId, CancellationToken ct)` | Hydrate + stale-reconcile sonrası cache'ten geçmişin kopyasını döner. |
| `Task AddExchangeAsync(string sessionId, string userQuery, string assistantResponse, TurnSignals? signals, CancellationToken ct)` | Kullanıcı+asistan mesaj çiftini cache+DB'ye ekler, delta yayınlar, durumu günceller. |
| `Task AppendAssistantMessageAsync(string sessionId, string text, CancellationToken ct)` | Son mesaj boş bir asistan mesajıysa yerinde UPDATE eder (streaming placeholder doldurma), değilse ekler. |
| `Task AppendUserMessageAsync(string sessionId, string text, CancellationToken ct)` | Tek başına kullanıcı mesajı ekler (örn. canlı devralma sırasında). |
| `Task ClearSessionAsync(string sessionId, CancellationToken ct)` | Cache + DB'den siler (cascade ile mesajlar da gider), Redis'e yayınlar. |
| `Task<List<SessionInfo>> GetAllSessionsAsync(string? forCustomerId, CancellationToken ct)` | Oturum listesini (başlık = ilk kullanıcı mesajı) döner; `forCustomerId` verilirse yalnızca o müşteriye BAĞLI (anonim olanlar hariç) oturumlar. |
| `Task<AgentSession> ReloadAsync(string sessionId, CancellationToken ct)` | Cache'i atlayıp doğrudan DB'den taze okur ve **cache'teki aynı nesneyi** yerinde günceller (referans değişmez). |

## 7. Bağımlılıklar

- `IDbContextFactory<CustomerSupportDbContext>`
- `IAppDistributedLock`
- `IMessageBusPort`
- `ILogger<PostgresSessionManager>`

## Bağlantılar

- [ISessionManager](../../CustomerSupportBot.Application/Ports/Outbound/Persistence/ISessionManager.md)
- [SessionStateExtractor](../../CustomerSupportBot.Domain/Services/SessionStateExtractor.md)
- [PostgresChatBridge](PostgresChatBridge.md) — benzer delta-yayın deseni
