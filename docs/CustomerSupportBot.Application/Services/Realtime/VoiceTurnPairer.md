# VoiceTurnPairer

**Kaynak:** `Services/Realtime/VoiceTurnPairer.cs`
**Tür:** `internal sealed class` (+ `internal readonly record struct VoiceTurn(string UserSide, string BotText)`)
**Namespace:** `CustomerSupportBot.Application.Services.Realtime`

## 1. Ne İşe Yarar

Native sesli modda bir turun iki yarısını — kullanıcının söylediği (transkript) ve asistanın
yanıtı — doğru şekilde eşleştirir ve turları geçmişe **konuşma sırasıyla** verir.

## 2. Hangi Amaçla Kullanılır

[RealtimeNativeService](RealtimeNativeService.md)'in olay döngüsü her ilgili OpenAI olayında
eşleştiriciyi çağırır; eşleştiricinin döndürdüğü hazır turlar chat bridge'e ve oturum geçmişine
yazılır, ardından duygu değerlendirmesi yapılır.

## 3. Sorumlulukları

- **Üstlendiği:** Transkripti ve yanıtı kullanıcı ses öğesi kimliğiyle (`item_id`) eşleştirmek;
  tool takip yanıtını aynı tura bağlamak; turları sırayla bırakmak; başarısız/reddedilen
  transkriptleri ve kaybolan transkriptleri ele almak; bağlantı kapanırken bekleyenleri boşaltmak.
- **Üstlenmediği:** Kalıcılık, tarayıcıya gönderim, girdi güvenliği kararı — bunlar
  `RealtimeNativeService`'tedir.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

Yalnızca `RealtimeNativeService` kullanır. Kimlikleri
[OpenAiRealtimeClientAdapter](../../../CustomerSupportBot.Adapters.AI/Realtime/OpenAiRealtimeClientAdapter.md)
`RealtimeServerEvent.ItemId` olarak taşır (bkz. [RealtimeModels](../../Ports/Outbound/AI/RealtimeModels.md)).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

OpenAI Realtime'da transkripsiyon yanıt üretimiyle paralel çalışır; transkript yanıttan önce,
ortasında ya da `response.done`'dan sonra gelebilir. "Son gelen transkript" ile eşleştirmek geç
gelen transkriptte turu bir önceki cümleyle kaydediyordu. Eşleştirme kuralları:

- **Transkript → öğe:** transkript olayı ait olduğu öğenin `item_id`'sini taşır.
- **Yanıt → öğe:** yanıt, başladığı anda (`response.created`) son commit edilmiş
  (`input_audio_buffer.committed`) kullanıcı öğesine aittir.
- **Tool takip yanıtı:** tool sonuçları gönderildikten sonra başlayan yanıt aynı kullanıcı turuna
  aittir — arada kullanıcı yeniden konuşmuş olsa bile.
- **Sıra:** turlar geldikleri sırayla bırakılır; transkripti henüz gelmemiş bir turun arkasındakiler
  bekler, böylece geçmişte sıra bozulmaz.
- **Kayıp transkript:** baş tur `MaxPendingTurns` (3) turdan uzun beklerse transkript kaybolmuş
  sayılır ve yer tutucuyla (`(sesli)`) bırakılır — geçmiş süresiz askıda kalmaz.
- **Başarısız/boş transkript:** tur yer tutucuyla bırakılır.
- **Reddedilen transkript:** o öğenin turu hiç yazılmaz — reddedilen metin sonraki turlarda
  ajanın bağlamına girmemeli.
- **Kimliksiz akış:** yanıta bağlanacak öğe yoksa (kimlik taşımayan adaptör, oturum başı karşılama
  yanıtı) eski davranış korunur: son transkript ya da yer tutucu.

Hatırlanan transkript/ret kayıtları son 32 öğeyle sınırlıdır. Tek bir olay döngüsünden çağrılır;
thread-safe değildir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `AudioCommitted(itemId)` | Son commit edilmiş kullanıcı öğesini kaydeder. |
| `ResponseCreated(): bool` | Yanıtı öğeye bağlar; tool takip yanıtıysa `true` (aynı tur). |
| `ToolCallsDispatched(followUpExpected)` | Sıradaki yanıtın takip yanıtı olacağını işaretler. |
| `CurrentTranscript(legacy)` | Şu anki yanıtın ait olduğu kullanıcı cümlesi (onay kaydı/eskalasyon için). Kimlik yoksa `legacy`; kimlik var ama transkript gelmediyse `null` — önceki turun cümlesi yazılmasın. |
| `ResponseCancelled()` | Takip beklentisini sıfırlar. |
| `ResponseCompleted(botText, legacyUserSide)` | Yanıt metnini kuyruğa ekler; hazır turları döner. |
| `TranscriptArrived(itemId, transcript)` | Transkripti öğeye yazar; hazır turları döner. |
| `TranscriptFailed(itemId)` | Yer tutucuyla çözer. |
| `TranscriptRejected(itemId)` | O öğenin turunu düşürür; arkasında bekleyenleri serbest bırakır. |
| `DrainAll()` | Bağlantı kapanırken tüm bekleyenleri (eksik transkript → yer tutucu) döner. |
| `Placeholder` *(const)* | `"(sesli)"`. |
| `MaxPendingTurns` *(const)* | `3`. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [RealtimeNativeService](RealtimeNativeService.md)
- Testler: `tests/CustomerSupportBot.Application.Tests/VoiceTurnPairerTests.cs`,
  `RealtimeNativeTurnPairingTests.cs`
