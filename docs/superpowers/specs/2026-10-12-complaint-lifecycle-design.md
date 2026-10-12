# Şikayet Yaşam Döngüsü — Tasarım

**Tarih:** 2026-10-12 · **Dal:** `feat/complaint-lifecycle`

## Amaç

Şikayetler bugün onaydan sonra `Beklemede` durumunda oluşturuluyor ve **hiç değişmiyor**:
`IComplaintRepository` yalnızca `Create / Get / GetByOrder / GetByCustomer` sunuyor, `İncelemede` ve
`Çözüldü` sabitleri yalnızca seed verisinde kullanılıyor, panelde şikayet ekranı yok. Müşteri "şikayetim ne
oldu" diye sorduğunda bot her zaman "Beklemede" diyor.

Hedef: şikayetin üstlenilmesi, çözülmesi/reddedilmesi ve gerekirse yeniden açılması; müşterinin bundan
e-postayla ve bottan haberdar olması; geciken şikayetlerin SLA ekranında görünmesi.

## Kararlar (kullanıcıyla)

- **Sorumlu:** admin ve temsilci. Temsilci atanmamış şikayeti üstlenir; admin herhangi bir şikayeti atar /
  yeniden atar (eskalasyonlardaki "üstlen" kalıbı).
- **Durumlar:** `Beklemede → İncelemede → Çözüldü | Reddedildi`; kapanmış şikayet yeniden açılabilir
  (`İncelemede`'ye döner).
- **Bildirim:** incelemeye alınınca ve kapanınca e-posta; bot sorulunca güncel durumu ve açıklamayı söyler.
- **Yaklaşım:** mevcut `catalog.complaints` genişletilir + olay geçmişi tablosu (eskalasyon kuyruğuna
  bindirme ve geçmişsiz sütun ekleme reddedildi).

## Kapsam dışı

Müşteriden ek bilgi isteme durumu (SLA durdurma), şikayet kategorisi/önceliği, panelden elle şikayet açma,
SLA ihlalinde otomatik yeniden atama, şikayet metninin düzenlenmesi.

## Veri modeli

### `catalog.complaints` (+ migration `AddComplaintLifecycle`)

| Sütun | Tip | Anlamı |
|---|---|---|
| `created_at` | `timestamptz not null` | Oluşturulma. Mevcut kayıtlar migration anını alır. SLA buradan sayılır. |
| `updated_at` | `timestamptz not null` | Son değişiklik (durum, atama). Mevcut kayıtlar = `created_at`. |
| `assigned_agent_id` | `text null` | Üstlenen temsilcinin kimliği (`HumanAgent.Id`, eskalasyondaki `AssignedTo` ile aynı tür). |
| `resolution` | `text null` (≤ 2000) | `Çözüldü`/`Reddedildi` için müşteriye görünen açıklama. Yeniden açılınca korunur, yeni kapanışta üzerine yazılır. |
| `approval_id` | `text null` | Şikayeti doğuran onay kaydı. Fotoğraflar `attachments.approval_id` üzerinden bulunur (kopyalanmaz). |
| `version` | `int not null default 0` | Her geçişte +1; e-posta defteri anahtarında kullanılır (yeniden açılıp tekrar kapanan şikayet yeni e-posta alır). |

İndeks: `(status, created_at)` — liste ve SLA taraması.

### `catalog.complaint_events` (yeni)

`id bigserial`, `complaint_code bigint` (FK, cascade), `at timestamptz`, `actor text` (kullanıcı adı ya da
`system`), `kind text` (`created`, `claimed`, `assigned`, `resolved`, `rejected`, `reopened`, `note`),
`from_status text null`, `to_status text null`, `assigned_agent_id text null`, `note text null` (≤ 2000, **iç
not — müşteriye gitmez**). İndeks: `(complaint_code, at)`.

Onay sonrası oluşturmada bir `created` olayı yazılır (`actor = system`). Migration öncesi kayıtların geçmişi
boş kalır; panel bunu "geçmiş kaydı yok" olarak gösterir.

### Domain

- `ComplaintInfo` + `CreatedAt`, `UpdatedAt`, `AssignedAgentId`, `Resolution`, `ApprovalId`, `Version`.
- `WellKnown.ComplaintStatuses` + `Rejected = "Reddedildi"`; `IsClosed(status)` yardımcı.
- `ComplaintEvent` (yukarıdaki alanlar).
- **`ComplaintTransitions`** (saf, `Domain/Services`): `Check(action, currentStatus, actorRole,
  actorAgentId, assignedAgentId, input) → ComplaintTransitionResult` (izin + hedef durum ya da hata kodu:
  `invalid_transition`, `forbidden`, `resolution_required`, `note_required`, `assignee_required`).

## Durum geçişleri

| Eylem | Nereden | Nereye | Kim | Zorunlu girdi |
|---|---|---|---|---|
| `claim` | Beklemede | İncelemede | Temsilci (kendine) | — |
| `assign` | Beklemede | İncelemede | Admin | `agentId` (aktif temsilci) |
| `assign` | İncelemede | İncelemede | Admin | `agentId` (yeniden atama) |
| `resolve` | İncelemede | Çözüldü | Atanan temsilci ya da admin | `resolution` |
| `reject` | İncelemede | Reddedildi | Atanan temsilci ya da admin | `resolution` |
| `reopen` | Çözüldü / Reddedildi | İncelemede | Admin ya da şikayeti kapatan temsilci | `note` (gerekçe); atama korunur |
| `note` | herhangi | (değişmez) | Admin ya da atanan temsilci | `note` |

- Başkasına atanmış şikayette temsilcinin `resolve/reject/note` denemesi **403**.
- Her geçiş **koşullu yazılır**: `UPDATE … SET … , version = version + 1 WHERE code = @c AND status =
  @expected AND version = @expectedVersion`. Etkilenen satır 0 ise güncel kayıt okunur ve **409** döner
  (yarış: iki temsilcinin aynı anda üstlenmesi / kapatması). Durum güncellemesi ve olay satırı **tek
  transaction**'dadır.
- Geçersiz geçiş **409** (`currentStatus` yanıtta), eksik girdi **400**, bulunamadı **404**.

## Uygulama katmanı

- **Outbound `IComplaintRepository`** (Postgres; InMemory uygulaması testler için):
  - `Create(ComplaintInfo, approvalId?)` — `created` olayıyla.
  - `ListAsync(ComplaintQuery{Status?, AssignedAgentId?, UnassignedOnly, Page, PageSize}) →
    (items, total)`; `CountOpenAsync() → (pending, inProgress, assignedTo(agentId))`.
  - `GetEventsAsync(code)`.
  - `TryTransitionAsync(code, expectedStatus, expectedVersion, newStatus, assignedAgentId, resolution,
    ComplaintEvent) → ComplaintTransitionOutcome(Updated | Conflict | NotFound, ComplaintInfo?)`.
  - `AddEventAsync(code, ComplaintEvent)` (yalnız not).
- **Inbound `IComplaintManagementPort` / `ComplaintManagementService`:** kimliği/rolü alır,
  `ComplaintTransitions` ile doğrular, depoyu çağırır, `Updated` ise bildirimi tetikler. Ayrıntı için
  fotoğrafları `IAttachmentStore` üzerinden `approval_id` ile toplar.
- **`ApprovalExecutionRouter` → `ComplaintRegistrationTool(..., approvalId)`:** onay kimliği yeni şikayete
  yazılır. Tool'un sohbet içi doğrudan çağrısı (onay kapısı dışında, yalnızca testlerde) `null` geçer.
- **Bot — `complaint_status_tool`:** mesaj = numara, sipariş, durum, kapandıysa açıklama, son güncelleme
  tarihi. **İç notlar, olay geçmişi ve temsilci adı bota gitmez.** Sesli mod aynı tool'u kullandığı için
  ayrıca değişiklik gerekmez.

## Bildirim

- **`ComplaintStatusEmailService`** (`OrderStatusEmailService` kalıbı): iki an —
  `İncelemede`'ye ilk geçiş (`claim`/`assign`) ve kapanış (`resolve`/`reject`). Yeniden atama ve not e-posta
  göndermez; yeniden açma "tekrar incelemede" e-postası gönderir.
- Ayar: `Email:Notifications:ComplaintInReview` ve `ComplaintClosed` (varsayılan `true`).
- Çok pod / tekrar: `INotificationLedger` anahtarı `complaint:{code}:{olay}:{version}`. Gönderilemezse talep
  bırakılır, hata loglanır; **durum geçişi yine başarılıdır**.
- İçerik: Türkçe düz metin + HTML, kullanıcı metni (şikayet, açıklama) escape edilir; e-posta müşteri
  kaydından (`ICustomerRepository.GetEmailAsync`), adres yoksa atlanır.

## API

Admin uçları `adminScope`'ta, temsilci uçları `agentScope`'ta (`/agent` öneki) — mevcut yetki ve hız sınırı.

| Uç | Rol | Açıklama |
|---|---|---|
| `GET /complaints?status=&assigned=&page=&pageSize=` | Admin | Liste; açıklar en eskiden yeniye, kapalılar yeniden eskiye. `assigned`: `unassigned` ya da temsilci kimliği. |
| `GET /agent/complaints?status=&mine=` | Temsilci | Atanmamış + kendisine atananlar (başkasına atananlar gizli). |
| `GET /complaints/counts`, `GET /agent/complaints/counts` | Admin / Temsilci | Rozet: bekleyen, incelemede, (temsilci için) bana atanan. |
| `GET /complaints/{code}`, `GET /agent/complaints/{code}` | İkisi | Ayrıntı: metin, sipariş özeti, müşteri no, fotoğraf kimlikleri, olaylar. Temsilci başkasına atanmışı da **görebilir** (salt-okunur), değiştiremez. |
| `POST /agent/complaints/{code}/claim` | Temsilci | Üstlen. |
| `POST /complaints/{code}/assign` `{agentId}` | Admin | Ata / yeniden ata. |
| `POST /(agent/)complaints/{code}/resolve` `{resolution, note?}` | İkisi | Çözüldü. |
| `POST /(agent/)complaints/{code}/reject` `{resolution, note?}` | İkisi | Reddedildi. |
| `POST /(agent/)complaints/{code}/reopen` `{note}` | İkisi | Yeniden aç. |
| `POST /(agent/)complaints/{code}/notes` `{note}` | İkisi | İç not. |

Her geçiş isteği `expectedVersion` alabilir (panel gönderir); verilmezse sunucu okuduğu sürümü kullanır.
Fotoğraflar mevcut `GET /(agent/)attachments/{id}` ucuyla açılır.

## Panel — "⚠️ Şikayetler" sekmesi

- Admin ve temsilcide görünür. Rozet: bekleyen (admin) / bekleyen + bana atanan (temsilci).
- **Liste:** filtre (Benim / Atanmamış / Tümü + durum), satırda şikayet no, sipariş no, yaş, atanan, SLA rengi
  (uyarı sarı, ihlal kırmızı). Sayfalı.
- **Ayrıntı:** şikayet metni, sipariş özeti, fotoğraflar (`ApprovalPhotos` bileşeni), müşteriye gönderilen
  açıklama, zaman çizelgesi (olaylar, iç notlar dahil). Duruma ve role göre düğmeler: Üstlen / Ata (temsilci
  seçici) / Çözüldü / Reddet / Yeniden aç / Not ekle. Açıklama ve gerekçe alanları zorunluysa boş gönderilemez.
- 409 gelirse kayıt yeniden yüklenir ve "başka biri güncelledi" uyarısı gösterilir.
- **İstek bütçesi:** rozet her otomatik yenilemede (15 sn) tek bir `counts` çağrısı ekler (dakikada 4);
  personel kotası kullanıcı başına dakikada 300 (`RateLimiting:StaffPerMinute`). Liste yalnızca sekme
  açıkken yenilenir.

## SLA

- `Sla:Complaints`: `PickupWarnAfterSeconds` (varsayılan 4 saat = 14400), `PickupBreachAfterSeconds` (24
  saat = 86400) — `Beklemede` kalma; `ResolveWarnAfterSeconds` (48 saat = 172800),
  `ResolveBreachAfterSeconds` (72 saat = 259200) — açık kalma toplamı (`created_at`'tan).
- SLA Guardian taramasına üçüncü kol: açık şikayetler `(status, created_at)` indeksiyle okunur; eşik aşımı
  mevcut SLA olay deposuna (`Kind = complaint_pickup | complaint_resolve`, `TargetId = code`) **bir kez**
  yazılır (`SlaPolicyEvaluator` kalıbı: `ISlaEventSink.LastEmittedAt(kind, code, severity)` boşsa).
  Değerlendirme saf bir `EvaluateComplaint` fonksiyonudur. Yeniden açılan şikayet için aynı tür olay tekrar
  yazılmaz (kabul edilen sınır). Otomatik işlem yok.
- `/sla/status` açık şikayet sayısı ve en eski yaşı da döner; `/sla` ekranı gösterir.

## KVKK

- Dışa aktarma: şikayet başına durum, müşteriye görünen açıklama, oluşturma/güncelleme zamanı ve durum
  geçişleri (zaman + durum). **İç notlar ve temsilci kimlikleri dışa aktarılmaz.**
- Silme: bugünkü davranış korunur (şikayet iş kaydı olarak kalır); fotoğraflar mevcut saklama (90 gün)
  kuralıyla silinir — panel silinmiş fotoğrafı "süresi doldu" olarak gösterir.

## Hata ve sınır durumları

- Pasif / silinmiş temsilciye atama → 400. Atanan temsilci sonradan silinirse şikayet atanmış görünür;
  admin yeniden atar.
- `resolution` ve `note` en fazla 2000 karakter; HTML panelde ve e-postada escape edilir; `PiiMasker`
  loglarda uygulanır.
- E-posta hatası geçişi geri almaz.

## Test

- **Domain:** `ComplaintTransitions` tablosunun her satırı + yasak geçişler + rol/atama kuralları.
- **Application:** `ComplaintManagementService` — yetki (403), yarış (409), girdi doğrulama (400), bildirimin
  doğru anlarda ve bir kez tetiklenmesi, e-posta hatasında geçişin başarılı kalması; `complaint_status_tool`
  çıktısı (açıklama var, iç not yok); `ApprovalExecutionRouter` onay kimliğini geçiriyor.
- **Persistence (Testcontainers):** migration, koşullu güncelleme (eşzamanlı iki `claim`'den biri kazanır),
  olay satırının aynı transaction'da yazılması, liste/sayaç sorguları.
- **Api:** uçların yetkisi (temsilci admin ucuna 403), 404/409/400 eşlemesi.
- **SLA:** eşik aşımında tek olay, kapalı şikayetin taranmaması.
- **Web:** liste/ayrıntı model yardımcıları (SLA rengi, düğme görünürlüğü) birim testleri.
