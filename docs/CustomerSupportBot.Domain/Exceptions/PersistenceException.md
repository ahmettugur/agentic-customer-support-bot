# PersistenceException

- **Kaynak:** `CustomerSupportBot.Domain/Exceptions/DomainException.cs` (aynı dosyada diğer exception'larla birlikte tanımlı)
- **Tür:** `public class : DomainException`
- **Namespace:** `CustomerSupportBot.Domain.Exceptions`

## 1. Ne İşe Yarar

Kalıcılık katmanında (veritabanı) geçici bir hata oluştuğunda fırlatılır — connection timeout,
deadlock gibi durumlar.

## 2. Hangi Amaçla Kullanılır

Persistence katmanındaki adapter'lar (EF Core repository implementasyonları) geçici,
tekrar-denenebilir hataları bu exception ile domain katmanına taşır. `Code = "PERSISTENCE_ERROR"`
sabittir. Retry mekanizması bu exception'ı yakalayıp işlemi tekrar deneyebilir — kalıcı bir iş
kuralı ihlali değil, geçici bir altyapı arızası olduğunu işaret eder.

## 3. Sorumlulukları

- ✅ Geçici/tekrar-denenebilir kalıcılık hatalarını tip güvenli biçimde taşımak
- ❌ Kalıcı iş kuralı ihlallerini temsil etmek — bkz. [ConcurrencyConflictException](ConcurrencyConflictException.md)
- ❌ Retry mantığını yürütmek — sadece işaret eder, retry kararını çağıran katman verir

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim fırlatır:** `CustomerSupportBot.Adapters.Persistence` katmanındaki repository implementasyonları
- **Kim yakalar:** API katmanındaki global exception handler (`DomainExceptionHandler`) — uygun
  HTTP durum koduna (ör. 503) çevirir

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 💡 `ExternalServiceException`'dan (AI/Redis gibi dış servisler) kasıtlı olarak ayrıdır:
> `PersistenceException` özellikle veritabanı katmanına, `ExternalServiceException` ise
> veritabanı dışı dış servislere işaret eder — hata kaynağını ayırt etmek, hangi alt sistemin
> arızalı olduğunu loglardan/metriklerden hızlıca anlamayı sağlar.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
|---|---|---|
| `Code` | `string` | Kalıtım yoluyla `DomainException`'dan gelir, sabit değeri `"PERSISTENCE_ERROR"` |

Kurucu: `PersistenceException(string message, Exception? innerException = null)`.

## 7. Bağımlılıklar

- [DomainException.md](DomainException.md) — üst sınıf

## Bağlantılar

- [ExternalServiceException.md](ExternalServiceException.md) — dış servis karşılığı
