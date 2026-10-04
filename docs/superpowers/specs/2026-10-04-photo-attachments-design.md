# Fotoğraf Ekleme (şikayet / iade / sipariş) — Tasarım

**Tarih:** 2026-10-04 · **Dal:** `feature/photo-attachments`

## Amaç

Müşteri yazılı sohbette fotoğraf ekleyebilsin: hasarlı gelen ürün (şikayet/iade) ya da sipariş
vermek istediği ürünün fotoğrafı. Fotoğraf görsel anlayan bir modelle kısa bir metne çevrilir, ajanlar
bu metni kullanır; açılan onay kaydına fotoğraf bağlanır ve temsilci onay kartında görür.

Başarı ölçütleri:
- Fotoğraf + "ürün kırık geldi" → ajan hasarı bilerek yanıt verir.
- Fotoğraf bir turda, sipariş numarası sonraki turda gelse bile şikayet/iade onay kaydına fotoğraf bağlanır.
- Admin/temsilci onay kartında fotoğrafı görür.

## Kapsam

- Yalnızca yazılı sohbet (sesli kanalda görüntü yok). JPEG ve PNG, en fazla 5 MB, mesaj başına 3,
  oturum başına 10 fotoğraf.
- Kapsam dışı: fotoğrafın şikayet kaydının kendisine (katalog tablosu) yazılması, görsel düzenleme,
  HEIC/WEBP.

## Mimari

**Akış**
1. Müşteri 📎 ile fotoğraf seçer → `POST /chat/attachments` (multipart, `sessionId` isteğe bağlı).
   Oturum yoksa sunucu oluşturur ve müşteriye bağlar; yanıt `{ attachmentId, sessionId, description }`.
2. Sunucu: boyut ve dosya imzası (magic bytes) kontrolü → **meta veriyi siler** (JPEG APP1–APP15/COM,
   PNG metin/eXIf/zaman parçaları — EXIF konum bilgisi dahil) → görsel modelle kısa açıklama →
   açıklama `IInputGuard`'dan geçer (kişisel veri maskeleme, enjeksiyon reddi) → `chat.attachments`.
3. Mesaj gönderilirken `attachmentIds` gövdeye eklenir. `ChatPortService` yalnızca bu oturuma ve bu
   müşteriye ait kayıtları kabul eder ve sorguya şu bloğu ekler:
   `[Müşterinin eklediği fotoğraf — otomatik analiz]: …` — geçmişe de bu hâliyle yazılır, böylece
   sonraki turlarda ve temsilci panelinde görünür.
4. Yan etkili bir tool onaya gittiğinde `SideEffectApprovalGate`, oturumun henüz bir onaya bağlanmamış
   fotoğraflarını `Parameters["attachmentIds"]`'e ekler ve onları o onay kaydına bağlar (aynı fotoğraf
   sonraki ilgisiz bir onaya taşınmaz). Yürütücü bu anahtarı yok sayar.
5. Onay kartı fotoğrafları gösterir: `GET /attachments/{id}` (admin) ve `/agent/attachments/{id}`.
   Müşteri kendi fotoğrafını `GET /chat/attachments/{id}` ile alabilir.

**Bileşenler**
- Domain: `ChatAttachment`.
- Ports: `IAttachmentStore` (kalıcılık), `IImageAnalysisPort` (görsel model), `IChatAttachmentPort`
  (inbound: yükle, getir).
- Application: `ChatAttachmentService` (doğrulama + akış), `ImageSanitizer` (saf: tür tespiti +
  meta veri silme).
- Persistence: `chat.attachments` (bytea, `session_id` FK → `chat.sessions`, cascade), EF store;
  testler için bellek içi store.
- AI: `ChatClientImageAnalysisAdapter` — mevcut `IChatClient`, `DataContent` ile; talimat
  `Prompts/services/image-analysis.md`.
- Web: mesaj kutusunda 📎, seçilen fotoğraflar için küçük önizleme ve kaldırma; kullanıcı balonunda
  küçük resim; onay kartında küçük resimler (yetkili istekle alınıp data URL olarak gösterilir).

## Güvenlik ve gizlilik

- Tür, dosya uzantısına ya da istemcinin bildirdiği türe değil **dosya imzasına** göre belirlenir.
- Meta veri (konum dahil) saklamadan önce silinir.
- Görsel model talimatı: adres, isim, telefon, kart gibi kişisel verileri yazıya dökme; yalnızca
  ürün, hasar ve ürün/sipariş etiketini anlat. Ek güvence olarak açıklama `IInputGuard` maskelemesinden
  geçer; enjeksiyon olarak reddedilirse açıklama kullanılmaz.
- Fotoğraf yükleme, sohbetle aynı yetki (Customer), oturum sahipliği ve hız sınırı (`chat`)
  kurallarına tabidir; uçta istek gövdesi sınırı vardır.
- Fotoğraflar oturumla birlikte silinir (FK cascade).
- Görsel modelin metni kullanıcı mesajının parçası olarak (kullanıcı rolü) ajanlara gider — kullanıcının
  kendi yazdığı metinle aynı güven düzeyi.

## Hata yönetimi

- Görsel model hatası → fotoğraf yine kaydedilir, açıklama "otomatik analiz yapılamadı"; ajan
  fotoğrafın varlığını bilir, temsilci görür.
- Geçersiz tür/boyut/limit → 400, anlaşılır Türkçe mesaj; oturum sahipliği → 403.
- Mesajdaki başka oturuma/müşteriye ait ya da bilinmeyen kimlikler sessizce yok sayılır (loglanır).

## Test

- `ImageSanitizer`: JPEG/PNG tanıma, sahte uzantı reddi, EXIF/metin parçalarının silinmesi, görüntü
  verisinin korunması.
- `ChatAttachmentService`: limitler, analiz hatasında kayıt, guard maskeleme/reddi, sahiplik.
- `ChatPortService`: fotoğraf bloğunun sorguya eklenmesi, yabancı kimliklerin yok sayılması.
- `SideEffectApprovalGate`: bağlanmamış fotoğrafların eklenip bağlanması; ikinci onaya taşınmaması.
- Persistence: Postgres store (Testcontainers) ve cascade.
- API: yükleme (oturum oluşturma dahil), sahiplik 403, tür/boyut 400, staff görüntüleme, müşteri
  başkasının fotoğrafına erişemez.
- Arayüz: tarayıcıda seçme → önizleme → gönderim gövdesinde kimlikler.
