# ICustomerRepository

**Kaynak:** `Ports/Outbound/Persistence/ICustomerRepository.cs`
**Implementasyon:** [`CustomerRepository`](../../../../CustomerSupportBot.Adapters.Persistence/Postgres/CustomerRepository.md)

## 1. Ne İşe Yarar

Gerçek müşteri kaydının (katalogdaki `CustomerEntity`) varlığını ve kimlik sahipliğini
doğrulayan secondary port.

## 2. Hangi Amaçla Kullanılır

Müşteri self-servis kaydı (`/auth/customer/register`) sırasında `IsEmailOwnedByCustomerAsync`
ile "bu e-posta gerçekten bu müşteri numarasına mı ait" kontrolü yapılır; admin onay
kuyruğu/geçmişi ekranlarında `GetFullNameAsync`/`GetFullNamesAsync` ile müşteri adı gösterilir.

## 3. Sorumlulukları

- **Üstlendiği:** Müşteri varlığı ve kimlik-sahipliği doğrulaması, ad bilgisi okuma.
- **Üstlenmediği:** Kullanıcı hesabı/login kaydı — o [`IUserAuthRepository`](../Auth/IUserAuthRepository.md)'nin işi (bu port yalnızca *iş* katalogundaki `Customer` varlığını okur).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

`Adapters.Persistence/Postgres/CustomerRepository` implemente eder.

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🐞 **`IsEmailOwnedByCustomerAsync` neden var:** public kayıt akışının kimlik sahipliği
> kontrolüdür. Bu olmadan `Exists` yalnızca "böyle bir müşteri var mı" sorusunu yanıtlıyordu ve
> herkes başkasının müşteri numarasıyla hesap açıp o müşteri adına geçerli bir JWT alabiliyordu
> — yani sistemin geri kalanındaki tüm sahiplik kontrolleri (EntityVerifier, oturum sahipliği,
> tool sahiplik kuralları) doğru müşteri sanıp geçiriyordu.

`GetFullNamesAsync` toplu sorgu olarak var çünkü admin onay kuyruğu bir listedir; her kart için
ayrı `GetFullNameAsync` sorgusu atmak N+1 problemi yaratır ve kuyruk büyüdükçe panelin açılışını
doğrusal olarak yavaşlatırdı.

## 6. Metotlar / Üyeler

| Metot | Açıklama |
|---|---|
| `bool Exists(long customerId)` | Müşteri kaydı var mı. |
| `Task<bool> IsEmailOwnedByCustomerAsync(long customerId, string email, CancellationToken ct = default)` | E-posta gerçekten bu müşteriye mi ait (case-insensitive). |
| `Task<string?> GetFullNameAsync(long customerId, CancellationToken ct = default)` | Tekil ad sorgusu. |
| `Task<IReadOnlyDictionary<long, string>> GetFullNamesAsync(IReadOnlyCollection<long> customerIds, CancellationToken ct = default)` | Toplu ad sorgusu (N+1'i önlemek için). |

## 7. Bağımlılıklar

Yok — port arayüzü bağımlılıksızdır (yalnızca ilkel tipler).
