# SemanticMemoryService

**Dosya:** `Services/Memory/SemanticMemoryService.cs`
**Implements:** `ISemanticMemoryIngestor`, `ISemanticMemoryWriter`

## 1. Ne İşe Yarar

Üç koleksiyonu (Episodic / Lessons / Knowledge) yöneten üst seviye memory facade'ı: embedding +
upsert + search akışını tek noktadan sunar. Koleksiyon isimleri `SemanticMemoryOptions`'tan
gelir (`cs_episodic`, `cs_lessons`, `cs_knowledge`).

## 2. Hangi Amaçla Kullanılır

`ContextPipeline` içindeki iki provider bu servisi çağırır:

- **`SemanticMemoryContextProvider`** — Knowledge + Lesson + (müşteri kimliği varsa) Episodic
  koleksiyonlarını arar, sonuçları reasoning prompt'una enjekte eder.
- **`TurnFinalizer`** (`WriteEpisodeAsync` üzerinden) — her turun sonunda soru+yanıtı Episodic
  koleksiyonuna yazar.

## 3. `WriteEpisodeAsync` — `customerId` tag'i

> 🐞 **Bulundu ve düzeltildi — episodic bellek write-only ölü veriydi.** `WriteEpisodeAsync`
> her turda bir kayıt üretiyordu, ama kod tabanında hiçbir yerde
> `SearchAsync(MemoryKind.Episodic, …)` çağrılmıyordu — yazılan hiçbir episode asla geri
> okunmuyordu. Sebebi: dokümanlar yalnızca `SessionId` taşıyordu ve `SemanticMemoryContextProvider`
> Episodic koleksiyonunu hiç aramıyordu (yalnızca Knowledge+Lesson).
>
> Düzeltme iki parçalı:
> 1. `WriteEpisodeAsync`'e opsiyonel `customerId` parametresi eklendi; doldurulduğunda
>    dokümana `Tags["customerId"]` olarak yazılır.
> 2. `SemanticMemoryContextProvider`, oturumun `AuthenticatedCustomerId`'si varsa Episodic
>    koleksiyonunu **customerId tag'iyle filtreleyerek** arar ve "Bu Müşteriyle Geçmiş
>    Görüşmeler" başlığı altında bağlama ekler.
>
> `SessionId` değil `customerId` ile filtrelenmesi kasıtlı: amaç aynı müşterinin **farklı
> oturumlardaki** geçmişini bulmak. `SessionId` ile sınırlı kalsaydı bugünkü oturum dünkü
> oturuma hiç bağlanamazdı. Kimlik doğrulanmamışsa (anonim tur) Episodic koleksiyonu hiç
> aranmaz — filtresiz arama başka bir müşterinin episode'unu sızdırma riski taşırdı.

## 4. `SearchByVectorAsync` — `tagFilter`

`tagFilter: IReadOnlyDictionary<string,string>?` parametresi `IVectorMemoryPort.SearchAsync`'e
kadar iletilir; Qdrant tarafında `Filter.Must` koşuluna (`FieldCondition` + `Match.Keyword`)
dönüşür.

> ⚠️ Bu parametre eklenirken `SearchAsync(MemoryKind, string query, …)` içindeki dolaylı çağrı
> (`SearchByVectorAsync(kind, vec, topK, minScore, ct)`) **pozisyonel** argüman kullanıyordu —
> yeni parametre `ct`'den önce eklendiği için `ct` sessizce `tagFilter`'a bağlanacaktı. Adlandırılmış
> argümana (`ct: ct`) çevrilerek düzeltildi. Bu sınıfa yeni bir opsiyonel parametre eklerken
> aynı riski göz önünde bulundurun — pozisyonel çağrıları arayın.

## Bağlantılar

- [MemoryPortService.md](MemoryPortService.md) — Memory orkestratör
- [../Providers/ContextProviders.md](../Providers/ContextProviders.md) — `SemanticMemoryContextProvider` (episode retrieval dahil)
- [../../CustomerSupportBot.Adapters.Agents/TurnFinalizer.md](../../CustomerSupportBot.Adapters.Agents/TurnFinalizer.md) — `WriteEpisodeAsync`'i çağıran taraf
