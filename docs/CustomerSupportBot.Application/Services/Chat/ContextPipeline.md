# ContextPipeline

- **Kaynak:** `CustomerSupportBot.Application/Services/Chat/ContextPipeline.cs`
- **Tür:** `public  class : IContextPipeline`
- **Namespace:** `CustomerSupportBot.Application.Services.Chat`

## Ne işe yarar?

`ContextPipeline`, <summary> Kayıtlı tüm <see cref="IContextProvider"/>'ları çalıştırır ve sonuçlarını tek bir bağlam metnine birleştirir.  <para> Provider'lar <b>paralel</b> koşar (biri diğerini beklemez), sonuç ise <c>Order</c>'a göre <b>deterministik</b> sırada birleşir — böylece prompt turdan tura kaymaz. </para>  <para> Üç koruma: provider başına <b>zaman aşımı</b>, hata anında <b>kritik/iyileştirici</b> ayrımı ve toplam <b>bütçe</b> tavanı. Gerekçeleri için bkz. <see cref="ContextPipelineOptions"/> ve <see cref="IContextProvider.IsCritical"/>. </para> </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ContextPipeline`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

```csharp
public ContextPipeline(IEnumerable<IContextProvider> providers,
        IOptions<ContextPipelineOptions> options,
        ILogger<ContextPipeline> logger)
```
- **Parametreler ve Başlatma:** Alınan servis bağımlılıkları (`readonly` alanlara) atanır ve gerekli başlatma kontrolleri yapılır.

## Metotlar ve İç Çalışma Mantıkları

### `BuildContextAsync`
```csharp
public async Task<ContextResult> BuildContextAsync(
        AgentSession session, string currentQuery, CancellationToken ct = default)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IContextPipeline`
