# SpecialistReasoningSchema

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/Team/SpecialistReasoningSchema.cs`
- **Tür:** `internal sealed class` Tanımları
- **Namespace:** `CustomerSupportBot.Adapters.Agents.Team`

## Ne işe yarar?

`SpecialistReasoningSchema`, OpenAI/Azure OpenAI gibi sağlayıcılarda `ChatResponseFormat.ForJsonSchema<SpecialistReasoningSchema>` kullanılarak uzman ajanların (`ProductAgent`, `OrderAgent`, `ComplaintAgent`, `HumanHandoffAgent`) çıktılarını strict JSON şemasına zorlamak için kullanılan veri transfer nesneleridir.

## Hangi amaçla kullanılır`?

Uzman ajanların serbest metin yerine araç çağırmadan önceki muhakemesini (`PreToolCheckSchema`) ve araç sonrasındaki değerlendirmesini (`PostToolReflectionSchema`) kesin ve güvenilir bir JSON yapısında üretmesini sağlamak amacıyla kullanılır.

## Şema Sınıfları ve Alanlar

| Sınıf | Alanlar | Açıklama |
|---|---|---|
| `SpecialistReasoningSchema` | `PreToolCheck, ResultConfidence, ResultNotes, PostToolReflection` | Uzman ajanın ana ReAct JSON kök şeması. |
| `PreToolCheckSchema` | `SelectedTool, RequiredParams, OptionalParams, CollectedParams, MissingParams, CanProceed, Reasoning, Confidence` | Araç öncesi parametre ve ilerleme denetimi. |
| `PostToolReflectionSchema` | `TaskComplete, Status, HandoffSuggestion, HandoffReason, MissingContext, Summary` | Araç sonrası görev tamamlama veya devir kararı. |

## Bağımlılıklar

- `System.Text.Json`
