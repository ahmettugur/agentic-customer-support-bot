# Veri Saklama ve Kişisel Veri Silme (KVKK) — Tasarım

**Tarih:** 2026-10-05 · **Dal:** `feature/data-retention`

## Amaç

Sohbet verisinin süresiz tutulmasını bitirmek ve müşterinin kendi verisini indirip silebilmesini
sağlamak. Fotoğraf eklemeyle birlikte saklanan kişisel verinin türü genişledi; bu iş artık zorunlu.

Başarı ölçütleri:
- Belirli süredir hareketsiz oturumlar ve belirli süreden eski fotoğraflar otomatik silinir (süreler
  appsettings'ten, kapatılabilir).
- Müşteri sohbet ekranından verisini JSON olarak indirebilir ve silebilir.
- Yönetici aynı işlemleri bir müşteri adına yapabilir (KVKK başvurusu).
- Silme tüm kopyaları kapsar: veritabanı, sunucu önbellekleri (çok pod'lu kurulumda tüm pod'lar),
  episodik bellek (Qdrant).

## Kapsam

**Silinen (oturuma bağlı kişisel veri):**
- Oturum, mesaj geçmişi, fotoğraflar (`chat.sessions` → FK cascade: `messages`, `attachments`)
- Canlı devralma mesajları (`chat.bridge_messages`), oturum modu (`chat.session_modes`)
- Oturumun puanı ve yorumu (`analytics.ratings`)
- Oturumun kapanmış eskalasyonları (`hitl.escalations`); açık olanlar silinmez — temsilci kuyruğu ve yük
  sayaçları bozulmasın — ama müşteri metni temizlenir
- Oturumun akıl yürütme izleri (`observability.reasoning_traces` — sorgu/yanıt metni içerir)
- Oturumun episodik bellek kayıtları (Qdrant)

**Müşteri silme talebinde ayrıca:** kişiselleştirme profili (`personalization.customer_profiles`) ve
müşteri etiketli tüm episodik bellek kayıtları.

**Silinmeyen (yasal/işlemsel kayıt):** siparişler, şikayet kayıtları, onay kayıtları (yapılan işlemin
denetim izi), müşteri ana kaydı ve giriş hesabı. Bunlar dışa aktarmada yer alır. Hesap kapatma ayrı bir
süreçtir — kapsam dışı.

**Kapsam dışı:** LLM çağrı kayıtları (metin içermez: model, token, maliyet), dersler (anonimleştirilmiş).

## Yapılandırma (`DataRetention`)

| Ayar | Varsayılan | Anlamı |
|---|---|---|
| `Enabled` | `true` | Otomatik temizliği açar/kapatır (dışa aktarma/silme her zaman açık). |
| `ConversationRetentionDays` | `180` | Son etkinliği bundan eski oturumlar silinir. `0` = kapalı. |
| `AttachmentRetentionDays` | `90` | Bundan eski fotoğraflar silinir (oturum daha yeni olsa da). `0` = kapalı. |
| `SweepIntervalMinutes` | `60` | Tarama aralığı. |
| `MaxSessionsPerSweep` | `500` | Bir taramada silinecek en fazla oturum (yükü yaymak için). |

## Mimari

- **Port `ISessionDataEraser`** (outbound): `EraseSessionsAsync(sessionIds)`. Oturuma bağlı veri tutan
  her depo bunu uygular: sohbet köprüsü, oturum modu, puanlar, eskalasyonlar, akıl yürütme izleri,
  episodik bellek. Yeni bir depo eklendiğinde silmeye katılmak için yalnızca bu arayüzü uygular.
- **Önbellekler:** silen pod kendi önbelleğinden çıkarır ve `csbot:privacy:sessions-erased` kanalına
  oturum kimliklerini yayınlar; her depo bu kanala abone olup diğer pod'larda da çıkarır.
- **`DataPrivacyService`** (`IDataPrivacyPort`, Application):
  - `RunRetentionAsync(now)`: süresi dolan fotoğrafları siler; hareketsiz oturumları toplar ve siler.
  - `ExportCustomerDataAsync(customerId)`: profil, oturumlar + mesajlar + fotoğraf bilgileri (görüntü
    verisi hariç), puanlar, onay geçmişi, siparişler, şikayetler.
  - `EraseCustomerDataAsync(customerId)`: müşterinin tüm oturumları + profil + müşteri etiketli bellek.
  - Oturum silme sırası: önce tüm `ISessionDataEraser`'lar, **en son** oturumun kendisi
    (`ISessionManager.ClearSessionAsync`). Bir depo hata verirse oturum yerinde kalır; sonraki tarama ya
    da tekrar deneme kaldığı yerden devam eder. Hatalar toplanır ve sonunda raporlanır.
- **`DataRetentionService`** (Api worker): `SweepIntervalMinutes`'te bir, dağıtık kilitle (tek pod).
- **Uçlar:**
  - `GET /customer/data/export`, `DELETE /customer/data?confirm=true` — müşteri kendi verisi
  - `GET /customers/{customerId}/data/export`, `DELETE /customers/{customerId}/data?confirm=true` — yalnız Admin
- **Arayüz:** sohbet başlığında "Gizlilik" menüsü: "Verilerimi indir" ve onaylı "Verilerimi sil".

## Hata yönetimi

- Silme kısmen başarısız olursa uç 500 döner ve hangi depoların başarısız olduğunu loglar; işlem güvenle
  tekrarlanabilir (silinmiş veriyi tekrar silmek no-op).
- Temizlik bir taramada hata alırsa loglanır, bir sonraki taramada tekrar denenir.

## Test

- `DataPrivacyService`: yalnızca süresi dolan oturumlar/fotoğraflar; silme sırası; kısmi hata; dışa
  aktarma yalnızca o müşterinin verisi; silme diğer müşterilere dokunmaz.
- Her Postgres deposu (Testcontainers): silme + önbellekten çıkarma.
- Qdrant: oturuma ve etikete göre silme.
- API: kimlik/rol, `confirm` zorunluluğu, başkasının verisine erişilemez.
- Arayüz: tarayıcıda indir/sil akışı.
