# MainLayout

**Dosya:** `Layout/MainLayout.razor`

## Ne İşe Yarar

Uygulamanın varsayılan (fallback) layout'udur: sol tarafta [`NavMenu`](NavMenu.md), sağda
`@Body` içeriği olacak şekilde iki bölmeli bir kabuk sağlar.

## Hangi Amaçla Kullanılır

Blazor'un `Router`'ı, bir sayfanın `@layout` direktifi yoksa `DefaultLayout` olarak bunu
kullanır. Pratikte bu projedeki sayfaların çoğu (`Chat`, `Login`, `CustomerLogin`) kendi
`@layout`'unu (`EmptyLayout`) veya (`Admin`, `Traces`, `Replay`, `Sla`, `Knowledge`)
`AdminLayout`'u açıkça belirttiğinden, `MainLayout` fiilen bir **fallback/güvenlik ağıdır** —
`@layout` direktifi unutulan bir sayfa olursa çıplak/layoutsuz render yerine bu devreye girer.

## Sorumlulukları

- `@Body` render alanını `main-layout-content` div'i içinde tanımlamak.
- [`NavMenu`](NavMenu.md) bileşenini sol/üstte göstermek.

## Diğer Katman ve Bileşenlerle İlişkileri

- `LayoutComponentBase`'den türer.
- Alt bileşen: [`NavMenu`](NavMenu.md).
- CSS: `MainLayout.razor.css` (component-scoped).

## Kullanılma Nedeni ve Tasarım Yaklaşımı

Kendi state'i veya `@code` bloğu yoktur — saf kompozisyon. Skeleton/App.razor'daki
`<Router>`'ın `DefaultLayout` sözleşmesini karşılamak için var olması yeterlidir; projedeki
gerçek sayfa deneyimleri `EmptyLayout` veya `AdminLayout` üzerinden kurgulanmıştır.

## Metotlar / Üyeler

Yok — yalnızca markup kompozisyonu.

## Bağımlılıklar

Yok (constructor injection / `@inject` kullanılmaz).
