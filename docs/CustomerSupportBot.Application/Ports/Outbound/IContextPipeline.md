# IContextPipeline

- **Kaynak:** `CustomerSupportBot.Application/Ports/Outbound/IContextPipeline.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Outbound`

## Ne işe yarar?

`IContextPipeline`, <summary> Context pipeline port'u — kayıtlı IContextProvider'ları çalıştırıp birleştirilen bağlam metnini döner. Adapter'lar bu port üzerinden bağlam alır. </summary> <summary> Bağlamı kurar. Yalnızca birleşik metni değil, <b>hangi provider'ın katkı yaptığını</b> da döner — çağıranın buna göre karar vermesi gerekebiliyor (bkz. <see cref="ContextResult"/>). </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IContextPipeline`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
