# IRealtimeBridge

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/IRealtimeBridge.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`IRealtimeBridge`, <summary> Köprü modu — Realtime sadece STT/TTS, agent pipeline cevabı üretir. </summary> <param name="authenticatedCustomerId"> Login'li müşterinin JWT claim'inden gelen kimliği. Oturuma bir kez bağlanır ve sipariş tool'ları bunu kullanır — bu değer olmadan her sipariş sorgusu sahiplik kontrolüne takılıp "bulunamadı" döner. </param>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IRealtimeBridge`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
