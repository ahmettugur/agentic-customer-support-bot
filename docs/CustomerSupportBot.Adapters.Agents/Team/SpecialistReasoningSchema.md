# SpecialistReasoningSchema

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/Team/SpecialistReasoningSchema.cs`
- **Tür:** `internal sealed class` Tanımları
- **Namespace:** `CustomerSupportBot.Adapters.Agents.Team`

## Ne işe yarar?

`SpecialistReasoningSchema`, OpenAI/Azure OpenAI gibi sağlayıcılarda `ChatResponseFormat.ForJsonSchema<SpecialistReasoningSchema>` kullanılarak uzman ajanların (`ProductAgent`, `OrderAgent`, `ComplaintAgent`, `HumanHandoffAgent`) çıktılarını strict JSON şemasına zorlamak için kullanılan veri transfer nesneleridir.

## Hangi amaçla kullanılır`?

Uzman ajanların serbest metin yerine araç çağırmadan önceki muhakemesini (`PreToolCheckSchema`) ve araç sonrasındaki değerlendirmesini (`PostToolReflectionSchema`) kesin ve güvenilir bir JSON yapısında üretmesini sağlamak amacıyla kullanılır.

## Kullanılma nedeni ve tasarım yaklaşımı

`Domain.Model.SpecialistReasoning`/`PreToolCheck`/`PostToolReflection` BİLİNÇLİ OLARAK yeniden kullanılmadı: o tiplerde `PostToolReflection.StatusEnum` gibi salt-okunur computed property'ler var ve bunlar JSON schema'ya sızıp modele anlamsız/gereksiz bir alan gösterirdi. Bu şema sınıfları yalnızca LLM'e sunulacak JSON şeklini tanımlar; gerçek ayrıştırma hâlâ `SpecialistReasoningParser` üzerinden yapılır — sağlayıcı strict schema'yı tam onurlandırmazsa parser'ın savunmacı (fence/alan bazlı) mantığı yedek güvence olarak devrededir.

`SpecialistReasoningSchemaOptions.CamelCase` — `JsonSerializerOptions` ile alan adlarını `camelCase`'e çeviren paylaşımlı ayar; şemanın OpenAI'ye gönderilen JSON'da C# `PascalCase` değil beklenen `camelCase` anahtarlarıyla görünmesini sağlar.

`PreToolCheckSchema.SelectedTool` yalnızca `OrderAgent` tarafından kullanılır (6 aday tool arasından seçim gerekçesini netleştirmek için); diğer ajanların promptları bu alandan bahsetmez, model boş bırakır. `OptionalParams` Complaint/HumanHandoff promptlarında kullanılır (ör. `customer_id` — otomatik türetildiği için `missingParams`'a sayılmaz); parser tarafından okunmaz, yalnızca modelin kendi muhakemesi içindir.

## Şema Sınıfları ve Alanlar

| Sınıf | Alanlar | Açıklama |
|---|---|---|
| `SpecialistReasoningSchema` | `PreToolCheck, ResultConfidence, ResultNotes, PostToolReflection` | Uzman ajanın ana ReAct JSON kök şeması. |
| `PreToolCheckSchema` | `SelectedTool, RequiredParams, OptionalParams, CollectedParams, MissingParams, CanProceed, Reasoning, Confidence` | Araç öncesi parametre ve ilerleme denetimi. |
| `PostToolReflectionSchema` | `TaskComplete, Status, HandoffSuggestion, HandoffReason, MissingContext, Summary` | Araç sonrası görev tamamlama veya devir kararı. |

## Bağımlılıklar

- `System.Text.Json`
