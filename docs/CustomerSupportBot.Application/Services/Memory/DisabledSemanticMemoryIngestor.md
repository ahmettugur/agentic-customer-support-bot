# DisabledSemanticMemoryIngestor

**Dosya:** `Services/Memory/DisabledSemanticMemoryIngestor.cs`
**Tür:** `public sealed class : ISemanticMemoryIngestor`
**Namespace:** `CustomerSupportBot.Application.Services.Memory`

## 1. Ne İşe Yarar

`ISemanticMemoryIngestor`'ın **Null Object** implementasyonu — semantik bellek (vektör depo)
yapılandırma ile devre dışı bırakıldığında DI konteynerine kaydedilir. Her metodu no-op'tur
(hiçbir şey yapmaz, boş/varsayılan sonuç döner).

## 2. Hangi Amaçla Kullanılır

`SemanticMemoryOptions.Enabled = false` (veya Qdrant yapılandırılmamış) olduğunda, DI
`ISemanticMemoryIngestor` için gerçek implementasyon (Qdrant tabanlı) yerine bu sınıfı kaydeder.
Bu sayede semantik belleği kullanan tüm kod (`SemanticMemoryService`,
`KnowledgeBaseIngestionService`) **hiçbir `if (bellek açık mı?)` kontrolü yazmadan** normal
akışını sürdürür.

## 3. Sorumlulukları

- **Üstlendiği:** `ISemanticMemoryIngestor` sözleşmesini zararsız biçimde yerine getirmek.
- **Üstlenmediği:** Herhangi bir gerçek iş — bilinçli olarak hiçbir şey yapmaz.

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- `ISemanticMemoryIngestor` port'unu implemente eder.
- **Alternatifi:** Adapters.AI katmanındaki gerçek Qdrant tabanlı implementasyon
  (`QdrantVectorMemoryAdapter` üzerine kurulu gerçek ingestor).
- **Kimin tarafından kullanılır:** Bu sınıfı doğrudan çağıran yoktur — her şey
  `ISemanticMemoryIngestor` arayüzü üzerinden, hangi implementasyonun kayıtlı olduğunu bilmeden çalışır.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

**Neden Null Object Pattern, "service locator" veya dağınık `if (enabled)` kontrolleri değil:**
Kod içi yorumda açıkça belirtilir: bu, **service locator anti-pattern'ı yerine açık bir
null-object kaydı** kullanma tercihidir. Alternatif yaklaşım — her çağıran noktada
`if (_options.Enabled) { ... } else { /* no-op */ }` yazmak — hem tekrarlıdır hem de bir
noktanın bu kontrolü unutması durumunda `NullReferenceException`'a (veya yapılandırılmamış bir
Qdrant bağlantısına bağlanmaya çalışma hatasına) yol açabilirdi. Null Object deseni, "kapalı"
durumu bizzat bir implementasyon haline getirerek bu riski DI kayıt seviyesinde, TEK bir yerde
çözer — geri kalan tüm kod her zaman "gerçek bir ingestor var" varsayımıyla yazılabilir.

`Enabled => false` ve `IsConfigured => false` döndürmesi, çağıran tarafların (isterlerse) bu
durumu ayırt edip farklı davranabilmesini de mümkün kılar — ama zorunlu değildir, metotlar
zaten güvenle çağrılabilir.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `Enabled` (`bool`) | Her zaman `false`. |
| `IsConfigured` (`bool`) | Her zaman `false`. |
| `EnsureCollectionsAsync(...)` | No-op, `Task.CompletedTask`. |
| `UpsertManyAsync(...)` | No-op. |
| `DeleteAsync(...)` | No-op. |
| `DeleteStaleAsync(...)` | No-op. |
| `CountAsync(...): Task<long>` | Her zaman `0`. |

## 7. Bağımlılıklar

Yok.

## Bağlantılar

- [ISemanticMemoryIngestor.md](ISemanticMemoryIngestor.md) — implemente ettiği arayüz
- [SemanticMemoryService.md](SemanticMemoryService.md) — bu arayüzü kullanan ana tüketici
