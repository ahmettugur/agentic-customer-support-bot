# IHitlEventSubscription

- **Kaynak:** `CustomerSupportBot.Application/Ports/Inbound/IHitlEventPort.cs`
- **Tür:** `public  interface : IDisposable;

/// <summary>
/// Chat streaming adapter'ının session bazlı approval/escalation event'lerini
/// dinlemek için kullandığı primary port.
/// </summary>
public interface IHitlEventPort`
- **Namespace:** `CustomerSupportBot.Application.Ports.Inbound`

## Ne işe yarar?

`IHitlEventSubscription`, <summary> Chat streaming adapter'ının session bazlı approval/escalation event'lerini dinlemek için kullandığı primary port. </summary>

## Hangi amaçla kullanılır?

- Hexagonal mimaride bağımlılıkların soyutlanması ve gevşek bağlı (loosely coupled) entegrasyon sağlamak.
- İlgili use case veya port çağrılarının tip güvenli ve test edilebilir şekilde yürütülmesini sağlamak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`IHitlEventSubscription`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
- `IDisposable;

/// <summary>
/// Chat streaming adapter'ının session bazlı approval/escalation event'lerini
/// dinlemek için kullandığı primary port.
/// </summary>
public interface IHitlEventPort`
