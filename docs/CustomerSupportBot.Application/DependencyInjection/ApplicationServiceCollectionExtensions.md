# ApplicationServiceCollectionExtensions

- **Kaynak:** `CustomerSupportBot.Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs`
- **Tür:** `public static class`
- **Namespace:** `CustomerSupportBot.Application.DependencyInjection`

## Ne işe yarar?

`ApplicationServiceCollectionExtensions`, Application/DependencyInjection/ApplicationServiceCollectionExtensions.cs Application katmanı servis kayıtları — driving port implementasyonları ve use case servisleri. <summary> Application katmanı servislerini DI container'a kaydeder. Driving port implementasyonları ve use case servisleri burada register edilir. </summary>

## Hangi amaçla kullanılır?

- İlgili use case gereksinimlerini karşılamak ve domain modelleri üzerinde gerekli işlemleri yürütmek.
- Hata durumlarında uygun domain istisnalarını fırlatmak ve loglama yapmak.

## Sorumlulukları

- **Üstlendiği:** İlgili domain sözleşmesini (`ApplicationServiceCollectionExtensions`) eksiksiz yerine getirmek.
- **Üstlenmediği:** Dış altyapı detaylarına (SQL, HTTP, gRPC) doğrudan bağımlı olmak.

## Constructor ve Başlatma Mantığı

Varsayılan parametresiz yapılandırıcı veya DI konteyneri üzerinden başlatılır.

## Metotlar ve İç Çalışma Mantıkları

### `AddApplicationDrivingPorts`
```csharp
public static IServiceCollection AddApplicationDrivingPorts(
        this IServiceCollection services,
        IConfiguration configuration)
```
- **İç Mantığı:** İlgili iş mantığını işletir, gerekli doğrulamaları yapar ve beklenen sonucu döner.

## Bağımlılıklar

- `CustomerSupportBot.Domain`
