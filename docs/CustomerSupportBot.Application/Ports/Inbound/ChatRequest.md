# ChatRequest

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/ChatRequest.cs`
- **Tür:** `public  record`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`ChatRequest`, Ports/Driving/ChatRequest.cs IChatPort driving port'unun use case input DTO'su. <summary> Kullanıcının gönderdiği chat isteği — use case boundary input. </summary> <param name="CustomerId"> Login'li müşterinin doğrulanmış kimliği — API katmanı bunu JWT claim'inden doldurur, asla client body'sinden GÜVENİLİR olarak alınmaz (endpoint bu alanı isteğin geldiği body'den değil, kimlik doğrulanmış HttpContext.User'dan set eder). </param>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ChatRequest`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
