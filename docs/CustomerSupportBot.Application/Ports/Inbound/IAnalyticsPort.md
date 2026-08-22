# IAnalyticsPort

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/IAnalyticsPort.cs`
- **Tür:** `public  interface`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`IAnalyticsPort`, <summary> Analitik verileri için primary port. </summary> <summary>Belirtilen sessionId için derecelendirme kaydeder.</summary> <summary>Tek bir session için derecelendirmeyi döner.</summary> <summary>Son N derecelendirmeyi döner.</summary> <summary>Tüm derecelendirmeler.</summary> <summary>Özet istatistikler (ortalama puan, toplam sayı vb.).</summary> <summary>Tüm dashboard istatistiklerini döner.</summary> <summary>Tek bir oturum için detaylı analytics döner.</summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IAnalyticsPort`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
