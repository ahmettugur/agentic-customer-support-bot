# RealtimeBridgeService

- **Kaynak:** `Services/Realtime/RealtimeBridgeService.cs`
- **Tür:** `public sealed class : IRealtimeBridge`
- **Namespace:** `CustomerSupportBot.Application.Services.Realtime`

## 1. Ne İşe Yarar

**Köprü modu** (bridge mode) sesli görüşmenin orkestratörüdür: tarayıcıdan gelen ses/kontrol
mesajlarını OpenAI Realtime'a iletir, OpenAI'den gelen transkripti **yazılı sohbetle aynı
reasoning + agent-team pipeline'ından** geçirir, sonucu hem ekrana hem TTS ile sese çevirir.
Her WebSocket bağlantısı için ayrı bir **Scoped** instance oluşturulur.

Bridge modu, [`RealtimeNativeService`](RealtimeNativeService.md)'in aksine, OpenAI'nin kendi
modelinin cevap üretmesine izin vermez (`create_response=false`) — cevabı her zaman metin
ajanları üretir, ses yalnızca o cevabın okunmasıdır.

## 2. Hangi Amaçla Kullanılır

Sesli kanalın, yazılı sohbetle **aynı** tool erişimini, aynı HITL onay akışını, aynı reasoning/
routing mantığını kullanmasını sağlamak — iki ayrı "beyin" olmasın diye. `RunAsync`, bir
WebSocket bağlantısının tüm ömrü boyunca çalışır (bağlantı kapanana kadar).

## 3. Sorumlulukları

**Üstlendiği:**
- Oturum kimliğini `SessionIdentityBinder.BindAtomicallyAsync` ile **atomik** bağlamak (yazılı
  sohbetle aynı mekanizma/kilit anahtarı).
- Tarayıcı → OpenAI ve OpenAI → tarayıcı iki pump'ı (`PumpBrowserAsync`, `HandleEventsAsync`)
  paralel çalıştırmak, biri biterse ikisini birden iptal etmek.
- **Half-duplex gating:** asistan konuşurken veya bir tur işlenirken mikrofon sesini OpenAI'ye
  iletmemek (`IsBusy`).
- Transkript geldiğinde reasoning + agent-team pipeline'ını çalıştırmak, sonucu geçmişe yazmak
  ve TTS ile okutmak.
- Tur bazlı distributed lock (`SessionIdentityBinder.TurnLockKey`) ile aynı oturumda paralel
  tur çalışmasını engellemek — **yazılı sohbetle aynı anahtar**, yani sesli ve yazılı kanal
  birbirine göre de sıraya girer.

**Üstlenmediği:** OpenAI Realtime protokolünün kendisi (`IRealtimeVoiceTransport`'un işi),
reasoning/routing mantığı (`IReasoningPort`/`IAgentTeamPort`'un işi).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `IRealtimeVoiceTransport` — OpenAI Realtime bağlantısı (Adapters.AI katmanında implemente edilir).
- `IAgentTeamPort`, `IReasoningPort` — yazılı sohbetle **paylaşılan** aynı pipeline.
- `ISessionManager` — geçmiş okuma/yazma.
- `IApprovalContextAccessor` — HITL onay bağlamına `AuthenticatedCustomerId` taşınması.
- `IChatBridge` — admin panelinin canlı izleyebilmesi için bot alışverişini kaydetmek.
- `IInputGuard` — transkripti prompt-injection/güvenlik açısından denetlemek.
- `IAppDistributedLock` — kimlik bağlama + tur kilidi.
- `SessionIdentityBinder` (statik yardımcı) — yazılı sohbetle ortak kimlik bağlama mantığı.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

### `_turnInFlight` — neden `_assistantSpeaking`den ayrı bir bayrak

Kullanıcı transkripti alındıktan sonra agent pipeline'ı (reasoning + workflow) sürerken
`true`. `_assistantSpeaking` bu pencereyi **kapsamaz**: bridge modunda `create_response=false`
olduğu için asistan ancak pipeline bitince `SpeakTextAsync` ile konuşmaya başlar — yani
"kullanıcı sustu" ile "asistan konuşuyor" arasında saniyeler süren sessiz bir aralık vardır.

> 🐞 **Bu aralık korumasız bırakıldığında canlıda görülen hata:** Mikrofon OpenAI'ye akmaya
> devam ediyor, `semantic_vad` sessizlik/gürültüde tetikleniyor ve transkripsiyon modeli —
> `TranscriptionPrompt` ile domain sözlüğüne yönlendirildiği için — boş dönmek yerine makul
> görünen bir cümle **uyduruyordu** ("Merhaba, müşteri numaram 1025."). Bu sahte transkript
> hem sohbete kullanıcı balonu olarak düşüyor hem de ikinci bir pipeline başlatıp ilk turun
> event akışıyla karışıyordu (ilk turun reasoning paneli yarım JSON'da kilitli kalıyordu).

### `_turnInFlight` sıfırlaması neden `finally`'de

Her çıkış yolunda (başarı, guard reddi, iptal, hata) sıfırlanmalı — aksi halde mikrofon kalıcı
olarak susturulmuş kalır ve oturum sağır olur.

### Tur kilidinin bekleme süresi neden 30sn (yazılı sohbette 120sn)

Sesli kullanıcı ekrana bakıp bekleyemez, karşısında sessizlik olur. 30 saniye içinde sıra
gelmezse beklemeye devam etmek yerine açıkça "hâlâ işleniyor" demek dürüsttür.

### TTS'e verilen metnin kaynağı neden `response_complete`, delta birleşimi değil

Sesli kanalda bu metin hem geçmişe yazılır hem de TTS ile **müşteriye okunur** — dolayısıyla
kaynağı kanonik olmak zorunda: önce `response_complete`, o gelmezse delta birleşimi.

> 🐞 **Eskiden yalnızca delta'lar birleştiriliyordu** ve bu, ajan adı sızıntısı olan bir turda
> müşterinin "OrderAgent size yardımcı olacak" gibi bir cümleyi sesli duyması demekti —
> `RewriteRoutingMessageAsync` savunması yalnızca yazılı ekrana uygulanıyor, sesi baypas
> ediyordu.

### Gecikmeli transkriptin ikinci savunma katmanı

Mikrofon zaten `IsBusy` iken susturuluyor, ama susturma anından ÖNCE OpenAI'ye ulaşmış ses
için transkript hâlâ gecikmeli gelebilir. `_turnInFlight` true iken gelen transkript ne sohbete
yazılır ne de yeni bir pipeline başlatır — aksi halde tek turda iki pipeline aynı kanala
paralel event basıp reasoning panelini bozardı.

### Intent neden `HandleUserTranscriptAsync` içinde state'e yazılmıyor

`AddExchangeAsync` turun state çıkarımını çalıştırıp aynı alanı zaten yazıyor; burada yazma
birkaç satır sonra sessizce eziliyordu — artık `TurnSignals` ile girdi olarak taşınıyor.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `RunAsync(channel, sessionId, authenticatedCustomerId, ct)` | Bağlantının tüm ömrü: transport kapalıysa/bağlanamazsa hata mesajı gönderip döner; kimliği atomik bağlar (başka müşteriye aitse reddeder); `ConfigureBridgeSessionAsync` ile OpenAI oturumunu ayarlar; `PumpBrowserAsync` ve `HandleEventsAsync`'i `Task.WhenAny` ile paralel çalıştırır, biri biterse ikisini de iptal eder. |
| `PumpBrowserAsync(channel, ct)` *(private)* | Tarayıcıdan gelen mesajları okur: `Binary` (ses) → `IsBusy` değilse OpenAI'ye iletir; `Text` (kontrol) → `HandleBrowserControlAsync`'e yönlendirir; `Closed` → döner. |
| `HandleBrowserControlAsync(json, ct)` *(private)* | `{"type":"interrupt"}` → `SendInterruptAsync`; `{"type":"stop"}` → `CloseAsync`. Parse hatası sessizce loglanır. |
| `HandleEventsAsync(channel, session, ct)` *(private)* | OpenAI'den gelen event'leri işler: `ResponseCreated`/`AudioDelta` → `_assistantSpeaking=true`; `InputTranscriptCompleted` → (yukarıdaki iki savunma kontrolünden sonra) `_turnInFlight=true` set edip `HandleUserTranscriptAsync`'i fire-and-forget başlatır; `ResponseDone`/`ResponseCancelled` → `_assistantSpeaking=false`; `ConnectionClosed` → döner. |
| `HandleUserTranscriptAsync(channel, session, transcript, ct)` *(internal — test edilebilirlik için `InternalsVisibleTo`)* | Tur kilidini alır → `IInputGuard.Inspect` ile transkripti denetler (red ise TTS ile red mesajını okur ve döner) → reasoning stream'ini çalıştırır → agent-team stream'ini çalıştırır (`ResponseComplete`'i kanonik yanıt olarak yakalar) → geçmişe yazar (`AddExchangeAsync`) + `IChatBridge.RecordBotExchange` → TTS ile okutur. `finally` içinde `_turnInFlight=false`. |
| `ForwardStreamEventAsync(channel, evt, ct)` *(private static)* | Reasoning/agent-team stream event'lerini olduğu gibi tarayıcıya JSON olarak iletir. |
| `IsBusy` *(private, get)* | `_assistantSpeaking \|\| _turnInFlight`. |

## 7. Bağımlılıklar

Constructor injection ile: `IRealtimeVoiceTransport`, `IAgentTeamPort`, `ISessionManager`,
`IReasoningPort`, `IApprovalContextAccessor`, `IChatBridge`, `IInputGuard`,
`IAppDistributedLock`, `ILogger<RealtimeBridgeService>`.

## Bağlantılar

- [RealtimeNativeService.md](RealtimeNativeService.md) — alternatif "native" mod (OpenAI'nin kendi cevabı)
- [../Chat/ContextPipeline.md](../Chat/ContextPipeline.md) — bu servisin de dolaylı olarak tetiklediği bağlam kurulumu
