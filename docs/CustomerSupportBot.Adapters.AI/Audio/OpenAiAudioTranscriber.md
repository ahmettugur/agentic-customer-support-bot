# OpenAiAudioTranscriber

- **Kaynak:** `CustomerSupportBot.Adapters.AI/Audio/OpenAiAudioTranscriber.cs`
- **Tür:** `public sealed class : IAudioTranscriber`
- **Ayar:** `VoiceCall:TranscriptionModel` (varsayılan `gpt-4o-transcribe`), `VoiceCall:TranscriptionLanguage` (`tr`)

## Ne işe yarar?

Sesli görüşme kayıt parçasını (`audio/webm`) OpenAI ses dökümü API'siyle yazıya döker.

`AudioClient` tembel üretilir (`Func<AudioClient>`, `AiClientFactory.CreateAudioClient`): anahtar
tanımlı değilse uygulama yine açılır, yalnızca döküm denemeleri başarısız olur. Anahtar sırası:
`AI:Realtime:ApiKey` doluysa o, değilse `AI:OpenAI:ApiKey`; Azure sağlayıcısında model adı dağıtım
adıdır.
