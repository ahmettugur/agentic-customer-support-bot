# AuthScope & AuthScopeRouter

**Dosya:** `Services/AuthScope.cs`

## Ne İşe Yarar
`AuthScope`, uygulamadaki iki ayrı kimlik alanını (`Staff` ve `Customer`) temsil eden bir
enum'dur. `AuthScopeRouter`, bir route'un hangi kimlik alanına ait olduğunu (yani hangi login
akışının/token'ın geçerli olduğunu) belirleyen saf bir yardımcı sınıftır.

## Hangi Amaçla Kullanılır
Bu uygulamada iki farklı kullanıcı tipi aynı tarayıcıda **aynı anda** oturum açabilir: bir
admin/agent (`Staff`) ve bir müşteri (`Customer`). Her ikisinin de token'ı ayrı
`localStorage` anahtarlarında tutulur ([AuthTokenStore](AuthTokenStore.md)) ve ayrı
`AuthenticationState`'e sahiptir ([AppAuthStateProvider](AppAuthStateProvider.md)). `AuthScope`
bu ayrımı kod genelinde tip-güvenli şekilde taşıyan ortak sözleşmedir; `AuthScopeRouter` ise
mevcut URL'den "bu route hangi scope'a ait?" sorusunu cevaplar (ör.
[AuthorizedHttpClientHandler](AuthorizedHttpClientHandler.md)'ın 401 sonrası hangi scope'u
refresh edeceğine karar vermesi için).

## Sorumlulukları
- `AuthScope` enum'u: `Staff` ve `Customer` değerlerini tanımlamak.
- `AuthScopeRouter.Resolve(relativePath)`: verilen route'un `Customer` mi `Staff` mi olduğuna
  karar vermek. Kural basittir — yalnızca kök (`/`, yani [Chat](../Pages/Chat.md)) ve
  `/customer-login` ([CustomerLogin](../Pages/CustomerLogin.md)) müşteri alanıdır; geri kalan
  her şey (admin, login, traces, replay, sla, knowledge) staff alanıdır.

Bu sınıf **token okuma/yazma yapmaz, HTTP isteği göndermez** — sadece bir string'i bir enum
değerine eşler.

## Diğer Katman ve Bileşenlerle İlişkileri
- [AuthTokenStore](AuthTokenStore.md), [AuthService](AuthService.md),
  [AppAuthStateProvider](AppAuthStateProvider.md), [AuthorizedHttpClientHandler](AuthorizedHttpClientHandler.md)
  — hepsi metotlarına `AuthScope` parametresi alır/döner.
- `AuthScopeRouter.Resolve` tipik olarak `NavigationManager.Uri`'den türetilen bir relative
  path ile çağrılır (route değişince hangi scope'un aktif olduğunu belirlemek için).

## Kullanılma Nedeni ve Tasarım Yaklaşımı
İki kimlik alanını ayrı enum değerleriyle modellemek, "hangi token'ı kullanıyorum" sorusunu
derleme zamanında görünür kılar — string sabitlerle (`"staff"`/`"customer"`) yapılsaydı bir
yazım hatası sessizce yanlış token'ı okurdu. `AuthScopeRouter.Resolve`'un route listesini
"beyaz liste" değil "yalnızca müşteri route'larını say, gerisi staff" şeklinde tanımlaması
bilinçlidir: yeni bir staff sayfası eklendiğinde bu router'ın güncellenmesi gerekmez, sadece
yeni bir müşteri route'u eklenirse burası dokunulur.

> 🐞 **Karar yalnızca yol bölümüne bakılarak verilir.** Girdi genellikle
> `NavigationManager.ToBaseRelativePath(Uri)`'dir ve sorgu dizesini/parçayı (`?…`, `#…`) içerir.
> Eskiden `"?session=…"` boş yol sayılmıyordu: sohbet sayfası sorgu parametresiyle açıldığında
> staff alanı seçiliyor, müşteri isteğine personel token'ı ekleniyor, 401'de müşteri personel
> giriş sayfasına (`/login`) yönleniyordu. Karşılaştırma da artık önek değil, ilk yol
> segmentinin tam eşleşmesidir (`customer-loginx` müşteri alanı değildir). Testler:
> `tests/CustomerSupportBot.Web.Tests/AuthScopeRouterTests.cs`.

## Metotlar / Üyeler

| Üye | Açıklama |
|---|---|
| `AuthScope.Staff` | Admin/Agent kimlik alanı. |
| `AuthScope.Customer` | Müşteri kimlik alanı. |
| `AuthScopeRouter.Resolve(string relativePath)` | Sorgu dizesi ve parça (`?`, `#` sonrası) atılır; ilk yol segmenti boşsa (kök) veya tam olarak `customer-login` ise (büyük/küçük harf duyarsız) `Customer`, aksi halde `Staff` döner. |

## Bağımlılıklar
Yok — statik, durumsuz bir yardımcı sınıf.
