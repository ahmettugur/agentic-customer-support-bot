# Temsilci ile Sesli Görüşme (Kayıt + Döküm) — Tasarım

**Tarih:** 2026-10-06 · **Dal:** `feat/agent-voice-call`

## Amaç

Bugün sesli moddaki müşteri temsilci istediğinde, temsilci katılınca ses kapanıyor ve görüşme yazılı devam
ediyor (`realtime-ui.js` — `human_joined` → sesli kanal kapanır). Temsilcinin müşteriyle **gerçek zamanlı sesli**
görüşebilmesi; görüşmenin **kaydedilmesi** ve **yazıya dökülüp** konuşma geçmişine girmesi isteniyor.

## Kararlar (kullanıcıyla netleşti)

| Konu | Karar |
|---|---|
| Ses taşıma | Tarayıcılar arası **WebRTC** (P2P). Sunucu sesi taşımaz; yalnızca kurulum (sinyal) mesajlarını iletir. |
| Kaydı kim yapar | **Temsilci tarayıcısı** — kendi mikrofonu ve müşterinin gelen sesi, **ayrı iki iz**. Müşteri kaydı durduramaz/bozamaz; konuşan ayrımı kesin. |
| Döküm zamanı | **Görüşme sırasında** — ~10 sn'lik parçalar yüklenir ve dökülür; panelde ~10–15 sn gecikmeyle görünür. |
| Rıza | **Zorunlu.** Müşteri kayda onay vermezse sesli görüşme olmaz, yazılı devam eder. |
| Saklama | Ses **90 gün** (`DataRetention:VoiceRecordingRetentionDays`); döküm metni konuşmayla (180 gün). |
| Eşzamanlılık | Temsilci aynı anda **en fazla bir** sesli görüşmede olabilir (yazılı sohbet sayısından bağımsız). |

## Kapsam

- Temsilci (Admin/Agent) devraldığı canlı sohbette sesli görüşme başlatır; müşteri kabul/ret eder.
- WebRTC bağlantısı; NAT/kurumsal ağ için TURN (docker-compose'a `coturn/coturn:latest`, `container_name: aibot_coturn`;
  yığın `docker compose -p aibot -f deploy/docker-compose.yml up -d` ile kalkar).
- Temsilci tarafında iki izli kayıt, parça parça yükleme, sunucuda saklama.
- Parça başına döküm (OpenAI, `tr`), konuşan etiketiyle konuşma geçmişine.
- Kayıt dinleme (yönetici + görüşmeyi yapan temsilci), döküm satırından o ana atlama.
- KVKK: saklama süresi, müşteri veri dışa aktarma/silmeye dahil.

**Kapsam dışı:** Müşterinin temsilciyi araması (yalnızca temsilci başlatır), üçlü/konferans görüşme,
görüntülü görüşme, telefon şebekesi (PSTN), sunucu tarafı ses karıştırma (iki iz ayrı saklanır), nesne
depolama (S3 vb. — port hazır, ilk uygulama Postgres).

## Mimari

### Domain

`VoiceCall` (yeni):

| Alan | Açıklama |
|---|---|
| `Id` | Görüşme kimliği |
| `SessionId` | Bağlı canlı sohbet |
| `AgentId`, `AgentDisplayName` | Görüşmeyi başlatan temsilci |
| `Status` | `Ringing → Active → Ended` ya da `Ringing → Declined / Missed / Cancelled`; `Active → Failed` |
| `CreatedAt`, `AnsweredAt`, `EndedAt` | Zamanlar |
| `ConsentAt` | Müşterinin kayıt rızası (kabul anı) — `Active` olabilmek için zorunlu |
| `EndReason` | `agent_hangup`, `customer_hangup`, `connection_lost`, `declined`, `missed`, `cancelled` |

Durum geçişleri domain'de; geçersiz geçiş hata (ör. rıza olmadan `Active`). 45 sn yanıtsız `Ringing` → `Missed`.

`VoiceRecordingChunk` (yeni): `CallId`, `Track` (`agent` | `customer`), `Sequence`, `StartedAtOffsetMs`,
`DurationMs`, `ContentType` (`audio/webm;codecs=opus`), `Data`, `TranscriptStatus` (`Pending | Done | Failed`),
`TranscriptText`, `Attempts`, `CreatedAt`. (`CallId`, `Track`, `Sequence`) benzersiz — aynı parça iki kez
yüklenirse ikincisi yok sayılır (idempotent).

### Tek sesli görüşme kuralı

`voice_calls` tablosunda `AgentId` üzerinde **unique partial index**: `WHERE status IN ('Ringing','Active')`.
Başlatma isteği satırı eklemeye çalışır; çakışma → `409 voice_call_busy`. Çok pod'da eşzamanlı isteklerde de
kural veritabanında garanti altındadır. Aynı oturumda ikinci açık görüşme de engellenir (ikinci partial index:
`SessionId`). InMemory adaptör aynı kuralı kilitle uygular.

Temsilci varlığı (`AgentPresence`) "Görüşmede" alt durumu gösterir; yönlendirme (SkillsBasedRouter) bu durumu
değiştirmez — yazılı sohbet ataması mevcut `CurrentLoad / MaxConcurrentLoad` ile devam eder.

### Sinyal (WebRTC kurulumu)

Mevcut canlı sohbet köprüsü (`IChatBridge`, SSE + Redis pub/sub) kullanılır. `ChatBridgeSender`'a yeni tür:
`VoiceSignal` — `BotTyping` gibi **geçmişe yazılmaz**, yalnızca yayınlanır, ve **yönlüdür** (temsilcinin
gönderdiği yalnızca müşteriye, müşterininki yalnızca temsilciye). Yük: `{ callId, type: offer|answer|ice|ring|accept|decline|hangup, data }`.

Uçlar (temsilci: `/admin/...` ve `/agent/...` önekleri, müşteri: `/chat/...`):

| Uç | Kim | İş |
|---|---|---|
| `POST /chat-sessions/{sid}/voice-calls` | Temsilci | Görüşme başlat (`Ringing`), müşteriye `ring` sinyali |
| `POST /voice-calls/{id}/signal` | İki taraf | offer/answer/ice ilet (görüşmenin tarafı olmayan → 403) |
| `POST /chat/voice-calls/{id}/accept` | Müşteri | Rıza + kabul → `Active` |
| `POST /chat/voice-calls/{id}/decline` | Müşteri | Ret → `Declined` |
| `POST /voice-calls/{id}/hangup` | İki taraf | `Ended` |
| `GET /voice-calls/ice-config` | İki taraf | STUN + kısa ömürlü TURN kimliği |

TURN kimliği: coturn `use-auth-secret` (TURN REST API) — sunucu paylaşılan sırla `kullanıcı = sonGeçerlilik:callId`,
`parola = HMAC-SHA1` üretir, ömür 10 dk. Kalıcı sır tarayıcıya gitmez. Ayar: `VoiceCall:Turn:Urls`,
`VoiceCall:Turn:SharedSecret` (yerelde compose ile aynı sabit değer — dev, hassas değil).

Bağlantı kopması: temsilci tarayıcısı `RTCPeerConnection` durumunu izler; 30 sn `disconnected/failed`
→ `hangup` (`connection_lost`). Sunucu tarafında güvenlik ağı: `Active` görüşme için 60 sn'dir parça
yüklenmiyorsa (temsilci sekmesi kapandı) arka plan işi görüşmeyi `Failed` / `connection_lost` yapar.

### Kayıt

Temsilci tarayıcısı iki `MediaRecorder` (yerel mikrofon izi + uzak müşteri izi), `timeslice = 10 sn`.
Her parça `POST /voice-calls/{id}/chunks?track=…&seq=…&offsetMs=…` (gövde ham ses, en fazla 2 MB) ile
yüklenir; başarısız yükleme 3 kez tekrar denenir (idempotent). Port: `IVoiceRecordingStore`
(Postgres `bytea`, fotoğraflardaki desen). Yalnızca görüşmenin temsilcisi yükleyebilir; `Ended` görüşmeye
son parçalar için 2 dk tolerans.

> Not: MediaRecorder parçaları bağımsız oynatılamayabilir (webm başlığı yalnızca ilk parçada). Döküm için her
> parça **tek başına çözülebilir** olmalı — bu yüzden kaydedici her 10 sn'de **yeniden başlatılır** (stop/start),
> her parça başlıklı tam bir webm dosyasıdır. Aradaki boşluk birkaç ms'dir; ofset alanı sıralamayı korur.

### Döküm

Yeni port `IAudioTranscriber` (`TranscribeAsync(stream, contentType, language, ct)` → metin); adaptör OpenAI
ses dökümü (model: mevcut `AI:TranscriptionModel`, dil `AI:TranscriptionLanguage`). Arka plan işi
(`VoiceTranscriptionService`, `BackgroundService`): `Pending` parçaları sırayla alır (çok pod'da çakışmasız:
koşullu durum güncellemesiyle sahiplenme), döker, köprüye yazar:

- Konuşma geçmişine **yalnızca temsilci tarafına** giden mesaj (`PublishAdminOnlyMessageAsync` deseni —
  geçmişe yazılır, müşteriye yayınlanmaz). `ChatBridgeMessage`'a isteğe bağlı meta alanları eklenir:
  `VoiceCallId`, `VoiceTrack` (`agent|customer`), `OffsetMs`. Metin düz: "Müşteri: …" / "Temsilci (ad): …";
  panel bu satırları meta alanlardan tanıyıp mikrofon ikonuyla ve "o ana atla" bağlantısıyla çizer (emoji yok).
  Müşteri ekranında döküm satırı gösterilmez (sesli görüşmeyi zaten yaşıyor).
- Boş/sessiz parça (döküm boş) satır üretmez.
- Hata: 3 deneme (artan bekleme); sonra `Failed`, satır "(döküm alınamadı)" — ses yine saklıdır.
- Duygu analizi: müşteri izinin döküm satırları mevcut duygu hattına müşteri mesajı gibi verilir.

Maliyet: döküm `ILlmSpendGuard`'a tabidir; limit aşılmışsa yeni parçalar `Pending` kalır ve limit açılınca
işlenir (kayıt kaybolmaz).

### Dinleme

`GET /voice-calls/{id}` (meta + döküm satırları), `GET /voice-calls/{id}/chunks/{chunkId}` (tek parçanın sesi —
webm parçaları tek tek tam dosya olduğu için istemci her izi parça parça, iki izi aynı anda oynatır).
Yetki: Admin her görüşme; Agent yalnızca kendi görüşmeleri. Panel: konuşma dökümü ve Traces sohbet
geçmişinde "Sesli görüşme · 4 dk 12 sn" satırı + oynatıcı (iki iz eşzamanlı, döküm satırı tıklanınca
`OffsetMs`'e atlar).

### Saklama / KVKK

- `DataRetentionOptions.VoiceRecordingRetentionDays = 90`: `DataRetentionService` bu süreden eski parçaları
  siler (görüşme meta ve döküm metni kalır).
- `IVoiceRecordingStore` ve görüşme deposu `ISessionDataEraser` uygular → oturum silinince ses de silinir.
- Müşteri veri dışa aktarımı (`/customer/data/export`) görüşme listesi + döküm metnini içerir (ses dosyası
  boyutu yüzünden dışa aktarıma girmez; ayrı istekle verilebileceği not edilir).

## Arayüz

**Temsilci (Canlı sohbetler, açık sohbet başlığı):** "Sesli görüşme başlat" (telefon ikonu). Görüşmedeyken
başka sohbetlerde pasif ("Zaten bir sesli görüşmedesiniz"). Çalarken: "Aranıyor… · İptal". Görüşmede:
süre sayacı, "Kayıt alınıyor" göstergesi, sessize al, bitir. Döküm satırları sohbet akışına düşer.

**Müşteri (sohbet ekranı):** mevcut `VoiceCallOverlay` görsel diliyle gelen arama kartı: "Temsilci sizi
arıyor", rıza metni ("Bu görüşme kalite ve kayıt amacıyla kaydedilecek ve yazıya dökülecek."), **Kabul et /
Reddet**. Görüşmede: temsilci adı, süre, sessize al, bitir. Müşteri sesli AI modundaysa gelen arama önce
AI sesli kanalını kapatır.

**Kenar çubuğu/varlık:** görüşmedeki temsilci "Görüşmede".

## Hata durumları

| Durum | Davranış |
|---|---|
| Temsilci zaten görüşmede | 409 `voice_call_busy`, düğme pasif + açıklama |
| Müşteri mikrofon izni vermez | Müşteri tarafı `decline` (`EndReason = no_microphone`), temsilciye bildirim |
| Rıza yok | Görüşme kurulmaz (`Declined`) |
| 45 sn yanıt yok | `Missed`, iki tarafa bildirim |
| WebRTC kurulamadı (TURN yok/erişilemez) | 20 sn içinde `connected` olmazsa `Failed`, yazılı devam |
| Temsilci sekmesi kapandı | 60 sn parça yoksa sunucu `Failed` |
| Parça yükleme hatası | 3 tekrar; kalıcı hata panelde "Kayıt kesintiye uğradı" uyarısı |
| Döküm hatası | 3 deneme, sonra "(döküm alınamadı)", ses saklı |

## Test

- **Domain:** durum geçişleri, rıza olmadan `Active` yok, `Missed` zaman aşımı.
- **Application:** başlatma (tek görüşme kuralı), sinyalin yalnızca karşı tarafa gitmesi ve geçmişe
  yazılmaması, yetki (görüşmenin tarafı olmayan 403), parça idempotentliği, döküm işi (sahiplenme, tekrar,
  boş parça, bütçe), saklama süresi.
- **Postgres entegrasyon (Testcontainers):** unique partial index ile eşzamanlı iki başlatmadan yalnız birinin
  geçmesi; parça sırası; silme/saklama.
- **API:** uç yetkileri (Admin/Agent/Customer), 409, 403.
- **Web:** ice-config istemcisi, durum makinesi yardımcıları (birim).
- **Uçtan uca (Playwright):** iki tarayıcı bağlamı, sahte mikrofon (`--use-fake-device-for-media-stream`),
  gerçek WebRTC bağlantısı, kayıt parçalarının yüklenmesi, sahte döküm adaptörüyle satırların panelde
  görünmesi.

## Teslim sırası

1. Domain + kalıcılık (VoiceCall, kayıt parçaları, partial index, migration) + tek görüşme kuralı.
2. Sinyal ucu + köprüde `VoiceSignal` + ice-config + coturn.
3. Temsilci ve müşteri arayüzü: arama, rıza, WebRTC, bitirme.
4. Kayıt yükleme + depolama.
5. Döküm işi + geçmişe yazma + duygu.
6. Dinleme ekranı, saklama/KVKK, belgeler.
