# PiiMasker

**Dosya:** `Services/Logging/PiiMasker.cs`
**Tür:** `static class`
**Namespace:** `CustomerSupportBot.Application.Services.Logging`

## 1. Ne İşe Yarar

Log satırlarına yazılmadan önce PII (e-posta, telefon, TC kimlik no, kredi kartı, IP adresi)
değerlerini maskeleyen, bağımsız (state'siz) statik yardımcı fonksiyonlar kümesi.

## 2. Hangi Amaçla Kullanılır

İki farklı çağıran, iki farklı senaryo için kullanır:

1. **Bilinen tek bir alanı maskelemek** — `ILogger` çağrısına PII geçirmeden önce:
   ```csharp
   _logger.LogWarning("[Auth] ... email={Email}", PiiMasker.MaskEmail(email));
   ```
   ([`CustomerAuthService`](../Auth/CustomerAuthService.md) — değişkenin zaten `email` olduğu
   bilinir, tespite gerek yok.)
2. **Serbest metin içinde PII'yi bulup maskelemek** — [`InputGuard`](../Chat/InputGuard.md)
   kendi regex'leriyle (kredi kartı/telefon/TC/e-posta desenleri) metindeki eşleşmeleri BULUR,
   her eşleşmeyi `PiiMasker.MaskXxx(match.Value)` ile maskeler. PII **tespiti** `InputGuard`'ın
   sorumluluğu — `PiiMasker` yalnızca "bu bir e-posta/telefon/TC/kart, maskele" komutunu alır.

## 3. Sorumlulukları

- **Üstlendiği:** Verilen ham değeri (tespiti çağıranın yaptığı, zaten izole edilmiş bir PII
  parçası), formatlamayı (boşluk/tire/`+`/`:`) koruyarak, yalnızca kimliği geri kurmaya yetecek
  asgari bilgiyi (son 2-4 hane, ilk e-posta karakteri, IP'nin ağ kısmı) açıkta bırakacak şekilde
  maskelemek.
- **Üstlenmediği:** Bir string içinde PII **tespiti** (nerede geçtiğini bulmak) — bu metotlar
  yalnızca **kendisine verilen** değeri maskeler; genel amaçlı bir regex tarayıcı değildir. Tespit
  sorumluluğu çağırandadır (`InputGuard` serbest metinde, `CustomerAuthService` zaten bilinen bir
  değişkende).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- [`CustomerAuthService`](../Auth/CustomerAuthService.md) — `RegisterAsync`/`AuthenticateAsync`
  içindeki 4 log çağrısında `MaskEmail` kullanır (bilinen tek alan).
- [`InputGuard`](../Chat/InputGuard.md) — `MaskPii` adımında 4 metodun tamamını (`MaskCreditCard`,
  `MaskPhone`, `MaskTcKimlikNo`, `MaskEmail`) serbest kullanıcı metni üzerinde kullanır; buradan
  çıkan maskelenmiş metin hem LLM'e gider hem `ReasoningTrace.UserQuery`'ye yazılır.
- Application katmanında herhangi bir servis, PII içeren bir alanı loglamadan önce buraya
  başvurabilir; DI kaydı gerekmez (statik metotlar).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

> 🔒 **Neden eklendi:** `CustomerAuthService` ham e-postayı `LogInformation`/`LogWarning` ile
> maskesiz yazıyordu. Structured log'lar genelde uygulamanın kendi veritabanından daha geniş
> erişimli bir yere (log toplama/SIEM) akar; bu da PII'nin uygulamanın kendi yetkilendirme
> sınırlarının dışına sızmasına yol açar. Tek bir servise özel bir `private static MaskEmail`
> ile başlandı, sonra telefon/TC/kredi kartı/IP için de aynı ihtiyaç öngörüldüğü için paylaşımlı
> bir yardımcıya çıkarıldı.
>
> **Format-preserving maskeleme bilinçli:** `MaskPhone`/`MaskTcKimlikNo`/`MaskCreditCard` yalnızca
> rakamları maskeler, ayraçları (boşluk, tire, `+`) olduğu gibi bırakır — maskelenmiş değer hâlâ
> "bu bir telefon numarasıydı" bilgisini taşır, debug ederken hangi alanın loglandığı belli olur.
>
> **Son N hane neden korunuyor (tamamen `***` değil):** aynı hesabın farklı log satırlarında
> hâlâ eşleştirilebilmesi (iki log satırının aynı müşteriye ait olduğunu anlamak) için — tam
> kör maskeleme bu debug faydasını kaybettirirdi. Kredi kartında endüstri standardına uyularak
> (ör. ödeme sağlayıcı makbuzları) son 4 hane, diğerlerinde son 2 hane bırakılır.
>
> **Kredi kartı/TC kimlik no için domain'de bir ALAN yok, ama serbest metinde olabilir:**
> domain bu alanları hiç taşımıyor (uygulama ödeme almıyor, `customerId` JWT'den geliyor, TC
> hiçbir tool parametresi değil). Ama bir müşteri chat'e "kartım 4111..." ya da "TC kimlik
> numaram ..." yazarsa, bu metin [`InputGuard`](../Chat/InputGuard.md) üzerinden hem LLM'e
> (OpenAI) hem `ReasoningTrace`'e gider — domain'in bu alanı "resmi olarak taşımaması" bu
> sızıntıyı engellemez. Bu yüzden `InputGuard`, ham `email`/`phone` gibi bilinen bir alan
> olmasa bile, serbest metni tarayıp bu 4 deseni bulur ve buradaki `MaskXxx` metotlarıyla
> maskeler.

## 6. Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `MaskEmail(string? email): string` | `"ahmet@x.com"` → `"a***@x.com"`. `@` yoksa `"***"`. |
| `MaskPhone(string? phone): string` | Son 2 hane korunur, formatlama korunur. `"0532 123 45 67"` → `"**** *** ** 67"`. |
| `MaskTcKimlikNo(string? tckn): string` | Son 2 hane korunur (`MaskPhone` ile aynı iskelet, farklı semantik). |
| `MaskCreditCard(string? cardNumber): string` | Son 4 hane korunur (sektör standardı). `"4111 1111 1111 1111"` → `"**** **** **** 1111"`. |
| `MaskIpAddress(string? ip): string` | IPv4: son oktet `*`. IPv6: ilk 2 grup + `"::"`. |

## 7. Bağımlılıklar

Yok — saf, state'siz statik metotlar; dış bağımlılık veya DI gerektirmez.

## Bağlantılar

- [CustomerAuthService.md](../Auth/CustomerAuthService.md) — bilinen alan (e-posta) çağırıcısı.
- [InputGuard.md](../Chat/InputGuard.md) — serbest metin içinde PII tespit eden çağırıcı.
