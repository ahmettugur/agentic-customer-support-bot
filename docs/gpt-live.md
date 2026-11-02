# Sesli kanal motorları: Realtime ve GPT-Live

Sesli görüşme iki motordan biriyle çalışır. Motor `AI:Realtime:Engine` ayarıyla seçilir:

```json
"AI": { "Realtime": { "Engine": "Live" } }
```

| Değer | Model | Çalışma biçimi |
|---|---|---|
| `Realtime` (varsayılan) | `gpt-realtime-2.1` | Yarı çift yönlü. Model konuşur, karar verir ve tool'ları kendisi çağırır. |
| `Live` | `gpt-live-1` | Tam çift yönlü. Ses modeli konuşmayı yürütür, tool ve akıl yürütmeyi arka plan modeline devreder. |

Ayar her bağlantıda okunur: `appsettings.json` değişince yeni görüşmeler yeni motorla açılır, uygulama yeniden başlatılmaz, süren görüşmeler etkilenmez. Ortam değişkeniyle de verilebilir (`AI__Realtime__Engine=Live`). Geçersiz değer açılışta hata verir.

İki motor da aynı tarayıcı uç noktasını (`/chat/realtime-native`), aynı ekranı, aynı tool'ları ve aynı onay akışını kullanır. Tool çalıştırma `VoiceToolDispatcher`, tur kaydı `VoiceTurnRecorder` içinde ortaktır. Yan etkili işlemler iki motorda da insan onayına gider, müşteri kimliği yalnızca doğrulanmış oturumdan okunur.

## GPT-Live ayarları (`AI:Realtime:Live`)

| Ayar | Varsayılan | Açıklama |
|---|---|---|
| `Endpoint` | `wss://api.openai.com/v1/live/sessions` | AB veri yerleşimi için `wss://eu.api.openai.com/...` |
| `Model` | `gpt-live-1` | Ses modeli. Yeni sürüm API'ye gelince yalnızca bu değişir. |
| `Voice` | `marin` | Türkçe için resmi ses yok; `marin` ile denendi. |
| `BackendModel` | `gpt-6-luna` | Tool seçen Responses modeli. |
| `BackendReasoningEffort` | `low` | Sesli yanıt beklediği için düşük. |
| `BackendServiceTier` | boş | `priority` ile hızlı mod seçilebilir. |
| `PricePerMinuteUsd` | `0.05` | Ses maliyeti hesabı. |
| `HistoryMessages` | `12` | Oturuma başlangıçta verilen son mesajlar. |
| `UserPauseMs` | `1500` | Kullanıcı konuşmasının bittiği sayılan sessizlik. Yalnızca balonu etkiler. |
| `AssistantPauseMs` | `1800` | Asistan turunun bittiği sayılan sessizlik. |
| `SpokenAnswerWaitSeconds` | `6` | Arka plan bittikten sonra cevabın söylenmesi için beklenen süre. |
| `IdleTimeoutSeconds` | `60` | Kimse konuşmazsa görüşme kapanır. |
| `MaxSessionSeconds` | `1800` | Görüşmenin üst süresi. |
| `EndGraceSeconds` | `6` | Sonlandırma kararından sonra veda için en fazla bekleme. |

Backend modelinin fiyatı `Telemetry:Pricing` altındadır. API anahtarı Realtime ile aynıdır.

## Gerçek API'de ölçülen davranışlar

Bu davranışların bir kısmı dokümanda yoktur; kod bunlara göre yazıldı (`gpt-live-1`, 2026-11-01):

- **Oturum zaman çizelgesi gelen sesle ilerler.** Ses yarı hızda gönderilince transkript bozuldu; ses kesilince `session.closed` hiç gelmedi. Bu yüzden mikrofon asistan konuşurken de akar ve kapanışta sessizlik gönderilir.
- **Çıkış sesi kesintisizdir.** Paketler sessiz anlarda da gelir (100 ms, tepe genlik 0-92; konuşmada binler). "Konuşuyor" bilgisi içerikten okunur, sessiz paketler tarayıcıya gönderilmez.
- **Tur sonu yoktur.** Transkript parçalarında öğe kimliği ve bitiş olayı yok, parçalar 1 saniyeyi aşan boşluklarla gelebilir. Turlar `LiveTurnAssembler` ile duraklamalardan çıkarılır.
- **Model cevaptan önce uzun susabilir.** Arka plan bittikten sonra 1,8 saniyeyi aşan bekleme görüldü; bu aralıkta tur açık tutulur.
- **`<|SpawnThinking|>` ifadesi çevrilmez.** Ses modeli işi bu yönergeyle devreder. Vedalaşma da `live-voice.md` içinde devretme koşulu olarak yazılıdır; aksi hâlde görüşme kapanmaz.

## Realtime'dan farkları

- **Girdi koruması tepkiseldir.** Yanıt bekletilemediği için `WaitForInputGuard` bu motorda geçerli değildir. Metin konuşma sürerken incelenir; reddedilirse tool'lar uygulamada engellenir, ses tarayıcıya 1,5 saniye gitmez, modele talimat gönderilir ve tur geçmişe yazılmaz. Sesin ilk parçası duyulabilir.
- **Yanıt iptal edilemez.** "Kes" düğmesi çalan sesi durdurur ve modele susma talimatı gönderir. Arka plan işi durmaz.
- **Maliyet süreye bağlıdır.** Sessizlik de ücretlidir ($3/saat). Etkinlik sesten değil konuşma metninden ölçülür, boşta ve üst süre sınırları zorunludur.
- **Maliyet sayılır.** Ses süresi ve arka plan token'ları `ILlmSpendGuard` ve `llm_call_usage` tablosuna yazılır; bütçe aşılırsa görüşme kapanır. Her görüşmenin sonunda `[Usage]` satırı loglanır. Realtime'da ses maliyeti hâlâ sayılmaz.
- **Geçmişle başlar.** Son mesajlar oturuma verilir; yazılı sohbetten sese geçen müşteri aynı konudan devam eder.
- **Ses kaydı tutulmaz.** `store: false` sabittir.

## Bilinen sınırlar

- **Türkçe resmi olarak desteklenmiş sayılmaz.** Resmi ses listesinde Türkçe yok, dil ve anahtar kelime ayarı yok. Sentetik sesle iyi sonuç alındı, gerçek insan sesiyle ölçülmedi. Üretimden önce gerçek konuşmalarla ürün adı ve numara doğruluğu denenmeli.
- **Yankı ölçülmedi.** Mikrofon açık kaldığı için hoparlörden çıkan ses modele dönebilir. Testler sahte mikrofonla yapıldı; hoparlörlü gerçek cihazda denenmeli. OpenAI örneği de kulaklık öneriyor.
- **Bağlantı WebSocket köprüsüdür.** OpenAI tarayıcı için WebRTC öneriyor. Yankı ya da gecikme yetersiz kalırsa WebRTC + sideband'e geçilebilir; sunucu olayları aynıdır. Bu durumda `client.data_channel.allowed_client_events` mutlaka kısıtlanmalıdır, varsayılan her şeye izin verir.
- **Tarayıcı kapanışta `1006` görebilir.** Görüşme bittikten sonra istemci zaten kapanmış olduğu için ekranda etkisi yoktur.
