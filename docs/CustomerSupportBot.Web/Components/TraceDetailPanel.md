# TraceDetailPanel

## Ne İşe Yarar
Seçilen bir reasoning trace'in detaylı görüntülendiği yan paneldir. Reasoning, planning, agent ziyaretleri, tool çağrıları ve specialist reasoning'leri yapılandırılmış şekilde gösterir.

## Hangi Amaçla Kullanılır
`Traces.razor` sayfasında bir trace seçildiğinde sağ panelde açılır; trace'in tüm aşamalarını detaylı olarak inceler.

## Sorumlulukları
- Trace özet bilgilerini göstermek (süre, iterasyon, hata, sonuç).
- Reasoning sonuçlarını render etmek (sentiment, intent, confidence, issues).
- Planning bilgilerini göstermek (plan adımları, sub-task'lar).
- Agent ziyaretlerini kronolojik sırada listelemek.
- Tool çağrılarını parametre ve sonuçlarıyla göstermek.
- Specialist reasoning detaylarını göstermek.
- Revision bilgisini göstermek (ilk draft vs. final response).
- Replay sayfasına link.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Parameter**: `TraceDetail` — parent bileşenden alınır.
- **İlişkili helper**: [JsonExtensions](../Helpers/JsonExtensions.md) — JsonElement erişimi.
- **Model bağımlılığı**: [TraceDetailModels](../Models/TraceDetailModels.md).
- **Kullanan sayfa**: [Traces](../Pages/Traces.md).

## Bağımlılıklar
- [TraceDetailModels](../Models/TraceDetailModels.md), [JsonExtensions](../Helpers/JsonExtensions.md).
