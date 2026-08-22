# IRealtimeNativeBridge

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/IRealtimeNativeBridge.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`IRealtimeNativeBridge`, <summary> Native mod — gpt-realtime-2 kendisi konuşur, okuma-only tool'ları çağırır. </summary> <param name="authenticatedCustomerId"> Login'li müşterinin JWT claim'inden gelen kimliği. Oturuma bir kez bağlanır ve sipariş tool'ları bunu kullanır — bu değer olmadan her sipariş sorgusu sahiplik kontrolüne takılıp "bulunamadı" döner. </param>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IRealtimeNativeBridge`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
