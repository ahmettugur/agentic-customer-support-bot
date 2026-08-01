# SpecialistReasoningSchema

**Dosya:** `CustomerSupportBot.Adapters.Agents/Team/SpecialistReasoningSchema.cs`
**Erişim:** `internal` (3 sınıf: `SpecialistReasoningSchema`, `PreToolCheckSchema`, `PostToolReflectionSchema` + `internal static class SpecialistReasoningSchemaOptions`)

## Ne işe yarar?

`ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<T>(...)` ile 4 specialist ajanın (`ProductAgent`, `OrderAgent`, `ComplaintAgent`, `HumanHandoffAgent`) çıktısını OpenAI/Azure OpenAI strict JSON schema moduna zorlamak için kullanılan DTO'lardır.

## Hangi amaçla kullanılır?

Her specialist ajanın `BuildInner` static metodunda `ChatOptions.ResponseFormat = ChatResponseFormat.ForJsonSchema<SpecialistReasoningSchema>(SpecialistReasoningSchemaOptions.CamelCase)` olarak set edilir.

## Sorumlulukları

- Specialist çıktısının beklenen JSON şeklini tanımlamak: `preToolCheck`, `resultConfidence`, `resultNotes`, `postToolReflection`.
- `SpecialistReasoningSchemaOptions.CamelCase`: `JsonNamingPolicy.CamelCase` ile C# `PascalCase` property adlarını (`DetectedIntent` vb.) JSON'da beklenen `camelCase` adlara (`detectedIntent`) çevirmek.

**Üstlenmediği işler:** Gerçek JSON parse (`SpecialistReasoningParser` yapar — bu şema yalnızca modelin ne üreteceğini kısıtlar, üretileni okumaz).

## Diğer katman ve bileşenlerle ilişkileri

**Kimler kullanır:** `Team/ProductAgent.cs`, `Team/OrderAgent.cs`, `Team/ComplaintAgent.cs`, `Team/HumanHandoffAgent.cs` (hepsi `BuildInner`'da referans verir).

**İlişkili olduğu ama BİLEREK yeniden kullanmadığı tipler:** `CustomerSupportBot.Domain.Model.SpecialistReasoning`/`PreToolCheck`/`PostToolReflection` — bkz. aşağıdaki tasarım notu. Asıl parse `CustomerSupportBot.Domain.Services.SpecialistReasoningParser` üzerinden yapılır.

## Kullanılma nedeni ve tasarım yaklaşımı

**Neden domain modelleri (`SpecialistReasoning`/`PreToolCheck`/`PostToolReflection`) doğrudan yeniden kullanılmadı:** `PostToolReflection.StatusEnum` gibi salt-okunur computed property'ler, reflection tabanlı şema üretiminde JSON şemaya sızıp modele anlamsız/redundan bir alan (`statusEnum`) gösterirdi. Bu yüzden bu dosya, yalnızca beklenen JSON şeklini tanımlayan, hiçbir computed property taşımayan saf DTO'lar içerir.

**`SelectedTool` ve `OptionalParams` alanları neden var:** Bu iki alan `SpecialistReasoningParser` tarafından hiç okunmaz — yalnızca bazı ajanların promptlarında (özellikle `OrderAgent`'ın 6 aday tool'u arasından seçim gerekçesini netleştirmesi için `selectedTool`; `ComplaintAgent`/`HumanHandoffAgent`'ın `customer_id` gibi otomatik türetilen alanları `missingParams`'a saymaması için `optionalParams`) model muhakemesini netleştiren reasoning-scaffold alanlarıdır. Şemadan çıkarılmadılar çünkü strict mode altında modelin bu ek alanları hâlâ (isteğe bağlı olarak) üretebilmesi model doğruluğuna katkı sağlıyor.

**Anthropic uyarısı:** `Microsoft.Agents.AI.Anthropic` bridge'i `ChatOptions.ResponseFormat`'ı hiç okumuyor (decompile ile doğrulandı) — o path'te bu şema sessizce no-op olur, `SpecialistReasoningParser`'ın defensive fence-temizleme + alan-bazlı parse mantığı tek güvence olarak kalır (bu yüzden hiç kaldırılmadı).

## Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `SpecialistReasoningSchemaOptions.CamelCase` (static readonly) | `JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }`. |
| `SpecialistReasoningSchema.PreToolCheck` (`PreToolCheckSchema?`) | Tool çağrısı öncesi parametre/karar muhakemesi. |
| `SpecialistReasoningSchema.ResultConfidence` (`double`) | Tool sonrası güven skoru (0.0–1.0). |
| `SpecialistReasoningSchema.ResultNotes` (`string`) | Sonuç özeti. |
| `SpecialistReasoningSchema.PostToolReflection` (`PostToolReflectionSchema?`) | Tool sonrası reflection — durum, handoff önerisi, özet. |
| `PreToolCheckSchema.SelectedTool` (`string?`) | Yalnızca `OrderAgent` kullanır (reasoning-scaffold, parser okumaz). |
| `PreToolCheckSchema.RequiredParams`/`CollectedParams`/`MissingParams` (`List<string>`) | Parametre durumu. |
| `PreToolCheckSchema.OptionalParams` (`List<string>`) | Reasoning-scaffold, parser okumaz. |
| `PreToolCheckSchema.CanProceed` (`bool`) | Tool çağrılabilir mi. |
| `PreToolCheckSchema.Reasoning` (`string`) / `Confidence` (`double`) | Karar gerekçesi ve güven skoru. |
| `PostToolReflectionSchema.TaskComplete` (`bool`) / `Status` (`string`) | Görev tamamlanma durumu (`done`/`needs_followup`/`needs_escalation`/`failed`/`partial`). |
| `PostToolReflectionSchema.HandoffSuggestion` (`string?`) / `HandoffReason` (`string`) | Dinamik handoff önerisi. |
| `PostToolReflectionSchema.MissingContext` (`List<string>`) / `Summary` (`string`) | Eksik bağlam ve kısa özet. |
