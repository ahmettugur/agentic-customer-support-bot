# CustomerSupportBot.Adapters.Persistence.FileSystem

Bu klasör, disk üzerindeki dosya sisteminden (Markdown dosyaları) veri okuyan adaptörleri barındırır.

## Dosyalar

- [FileSystemPromptRepository](FileSystemPromptRepository.md) — [IPromptRepository](../../CustomerSupportBot.Application/Ports/Outbound/IPromptRepository.md) portunu uygulayan; `Prompts/` dizinindeki Markdown şablonlarını uygulama açılışında belleğe yükleyen ve `{{DEĞİŞKEN}}` formatındaki yer tutucuları ikame eden (Render) prompt ambarı.
- [FileSystemKnowledgeBaseSource](FileSystemKnowledgeBaseSource.md) — [IKnowledgeBaseSource](../../CustomerSupportBot.Application/Ports/Outbound/AI/IKnowledgeBaseSource.md) portunu uygulayan; `KnowledgeBase/` dizinindeki `.md` dokümanlarını okuyup RAG vektörleştirme hattına sunan kaynak.
