# Traces.razor

## Ne İşe Yarar
Reasoning trace oturumlarını listeleyen ve seçilen oturumun trace detaylarını gösteren master-detail sayfasıdır.

## Hangi Amaçla Kullanılır
Admin panelinde AI'ın reasoning süreçlerini incelemek, debug etmek ve oturum bazlı trace geçmişini görmek için kullanılır.

## Sorumlulukları
- Trace oturumlarını sol panelde listelemek.
- Seçilen oturumun trace'lerini sağ panelde göstermek.
- Trace detay panelini açmak/kapatmak.
- Replay sayfasına yönlendirme.
- Auto-refresh açıkken 5sn'de bir oturum listesini yenilemek — yalnızca sekme görünürken
  (bkz. aşağıdaki not).

## Erişim
`[Authorize(Roles = "Admin")]` — bu uca dayanan `/traces/*` API'leri `Program.cs`'de
`Admin` rolüyle korunuyor; sayfa yetkisi API ile örtüşüyor.

> 🐞 **Bulundu ve düzeltildi — trace kartı klavyeyle erişilemiyordu:** Merkez sütundaki
> `.trace-item` düz bir `<div @onclick>` idi — `role="button"`/`tabindex`/klavye handler'ı
> yoktu, yani Tab ile ona hiç odaklanılamıyordu. Soldaki oturum listesi
> ([TraceSessionItem](../Components/TraceSessionItem.md)) zaten bu deseni doğru uyguluyordu;
> aynı desen (`role="button"`, `tabindex="0"`, Enter/Space handler, `:focus-visible` halkası)
> buraya da taşındı. Aynı geçişte oturum başlığına da bir `title` tooltip'i eklendi —
> liste tek satıra kısaltıldığı için (`text-overflow: ellipsis`) neredeyse aynı "Sipariş
> ver…"/"Siparişimin duru…" başlıklı çok sayıda oturum arasında ayrım yapmak zordu.

> 🐞 **Bulundu ve düzeltildi — arka planda gereksiz polling:** `Sla.razor` sayfa
> görünürlüğünü `sla-bridge.js`/`document.visibilitychange` ile izleyip sekme arka
> plandayken polling'i durduruyordu; bu sayfa aynı deseni kullanmıyordu, yani açık
> bırakılmış bir `/traces` sekmesi sonsuza kadar 5sn'de bir `/traces/sessions` çağırıyordu.
> `sla-bridge.js` genel amaçlı olduğu için (`__slaSetup`/`OnVisibilityChange` adları
> sayfaya özel değil) aynı köprü burada da kullanılıp `_pageVisible` bayrağıyla timer
> callback'i sekme görünür değilken erken dönüyor.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: [TracesApiService](../Services/TracesApiService.md), `IJSRuntime` (görünürlük köprüsü için).
- **Kullanan bileşenler**: [TraceSessionItem](../Components/TraceSessionItem.md), [TraceDetailPanel](../Components/TraceDetailPanel.md).
- **Model bağımlılığı**: [TraceDetailModels](../Models/TraceDetailModels.md).

## Bağımlılıklar
- [TracesApiService](../Services/TracesApiService.md).
- [TraceSessionItem](../Components/TraceSessionItem.md), [TraceDetailPanel](../Components/TraceDetailPanel.md).
