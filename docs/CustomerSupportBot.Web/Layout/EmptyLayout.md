# EmptyLayout

**Dosya:** `Layout/EmptyLayout.razor`

## Ne İşe Yarar
İçeriği hiçbir navigasyon barı, kenar menüsü ya da çerçeve eklemeden doğrudan render eden
"boş" bir layout'tur — yalnızca `@Body`'yi basar.

## Hangi Amaçla Kullanılır
Kimlik doğrulama gerektirmeyen veya kendi tam-ekran tasarımını isteyen sayfalarda kullanılır:
[Login](../Pages/Login.md), [CustomerLogin](../Pages/CustomerLogin.md) ve
[Chat](../Pages/Chat.md) (`@layout EmptyLayout` direktifiyle [MainLayout](MainLayout.md)'un
varsayılanını geçersiz kılar).

> Chat sayfasının bu layout'u seçmesi bilinçlidir: sohbet ekranı kendi üst barını
> (marka, tema toggle, login/logout durumu) kendi markup'ında çizer — [MainLayout](MainLayout.md)'un
> sol menüsü sohbet deneyimine görsel gürültü katardı.

## Sorumlulukları
- `LayoutComponentBase`'ten türeyip `@Body`'yi render etmek. Başka hiçbir sorumluluğu yoktur.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Kullanan sayfalar**: [Login](../Pages/Login.md), [CustomerLogin](../Pages/CustomerLogin.md),
  [Chat](../Pages/Chat.md).
- Karşılaştır: [MainLayout](MainLayout.md) (sol menülü varsayılan layout),
  [AdminLayout](AdminLayout.md) (üst navigasyon barlı yönetim layout'u).

## Kullanılma Nedeni ve Tasarım Yaklaşımı
Blazor'da her sayfa mutlaka bir layout'a bağlanır; login ekranları ve chat gibi kendi tam-ekran
tasarımını isteyen sayfalar için "hiçbir şey ekleme" davranışını açıkça ifade eden ayrı bir
layout tanımlamak, bu sayfaların `@layout` direktifini `null` bırakıp örtük varsayılana
güvenmesinden (ve yanlışlıkla [MainLayout](MainLayout.md)'un menüsünü miras almasından) daha
güvenlidir.

## Metotlar / Üyeler
Yok — `@code` bloğu içermez.

## Bağımlılıklar
Yok.
