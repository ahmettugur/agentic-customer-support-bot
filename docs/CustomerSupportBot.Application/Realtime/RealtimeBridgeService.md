# RealtimeBridgeService

**Dosya:** `Services/Realtime/RealtimeBridgeService.cs`
**Implements:** `IRealtimeBridge`
**Uç:** `WS /chat/realtime/{sessionId?}`

> ⚠️ Bu doküman eskiden bu sınıfı "HITL canlı sohbet köprüsü — bot'tan insan temsilciye geçiş"
> olarak anlatıyordu. Yanlıştı; o iş `ChatSessionPortService`/`IChatModeRegistry`'de.
> Buradaki "köprü", **ses ile ajan pipeline'ı arasındaki** köprüdür.

## 1. Ne İşe Yarar

**Köprü sesli mod**: OpenAI Realtime yalnızca kulak ve ağızdır (STT + TTS); cevabı **ajan
pipeline'ı** üretir. Kullanıcının konuşması metne çevrilir, reasoning + workflow çalışır,
üretilen yanıt `SpeakTextAsync` ile seslendirilir.

Native moddan farkı için bkz. [RealtimeNativeService](RealtimeNativeService.md#2-köprü-modundan-farkı).

## 2. Sorumlulukları

- Ses akışını iki yönde pompalamak; transkripti alıp pipeline'ı tetiklemek.
- Yanıtı seslendirmek ve olayları tarayıcıya iletmek.
- HITL onay bağlamını kurmak (`_approvalContext.SetScope`) — yan etkili tool'lar bu kanalda da
  çalışabildiği için `AuthenticatedCustomerId` buraya taşınmalıdır.
- **Oturuma kimlik bağlamak** (`SessionIdentityBinder.TryBindAsync`); oturum başka bir
  müşteriye aitse bağlantıyı reddetmek.

## 3. `_turnInFlight` — kolay gözden kaçan yarış

`_assistantSpeaking` yetmez. Köprü modunda `create_response=false` olduğu için asistan ancak
pipeline bittikten sonra konuşmaya başlar; yani "kullanıcı sustu" ile "asistan konuşuyor"
arasında **saniyeler süren sessiz bir aralık** vardır.

Bu aralık korumasız bırakıldığında canlıda şu görüldü: mikrofon OpenAI'ye akmaya devam ediyor,
`semantic_vad` sessizlikte tetikleniyor ve transkripsiyon modeli — `TranscriptionPrompt` ile
domain sözlüğüne yönlendirildiği için — boş dönmek yerine makul görünen bir cümle uyduruyordu
(*"Merhaba, müşteri numaram 1025."*). Bu sahte transkript hem sohbete kullanıcı balonu olarak
düşüyor hem de ikinci bir pipeline başlatıp ilk turun olay akışıyla karışıyordu.

`IsBusy = _assistantSpeaking || _turnInFlight` bu pencereyi de kapatır.

> 🐞 **Canlı hata (düzeltildi):** `_turnInFlight` **bağlantı kapsamlıydı** — yalnızca kendi
> WebSocket bağlantısını koruyordu. Aynı oturuma ikinci bir WebSocket açıldığında (ya da
> kullanıcı aynı anda metin sohbetini kullandığında) iki tur PARALEL çalışabiliyordu: aynı
> geçmiş üzerinde iki workflow, aynı state üzerinde iki yazma, aynı oturumda iki HITL akışı.
> `HandleUserTranscriptAsync` artık metin sohbetiyle **aynı** oturum turu kilidini alıyor
> (`SessionIdentityBinder.TurnLockKey(sessionId)`, `IAppDistributedLock` üzerinden) — ses ve
> metin de birbirine göre sıraya girer. Bekleme süresi (30sn) metindekinden (120sn) bilinçli
> olarak kısa: sesli kullanıcı karşısında sessizlikle bekleyemez.

## 4. Bilinen risk — sesli metinde sayılar

Bu modda reasoning çalıştığı için `IdExtractor` devrededir ve **4+ ardışık rakam** arar
(`\b(\d{4,})\b`). STT "1030" yazarsa sorun yok; "bin otuz" gibi Türkçe sayı sözcüğü yazarsa
hiçbir şey eşleşmez. Native modda bu risk yoktur (orada reasoning yok).

## Bağlantılar

- [RealtimeNativeService.md](RealtimeNativeService.md) — Diğer sesli mod
- [../../CustomerSupportBot.Adapters.AI/Realtime.md](../../CustomerSupportBot.Adapters.AI/Realtime.md) — Taşıyıcı ve kimlik doğrulama
- [../../CustomerSupportBot.Domain/Services/IdExtractor.md](../../CustomerSupportBot.Domain/Services/IdExtractor.md) — ID çıkarımı
- [../../security.md](../../security.md#oturum--müşteri-bağı) — Oturum sahipliği
