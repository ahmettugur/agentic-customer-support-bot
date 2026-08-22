# ContextPipelineOptions

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/ContextPipelineOptions.cs`
- **Tür:** `public  class`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`ContextPipelineOptions`, <summary> Bağlam kurulumunun sınırları. Bağlam bir <b>iyileştirmedir, zorunluluk değildir</b>: üretimi turu süresiz bekletmemeli ve prompt'u sınırsız şişirmemelidir. </summary> <summary> Tek bir provider'a tanınan süre. Aşılırsa o provider atlanır, tur devam eder.  <para> Neden gerekli: pipeline içinde ağ ve LLM çağrıları var (<c>SemanticMemoryContextProvider</c> embedding + vektör araması, <c>ConversationSummaryProvider</c> doğrudan bir <c>IChatClient.CompleteAsync</c>). Bağlam kurulumu workflow'dan ÖNCE çalıştığı için <c>WorkflowGuardOptions.TimeoutSeconds</c> koruması burada henüz devrede değildir — bu ayar olmadan yavaş bir provider turu belirsiz süre bloklar. </para> </summary> <summary>Tek bir provider'ın katkısı için üst sınır (karakter).</summary> <summary> Tüm bağlamın toplam üst sınırı (karakter). Tavana ulaşıldığında <b>düşük öncelikli</b> (yüksek <c>Order</c>) provider'lar dışarıda bırakılır — kritik olanlar önce yerleşir. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ContextPipelineOptions`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Özellikler/Properties

- `ProviderTimeoutSeconds` (`int`): İlgili veriyi temsil eden özellik.
- `MaxProviderChars` (`int`): İlgili veriyi temsil eden özellik.
- `MaxTotalChars` (`int`): İlgili veriyi temsil eden özellik.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
