# DisabledSemanticMemoryIngestor

## Ne İşe Yarar
Semantik bellek devre dışı olduğunda DI'a kaydedilen no-op (Null Object) `ISemanticMemoryIngestor` implementasyonudur.

## Hangi Amaçla Kullanılır
Qdrant/embedding konfigürasyonu yapılmadığında veya bilinçli olarak devre dışı bırakıldığında, servis locator anti-pattern'ı yerine açık bir null-object kaydı kullanılır.

## Sorumlulukları
- `Enabled` ve `IsConfigured` → `false` döndürmek.
- Tüm write metotlarında (`EnsureCollectionsAsync`, `UpsertManyAsync`, `DeleteAsync`) hiçbir şey yapmamak.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Implements**: `ISemanticMemoryIngestor` (Domain arayüzü).
- **DI kaydı**: `ApplicationServiceCollectionExtensions` — Qdrant yapılandırılmadığında bu kaydedilir.
- **Alternatif**: Qdrant aktifken `QdrantMemoryIngestor` (Adapters katmanında) kaydedilir.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Null Object pattern — `if (memory != null)` kontrolleri yerine her zaman inject edilebilir bir instance garanti edilir. DI container'da `null` kaydı yerine açık bir no-op sınıfı tercih edilir.

## Bağımlılıklar
Yok — saf C#.
