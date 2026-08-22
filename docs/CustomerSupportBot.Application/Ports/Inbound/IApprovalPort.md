# IApprovalPort

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/IApprovalPort.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`IApprovalPort`, <summary> Human-in-the-Loop onay akışı için primary port. </summary> <summary> Bekleyen onay istekleri — <see cref="ApprovalRequest.CustomerName"/> doldurulmuş olarak. </summary> <remarks> Async olmasının sebebi müşteri adlarının veritabanından çözülmesidir; kuyruğun kendisi bellek içi cache'ten gelir. Ad çözümü tek bir toplu sorgudur (N+1 değil). </remarks> <summary>Son N onay geçmişi — <see cref="ApprovalRequest.CustomerName"/> doldurulmuş olarak.</summary> <summary> Onaylanmış ama yürütmesi askıda kalmış kayıtlar — admin panelinin elle müdahale için göstermesi gerekenler. Tarih sınırı yoktur (bkz. IApprovalQueue.GetStuckExecutionsAsync). </summary> <summary>Tek istek.</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IApprovalPort`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
