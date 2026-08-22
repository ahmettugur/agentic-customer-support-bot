# SessionIdentityBinder

**Dosya:** `Services/Chat/SessionIdentityBinder.cs`
**Tür:** `public static class` (saf yardımcı sınıf)
**Namespace:** `CustomerSupportBot.Application.Services.Chat`

## 1. Ne İşe Yarar

Bir sohbet oturumunu JWT-doğrulanmış müşteri kimliğine bağlayan ve oturum sahipliğini
doğrulayan **tek nokta**. "Bu oturumu bu müşteri kullanabilir mi?" sorusunun cevaplandığı
tek kod yeri — yazılı chat, sesli köprü modu ve sesli native mod dahil üç farklı kanal
BURAYA çağrı yapar.

## 2. Hangi Amaçla Kullanılır

- [`ChatPortService`](ChatPortService.md), her turun başında `TryBindAsync` çağırır (tur
  zaten dağıtık kilit altında olduğu için ayrıca kilitlemeye gerek yoktur).
- Sesli (realtime) kanallar bağlantı kurulurken `BindAtomicallyAsync` çağırır (kilidi kendi
  kurar, çünkü tur kilidi altında değildirler).
- Olay akışına abone olma / okunmamış onay bildirimi çekme gibi **salt-okunur** uçlar
  `IsAccessibleAsync` çağırır.

## 3. Sorumlulukları

- **Üstlendiği:** Kimlik bağlama kararını (ilk bağlanan kazanır, sonra sabittir) ve sahiplik
  doğrulamasını (bağlıysa eşleşme kontrolü) merkezi bir yerde tutmak.
- **Üstlenmediği:** Kilit alma mekaniği (`IAppDistributedLock`'a devredilir),
  oturumun kalıcılığı (`ISessionManager`'a devredilir).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Statik, DI'a kayıtlı değil** — parametre olarak `ISessionManager`/`IAppDistributedLock` alır.
- [`ChatPortService.BindAuthenticatedCustomerAsync`](ChatPortService.md) tarafından `TryBindAsync` ile çağrılır.
- Realtime/sesli adaptörler (Adapters.AI katmanı) tarafından `BindAtomicallyAsync` ile çağrılır.
- Api katmanındaki olay akışı/onay bildirimi endpoint'leri tarafından `IsAccessibleAsync` ile çağrılır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **Neden ayrı bir statik yardımcı (kod tekrarı yerine merkezi kural):** Aynı bağlama
> mantığı üç kanalda gerekiyordu. Kural üç yere ayrı ayrı kopyalansaydı, biri güncellenip
> diğerleri eskirdi — nitekim gerçekte böyle oldu: sesli kanallar uzun süre bu bağı **hiç
> kurmuyordu**, sonucunda sipariş sorgulayan her tool boş `customerId=""` ile çalışıp sessizce
> "sipariş bulunamadı" dönüyordu. Kural artık tek yerde durduğu için üç kanal da aynı davranışı
> otomatik olarak paylaşır.

**`TryBindAsync` — bağlama mantığı:** `authenticatedCustomerId` boşsa dokunmadan `true` döner
(anonim akışların bozulmaması için — ama bu proje artık login zorunlu olduğundan bu dal
fiilen sadece iç/test senaryolarında devreye girer). Oturum henüz kimseye bağlı değilse
bağlanır ve kalıcılığa yazılır. **Zaten** bağlıysa, gelen kimlikle eşleşiyor mu diye bakılır —
eşleşmiyorsa `false` döner ve çağıran taraf (`ChatPortService`) bunu
`UnauthorizedSessionAccessException` olarak yükseltir.

> 🐞 **Neden bu kontrol olmadan güvenlik açığı oluşurdu:** Kontrol olmasaydı, bir kullanıcı
> başkasının `sessionId`'sini elde edip göndererek o oturumun (yanlış) kimliğiyle çalışan
> tool'lara (sipariş geçmişi, iptal, iade) erişebilirdi — oturum zaten bağlı olduğu için
> bağlama sessizce atlanır ve tool'lar oturumdaki (başkasına ait) kimlikle koşardı.

**`TurnLockKey` — neden `session:{id}` değil:** O anahtar oturum durumu yazan alt işlemler
tarafından (`MutateStateAsync`, `AddExchangeAsync`) zaten içeriden alınıyor; Redis dağıtık
kilidi yeniden girişli (reentrant) olmadığından, aynı anahtarı dışarıda (tur kilidi için) de
kullanmak kendi kendine kilitlenme (deadlock) üretirdi. `session-turn:{id}` ayrı bir anahtar
kullanır.

**`BindAtomicallyAsync` neden var, `ChatPortService` neden onu çağırmıyor:** Yazılı chat
turu zaten kilidi TUR BOYUNCA tutuyor ve bağlamayı o kilidin altında (`TryBindAsync` ile
doğrudan) yapıyor — aynı `TurnLockKey` anahtarını paylaştıkları için ikisi birbirini zaten
dışlar, ayrıca kilitlemeye gerek yok. Realtime kanalları ise kilidi bağlantı ömrü boyunca
(dakikalarca) TUTAMAZ — bu yüzden yalnızca bağlama ANINI kilitleyen ayrı bir metot
(`BindAtomicallyAsync`) sunulur: kilidi alır, oturumu tazeler, bağlar, kilidi bırakır.

**`IsAccessibleAsync` neden `GetOrCreateAsync` DEĞİL `GetAsync` kullanır:** Bu metot salt-okunur
uçlar (abonelik, bildirim listeleme) içindir ve oturumu **oluşturmaz, değiştirmez**.
`GetOrCreateAsync` çağrılsaydı, rastgele/yanlış bir `sessionId` verilmesi durumunda BOŞ bir
oturum üretilirdi — okuma amaçlı bir uç için istenmeyen bir yan etki.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `TurnLockKey(string sessionId): string` | Tur/bağlantı kilidi için kullanılacak anahtar (`session-turn:{id}`); `session:{id}`'den kasıtlı olarak farklıdır. |
| `TryBindAsync(AgentSession session, string? authenticatedCustomerId, ISessionManager sessions, CancellationToken ct = default): Task<bool>` | Zaten kilit altındaki bir çağrı için bağlama; kilitlemeyi kendisi yapmaz. |
| `BindAtomicallyAsync(string sessionId, string? authenticatedCustomerId, ISessionManager sessions, IAppDistributedLock locks, CancellationToken ct = default): Task<AgentSession?>` | Kilidi kendi alır, oturumu tazeler, bağlar; başarısızsa `null`. |
| `IsAccessibleAsync(string? sessionId, string? authenticatedCustomerId, ISessionManager sessions, CancellationToken ct = default): Task<bool>` | Salt-okunur sahiplik kontrolü; oturum yoksa/bağlanmamışsa/bu müşteriye aitse `true`. |

## 7. Bağımlılıklar

Yok (statik sınıf) — ihtiyaç duyduğu servisleri (`ISessionManager`, `IAppDistributedLock`) parametre olarak alır, kendi inject etmez.

## Bağlantılar

- [ChatPortService.md](ChatPortService.md) — tur kilidi altında `TryBindAsync`'i çağıran taraf
