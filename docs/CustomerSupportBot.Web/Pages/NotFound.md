# NotFound

**Dosya:** `Pages/NotFound.razor`
**Route:** `/not-found`

## Ne İşe Yarar
Eşleşen route bulunamadığında [App](../App.md)'in `Router` bileşeni tarafından gösterilen
sabit "sayfa bulunamadı" ekranıdır.

## Hangi Amaçla Kullanılır
Kullanıcı var olmayan bir URL'ye giderse (yanlış link, silinmiş sayfa) beyaz ekran yerine
anlaşılır bir mesaj görür.

## Sorumlulukları
- Sabit bir "Not Found" başlığı ve açıklama metni göstermek.
- [MainLayout](../Layout/MainLayout.md) içinde render edilmek (`@layout MainLayout`).

Bunun dışında hiçbir iş mantığı, state veya API çağrısı taşımaz — tamamen statik bir sayfadır.

## Diğer Katman ve Bileşenlerle İlişkileri
- [App.razor](../App.md) → `Router`'ın `NotFoundPage="typeof(Pages.NotFound)"` parametresiyle
  bağlanır; hiçbir route tanımlı `[page]` direktifi yoktur, yalnızca bu şekilde çağrılabilir.
- Layout: [MainLayout](../Layout/MainLayout.md).

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Blazor'un `Router` bileşeni, eşleşmeyen bir URL için render edilecek bir bileşen ister; bu
sayfa o sözleşmeyi karşılayan en minimal bileşendir. Ayrı bir sayfa olarak tutulması, ileride
(marka/tasarım ile uyumlu bir 404 ekranı gerektiğinde) genişletilebilir olmasını sağlar.

## Metotlar / Üyeler
Yok — `@code` bloğu içermez, saf markup'tır.

## Bağımlılıklar
Yok.
