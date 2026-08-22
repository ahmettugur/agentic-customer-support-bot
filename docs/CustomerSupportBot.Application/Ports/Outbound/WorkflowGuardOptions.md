# WorkflowGuardOptions

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/WorkflowGuardOptions.cs`
- **Tür:** `public  class`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`WorkflowGuardOptions`, Application/Services/Workflow/WorkflowGuardOptions.cs Workflow seviyesi guard ayarları. appsettings.json'dan bind edilir. <summary> Workflow için guard parametreleri. appsettings.json "WorkflowGuards" bölümü. </summary> <summary> Tüm workflow için saniye cinsinden timeout. Bu süre aşılırsa CancellationToken tetiklenir. </summary> <summary> Aynı tool + aynı parametre combo'sunun maksimum tekrar sayısı. Bu eşik aşılırsa workflow sonlandırılır. </summary> <summary> Reasoning çağrısına gönderilecek EN FAZLA geçmiş mesaj sayısı (en yeniler).  <para> Workflow tarafında geçmiş özetlenip kırpılıyordu ama reasoning aynı korumadan yararlanmıyor, oturumun TAMAMINI modele gönderiyordu. Uzun oturumlarda bu üç şeyi birden büyütür: token maliyeti, gecikme ve modelin bağlam sınırını aşma riski — sınır aşılırsa reasoning fallback'e düşer ve tur sessizce kalitesizleşir.

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`WorkflowGuardOptions`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Özellikler/Properties

- `TimeoutSeconds` (`int`): İlgili veriyi temsil eden özellik.
- `MaxDuplicateToolCalls` (`int`): İlgili veriyi temsil eden özellik.
- `ReasoningHistoryMessages` (`int`): İlgili veriyi temsil eden özellik.
- `ReasoningTimeoutSeconds` (`int`): İlgili veriyi temsil eden özellik.
- `MaxIterations` (`int`): İlgili veriyi temsil eden özellik.
- `MaxHandoffsPerAgent` (`int`): İlgili veriyi temsil eden özellik.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
