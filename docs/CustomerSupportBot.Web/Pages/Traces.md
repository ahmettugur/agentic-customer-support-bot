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

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: [TracesApiService](../Services/TracesApiService.md).
- **Kullanan bileşenler**: [TraceSessionItem](../Components/TraceSessionItem.md), [TraceDetailPanel](../Components/TraceDetailPanel.md).
- **Model bağımlılığı**: [TraceDetailModels](../Models/TraceDetailModels.md).

## Bağımlılıklar
- [TracesApiService](../Services/TracesApiService.md).
- [TraceSessionItem](../Components/TraceSessionItem.md), [TraceDetailPanel](../Components/TraceDetailPanel.md).
