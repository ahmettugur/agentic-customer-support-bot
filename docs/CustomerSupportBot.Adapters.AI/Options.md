# AiProviderOptions

**Dosya:** `Options/AiProviderOptions.cs`

`appsettings.json > "AI"` bölümünden bind edilir.

---

## Üst seviye yapı

```csharp
public sealed class AiOptions
{
    public const string SectionName = "AI";

    public AiProvider Provider { get; set; }              // Aktif sağlayıcı
    public OpenAiOptions OpenAI { get; set; } = new();
    public AzureOpenAiOptions AzureOpenAI { get; set; } = new();
    public RealtimeOptions Realtime { get; set; } = new();
}

public enum AiProvider { OpenAI, AzureOpenAI }
```

**Tek aktif sağlayıcı:** `Provider` değeri AiClientFactory'nin hangi SDK'yı kullanacağını belirler.

---

## OpenAiOptions

```csharp
public sealed class OpenAiOptions
{
    public string? ApiKey { get; set; }
    public string? Model { get; set; }                    // Standart chat modeli
    public string? ReasoningModel { get; set; }           // o-series reasoning
    public string? ReasoningEffort { get; set; }          // "low" | "medium" | "high"
}
```

| Alan | Örnek |
|---|---|
| `ApiKey` | `sk-...` |
| `Model` | `gpt-4o-mini`, `gpt-4-turbo` |
| `ReasoningModel` | `o1-mini`, `o1-preview` |
| `ReasoningEffort` | `medium` (o-series için derin düşünme miktarı) |

`ReasoningModel` boşsa `Model` kullanılır (her model "reasoning effort" parametresini kabul etmez; OpenAI sessizce yutabilir).

---

## AzureOpenAiOptions

```csharp
public sealed class AzureOpenAiOptions
{
    public string? Endpoint { get; set; }                 // https://...openai.azure.com
    public string? ApiKey { get; set; }
    public string? Deployment { get; set; }               // Standart deployment adı
    public string? ReasoningDeployment { get; set; }      // Reasoning deployment
    public string? ReasoningEffort { get; set; }
}
```

Azure'da `Model` yerine `Deployment` (Azure portal'de tanımlanmış deployment adı). Üç gerekli alan: `Endpoint`, `ApiKey`, `Deployment`.

---

## RealtimeOptions

```csharp
public sealed class RealtimeOptions
{
    public bool Enabled { get; set; } = true;
    public string Model { get; set; } = "gpt-realtime-2";
    public string? ApiKey { get; set; }                     // Boşsa OpenAiOptions.ApiKey
    public string Voice { get; set; } = null!;              // zorunlu — appsettings'te belirtilmeli
    public int VadSilenceMs { get; set; } = 600;
    public string ReasoningEffort { get; set; } = "low";
    public int MaxResponseTokens { get; set; } = 4096;

    // Transcription
    public string TranscriptionModel { get; set; } = "gpt-4o-transcribe";
    public string? TranscriptionLanguage { get; set; } = "tr";  // null = dil algılama
    public string TranscriptionPrompt { get; set; } = "Müşteri destek görüşmesi..."; // domain terimlerini öğretir
}
```

### Önemli alanlar

| Alan | Açıklama |
|---|---|
| `Enabled` | False ise Realtime endpoint 503 döner |
| `Model` | OpenAI realtime modeli — Realtime API'ya özel |
| `Voice` | `alloy`, `echo`, `shimmer`, ... (zorunlu) |
| `VadSilenceMs` | Voice Activity Detection — sessizlik eşiği. Kullanıcı bu süre kadar sessiz kalınca konuşmayı bitti say |
| `TranscriptionLanguage` | `"tr"` Türkçe ASR'ye bias verir — model rastgele dil seçmez |
| `TranscriptionPrompt` | ASR için bağlam metni — domain terimlerini (ürün adı, sipariş ID formatı) önceden öğretir; transkripsiyon doğruluğu artar |

`ApiKey` opsiyonel — boşsa Realtime adapter `OpenAiOptions.ApiKey`'i kullanır. Bu sayede iki ayrı API key yönetmek gerekmiyor.

---

## Validation

`AiClientFactory.Require()` helper'ı eksik alan tespit eder:

```csharp
static string Require(string? value, string keyName)
{
    if (string.IsNullOrWhiteSpace(value))
        throw new InvalidOperationException(
            $"AI configuration eksik: {keyName} ayarlanmalı");
    return value;
}
```

Startup'ta hangi sağlayıcı seçilmişse onun alanları kontrol edilir. Yanlış config → uygulama başlamaz.

---

## Tam örnek

```json
{
  "AI": {
    "Provider": "OpenAI",
    "OpenAI": {
      "ApiKey": "sk-...",
      "Model": "gpt-4o-mini",
      "ReasoningModel": "o1-mini",
      "ReasoningEffort": "medium"
    },
    "AzureOpenAI": {
      "Endpoint": "https://contoso.openai.azure.com",
      "ApiKey": "...",
      "Deployment": "gpt-4o-mini",
      "ReasoningDeployment": "o1-mini",
      "ReasoningEffort": "high"
    },
    "Realtime": {
      "Enabled": true,
      "Model": "gpt-realtime-2",
      "Voice": "alloy",
      "VadSilenceMs": 600,
      "ReasoningEffort": "low",
      "TranscriptionLanguage": "tr",
      "TranscriptionPrompt": "Müşteri destek konuşması. Sipariş ID'leri 1042 formatında..."
    }
  }
}
```

---

## Provider değiştirme

Production'da provider'ı değiştirmek **kod değişikliği gerektirmez**:

```bash
# Env var override
AI__Provider=AzureOpenAI
AI__AzureOpenAI__Endpoint=https://contoso.openai.azure.com
AI__AzureOpenAI__ApiKey=newkey
```

Uygulamayı yeniden başlat — `Adapters.AI` yeni sağlayıcıya geçer. Application/Domain hiç etkilenmez (port'lar aynı).
