# AiProviderOptions

- **Kaynak:** `CustomerSupportBot.Adapters.AI/Options/AiProviderOptions.cs`
- **Tür:** `public sealed class` & `public enum` Modelleri
- **Namespace:** `CustomerSupportBot.Adapters.AI`

## Ne işe yarar?

`AiProviderOptions.cs`, `appsettings.json` içerisindeki `"AI"` yapılandırma bölümünü (`AiOptions.SectionName = "AI"`) strongly-typed C# nesnelerine bağlayan seçenek modellerini barındırır.

## Hangi amaçla kullanılır`?

- Aktif yapay zeka sağlayıcısını (`AiProvider`: `OpenAI` veya `AzureOpenAI`) seçmek.
- OpenAI için API anahtarı, standart sohbet modeli (`Model`), muhakeme modeli (`ReasoningModel`) ve muhakeme eforunu (`ReasoningEffort`) yapılandırmak.
- Azure OpenAI için Endpoint, API Key, Deployment ve ReasoningDeployment değerlerini ayarlamak.
- Realtime WebSocket ses oturumu için model (`Model`), ses tonu (`Voice`), VAD sessizlik süresi (`VadSilenceMs`) ve ASR transkripsiyon prompt'unu (`TranscriptionPrompt`) yapılandırmak.

## Sınıflar ve Alanlar

### 1. `AiProvider` (Enum)
- `OpenAI`: Genel OpenAI API'si.
- `AzureOpenAI`: Kurumsal Azure OpenAI Service dağıtımı.

### 2. `AiOptions` (Kök Sınıf - Section: "AI")
- `Provider` (`AiProvider`): Aktif sağlayıcı.
- `OpenAI` (`OpenAiOptions`): OpenAI ayarları.
- `AzureOpenAI` (`AzureOpenAiOptions`): Azure OpenAI ayarları.
- `Realtime` (`RealtimeOptions`): Gerçek zamanlı ses ayarları.

### 3. `RealtimeOptions`
- `Enabled` (`bool`): Ses özelliğinin açık olup olmadığı (varsayılan: `true`).
- `Model` (`string`): Realtime modeli (varsayılan: `gpt-realtime-2`).
- `Voice` (`string`): Konuşma sesi (ör. `alloy`).
- `VadSilenceMs` (`int`): VAD konuşma bitişi bekleme süresi (varsayılan: `600ms`).
- `TranscriptionPrompt` (`string`): ASR transkripsiyonuna verilen genel bağlam talimatı (halüsinasyonu önlemek için somut örnek numara içermez).

### 4. `OpenAiOptions` & `AzureOpenAiOptions`
- API anahtarları, model adları, dağıtım isimleri ve muhakeme efor seviyeleri.
