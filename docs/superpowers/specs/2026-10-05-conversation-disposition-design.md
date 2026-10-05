# Konuşma Kapanış Nedeni ve Etiketler — Tasarım

**Tarih:** 2026-10-05 · **Dal:** `feature/conversation-disposition`

## Amaç

Temsilci canlı sohbeti "Sohbeti Bitir" ile kapattığında sohbetin nasıl bittiği kaydedilmiyor. Hangi
konuların temsilciye kaldığı, kaçının gerçekten çözüldüğü, kaçının takip gerektirdiği bilinmiyor.
Sektörde buna "wrap-up / disposition code" denir.

Başarı ölçütleri:
- Temsilci sohbeti kapatırken bir **kapanış nedeni** seçer (ayarla zorunlu), isteğe bağlı **etiketler**
  ve kısa bir **not** ekler.
- Nedenler `appsettings`'ten ayarlanır.
- Analitik panelinde neden dağılımı ve en sık etiketler görünür.
- Kayıtlar kişisel veri kapsamındadır: KVKK silmesi ve dışa aktarma bunları da kapsar.

## Yapılandırma (`ConversationClosing`)

| Ayar | Varsayılan | Anlamı |
|---|---|---|
| `RequireReason` | `true` | Panelden kapatırken neden zorunlu. |
| `Reasons` | aşağıdaki liste | `[{ "Code": "...", "Label": "..." }]`. Boş bırakılırsa varsayılan liste. |

Varsayılan nedenler: `resolved` Çözüldü · `information_provided` Bilgi verildi · `follow_up_required`
Takip gerekiyor · `customer_unresponsive` Müşteri yanıt vermedi · `transferred` Başka birime yönlendirildi
· `other` Diğer.

Listeden bir neden kaldırılırsa eski kayıtlar silinmez; analitikte kod adıyla görünür.

## Kurallar

- Etiket: küçük harfe çevrilir (Türkçe `I`/`İ` → `i`), boşluklar `-` olur; harf/rakam/`-`/`_`; ≤ 30
  karakter; kayıt başına ≤ 10, tekrarlar atılır.
- Not ≤ 1000 karakter.
- Neden verilirse listede olmalı (değilse 400).

## Mimari

- **Domain:** `ConversationDisposition` (Id, SessionId, ReasonCode, Tags, Note, ClosedBy, ClosedAt).
- **Outbound `IConversationDispositionStore`:** ekle, oturumun kayıtları, neden sayıları, en sık
  etiketler; `ISessionDataEraser` (KVKK).
- **Inbound `IConversationClosingPort` / `ConversationClosingService`:**
  - `GetOptionsAsync` → zorunluluk, nedenler, önerilen etiketler (en sık 10).
  - `CloseAsync(sessionId, input, closedBy, agentId)` → doğrula → mevcut bırakma akışı
    (`IChatSessionPort.ReleaseAsync`: Bot moduna dönüş, eskalasyonların çözülmesi, yük) → kaydet.
    Doğrulama bırakmadan **önce** yapılır: geçersiz istek sohbeti kapatmaz.
- **Postgres:** `chat.conversation_dispositions` (etiketler `text[]`, `session_id` indeksli). Önbellek
  yok. En sık etiketler `unnest` ile veritabanında sayılır.
- **Uçlar:**
  - `GET /conversation-closing/options`, `POST /chat-sessions/{sid}/close` (Admin)
  - `GET /agent/conversation-closing/options`, `POST /agent/chat-sessions/{sid}/close` (Temsilci)
  - Mevcut `…/release` uçları değişmez (programatik bırakma; kapanış kaydı oluşturmaz).
  - Geçersiz 400, sohbet canlı değilse 404.
- **Analitik:** `AnalyticsDashboard.ClosingReasons` (`{code, label, count}`) ve `TopTags`
  (`{tag, count}`).
- **KVKK:** silme `ISessionDataEraser` ile; dışa aktarmada her oturumun kapanış kayıtları.
- **Arayüz:** "Sohbeti Bitir" bir pencere açar: neden (zorunluysa işaretli), etiketler (virgülle; önerilen
  etiketler tıklanarak eklenir), not. Analitik sekmesinde "Kapanış nedenleri" ve "Sık etiketler".

## Kapsam dışı

Bot'un kendi bitirdiği görüşmelere neden atama; etiketle arama (bir sonraki madde: konuşma arama);
etiket yönetimi ekranı.

## Test

- Servis: zorunlu neden, bilinmeyen neden, etiket normalleştirme/sınırlar, not sınırı, geçersizse
  sohbet bırakılmaz, canlı olmayan sohbet, `RequireReason=false` ile kayıtsız kapanış, önerilen etiketler.
- Seçenekler: yapılandırılmış liste varsayılanı değiştirir (liste birleşmez).
- Postgres (Testcontainers): ekleme, oturum kayıtları, neden sayıları, en sık etiketler, KVKK silmesi.
- Analitik: dağılım ve etiketler, kaldırılmış neden kod adıyla.
- API: rol kuralları, 400/404, agent ucu yükü düşürür.
- Tarayıcı: kapanış penceresi ve analitik kartları.
