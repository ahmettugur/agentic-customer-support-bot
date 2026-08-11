# JwtUtils

## Ne İşe Yarar
`static class` — bir JWT'nin `exp` (expiry) claim'ini, **imza doğrulaması yapmadan**, payload'ı decode ederek okur.

## Hangi Amaçla Kullanılır
[AppAuthStateProvider](AppAuthStateProvider.md), sayfa açılışında/her navigasyonda `localStorage`'daki access token'ın süresinin dolup dolmadığına bakmak için kullanır. Bu, "token localStorage'da var mı" (eski davranış) ile "token hâlâ geçerli mi" arasındaki farkı kapatır.

## Sorumlulukları
- JWT'yi `.` ile üç parçaya ayırıp orta parçayı (payload) Base64Url decode etmek.
- Decode edilen JSON'dan `exp` (Unix epoch saniye) alanını okuyup `DateTimeOffset`'e çevirmek.
- Bozuk/beklenmedik formatlı bir token için exception fırlatmadan `null` dönmek.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Kullanan sınıf**: [AppAuthStateProvider](AppAuthStateProvider.md) — tek tüketicisi.
- **Karıştırılmamalı**: Sunucu tarafındaki gerçek JWT üretim/doğrulama kodu (`CustomerSupportBot.Adapters.Persistence/Auth/JwtAccessTokenProvider.cs`, `Microsoft.IdentityModel.Tokens` kullanır, imza doğrular) ile hiçbir ilgisi yoktur. Bu sınıf Web (WASM) katmanındadır, imza kontrolü yapmaz, yapamaz da — `Microsoft.IdentityModel.Tokens`/`System.IdentityModel.Tokens.Jwt` paketlerini WASM bundle'ına eklemeden minimum işi yapmak için elle Base64Url decode kullanır.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
İmza doğrulaması **bilinçli olarak yapılmaz** — bu sınıfın amacı güvenlik kararı vermek değil, kullanıcı deneyimini iyileştirmektir: "büyük ihtimalle süresi dolmuş bir token'la sunucuya gidip 401 yemek yerine, sunucuya sormadan önce bir bak" sorusuna cevap verir. Gerçek yetkilendirme kararı her zaman sunucu tarafında (JWT imza + `[Authorize]`) verilir; bu sınıf yanlış (ör. kurcalanmış) bir token'a "geçerli" derse bile sunucu onu zaten reddedecektir — tek sonucu kullanıcının bir 401 daha görmesi olur, güvenlik açığı doğurmaz.

Decode başarısız olursa (`null` döner) çağıran taraf token'ı "bilinmiyor, süresi dolmamış say" olarak ele alır — normal akışa (sunucunun gerekirse 401 ile reddetmesine) bırakılır; decode hatası kullanıcıyı yanlışlıkla login'e atmaz.

## Metotlar / Üyeler

| Metot | Açıklama |
|-------|----------|
| `TryGetExpiryUtc(string? jwt)` | JWT'nin `exp` claim'ini `DateTimeOffset?` olarak döner; parse edilemezse `null`. |

## Bağımlılıklar
Yok — yalnızca `System.Text.Json` ve `System.Convert`/`System.Text.Encoding` (BCL).
