# CustomerSupportBot.Adapters.AI.Chat

Bu klasör, standart LLM sohbet, özetleme ve o-serisi akıl yürütme (Reasoning) modelleri için istemci adaptörlerini ve sağlayıcı fabrikasını barındırır.

## Dosyalar

- [AiClientFactory](AiClientFactory.md) — Sağlayıcı türüne (`OpenAI`, `AzureOpenAI`) göre standart `IChatClient` ve `ReasoningChatClient` nesnelerini üreten fabrika.
- [GeneralChatClientAdapter](GeneralChatClientAdapter.md) — [IGeneralChatClient](../../CustomerSupportBot.Application/Ports/Outbound/AI/IGeneralChatClient.md) portunu uygulayan ve `ConversationMessage` ↔ `ChatMessage` dönüşümünü yöneten genel amaçlı LLM adaptörü.
- [ReasoningChatClient](ReasoningChatClient.md) — [IReasoningChatClient](../../CustomerSupportBot.Application/Ports/Outbound/AI/IReasoningChatClient.md) portunu uygulayan ve `reasoning_effort` parametresiyle derin muhakeme yürüten adaptör.
