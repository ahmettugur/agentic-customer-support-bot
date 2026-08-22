# AiClientFactory

- **Kaynak:** `CustomerSupportBot.Adapters.AI/Chat/AiClientFactory.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Adapters.AI.Chat`

## Ne işe yarar?

`AiClientFactory`, `appsettings.json` içerisindeki strongly-typed [AiOptions](../Options/AiProviderOptions.md) yapılandırmasına göre OpenAI ve Azure OpenAI sağlayıcıları arasında geçişi yöneten, standart sohbet için `Microsoft.Extensions.AI.IChatClient` ve akıl yürütme için [ReasoningChatClient](ReasoningChatClient.md) nesnelerini üreten statik fabrikadır.

## Hangi amaçla kullanılır`?

- **Sağlayıcı Soyutlaması:** Uygulamanın geri kalanının OpenAI veya Azure OpenAI SDK detaylarından bağımsız kalmasını sağlamak.
- **Zorunlu Alan Doğrulaması (Fail-Fast):** API anahtarı (`ApiKey`), uç nokta (`Endpoint`), model adı (`Model`) veya dağıtım adı (`Deployment`) tanımlanmamışsa uygulama başlangıcında açıklayıcı `InvalidOperationException` fırlatarak çökmeleri erkenden yakalamak (`Require` metodu).
- **Dekorasyon Desteği (OpenTelemetry / Logging):** `CreateReasoningChatClient` çağrılırken dışarıdan bir `Func<IChatClient, IChatClient>` ile istemciye telemetri veya günlükleme middleware'i ekleyebilmek.

## Sorumlulukları

- **Üstlendiği:**
  - `CreateStandardChatClient` ile standart `IChatClient` üretmek.
  - `CreateReasoningChatClient` ile `ReasoningChatClient` üretmek.
  - OpenAI için `OpenAIClient.GetChatClient(model).AsIChatClient()` çağrısını yapmak.
  - Azure OpenAI için `AzureOpenAIClient.GetChatClient(deployment).AsIChatClient()` çağrısını yapmak.
  - Eksik yapılandırmaları `Require` ile doğrulamak.
- **Üstlenmediği:**
  - LLM çağrılarını doğrudan koşturmak (bu istemci adaptörlerindedir).

## Metotlar ve İç Çalışma Mantıkları

### 1. `CreateStandardChatClient`
```csharp
public static IChatClient CreateStandardChatClient(AiOptions options)
```
- **Ne işe yarar?:** Standart çok turlu sohbet ve uzman ajanlar için `IChatClient` üretir.
- **İç Mantığı:**
  - `options.Provider` değeri `AiProvider.AzureOpenAI` ise `CreateAzureChatClient(options.AzureOpenAI, ...)` çağrılır.
  - Aksi halde `CreateOpenAIChatClient(options.OpenAI, ...)` çağrılır.
  - Model/Deployment parametreleri `Require` ile denetlenir.

### 2. `CreateReasoningChatClient`
```csharp
public static ReasoningChatClient CreateReasoningChatClient(
    AiOptions options,
    Func<IChatClient, IChatClient>? decorate = null)
```
- **Ne işe yarar?:** o1, o3-mini gibi muhakeme modelleri için [ReasoningChatClient](ReasoningChatClient.md) üretir.
- **İç Mantığı:**
  1. `decorate ??= c => c` ile varsayılan passthrough fonksiyonu atanır.
  2. Sağlayıcı Azure ise: `ReasoningDeployment` (yoksa `Deployment`) ve `ReasoningEffort` okunur, Azure istemcisi oluşturulup `decorate` edilir ve `new ReasoningChatClient(client, deployment, effort)` döndürülür.
  3. Sağlayıcı OpenAI ise: `ReasoningModel` (yoksa `Model`) ve `ReasoningEffort` okunur, OpenAI istemcisi oluşturulup `decorate` edilir ve `new ReasoningChatClient(client, model, effort)` döndürülür.

### 3. `CreateOpenAIChatClient` (Private Static)
```csharp
private static IChatClient CreateOpenAIChatClient(OpenAiOptions options, string model)
```
- **Ne işe yarar?:** OpenAI SDK üzerinden `IChatClient` kurar.
- **İç Mantığı:** `options.ApiKey` kontrol edilir. `new OpenAIClient(options.ApiKey).GetChatClient(model).AsIChatClient()` çağrılır.

### 4. `CreateAzureChatClient` (Private Static)
```csharp
private static IChatClient CreateAzureChatClient(AzureOpenAiOptions options, string deployment)
```
- **Ne işe yarar?:** Azure OpenAI SDK üzerinden `IChatClient` kurar.
- **İç Mantığı:** `options.Endpoint` ve `options.ApiKey` denetlenir. `new AzureOpenAIClient(new Uri(options.Endpoint), new ApiKeyCredential(options.ApiKey)).GetChatClient(deployment).AsIChatClient()` çağrılır.

### 5. `Require` (Private Static)
- **Ne işe yarar?:** Verilen dizgenin boş olup olmadığını kontrol eder. Boşsa hangi ayar anahtarının (`key`) eksik olduğunu bildiren `InvalidOperationException` fırlatır.

## Bağımlılıklar

- `Microsoft.Extensions.AI.IChatClient`
- `Azure.AI.OpenAI.AzureOpenAIClient`
- `OpenAI.OpenAIClient`
- [AiOptions](../Options/AiProviderOptions.md)
- [ReasoningChatClient](ReasoningChatClient.md)
