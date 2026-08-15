# Sla.razor

## Ne İşe Yarar
SLA (Service Level Agreement) izleme dashboard'udur. Approval ve escalation'ların SLA durumlarını, ihlalleri ve uyarıları görüntüler.

## Hangi Amaçla Kullanılır
Admin panelinde SLA metriklerini izlemek ve geçmiş SLA olaylarını incelemek için kullanılır.

## Sorumlulukları
- SLA genel durumunu göstermek (pending counts, en eski bekleyen, ihlal sayıları).
- Approval ve escalation SLA ayrı kartlarda göstermek.
- SLA olay geçmişini tablo olarak listelemek (timestamp, kind, severity, action).
- Otomatik yenileme (polling).

## Erişim
`[Authorize(Roles = "Admin")]` — `/sla/*` API'leri `Admin` rolüyle korunuyor
(bkz. [Traces.md](Traces.md#erişim)).

> 🐞 **Bulundu ve düzeltildi — eşikler her zaman 0 görünüyordu:**
> `SlaApprovalStats`/`SlaEscalationStats` (`AdminModels.cs`) alanları `WarnAfter`/
> `BreachAfter` adındaydı; API ise `warnAfterSeconds`/`breachAfterSeconds` döndürüyordu.
> `System.Text.Json`'ın varsayılan case-insensitive eşleştirmesi yalnızca büyük/küçük harf
> farkını tolere eder, farklı adları eşleştirmez — bu yüzden bu iki alan **her zaman**
> `0`'a deserialize oluyordu. Sonucu: "Warn eşiği" ve "Breach eşiği" satırları her zaman
> `0` gösteriyordu, ve `StatClass(actual, warn, breach)` `0 >= 0` olduğu için boş kuyrukta
> bile "En eski (sn)" değerini kırmızı (breach) render ediyordu — sahte alarm. Alan adları
> `WarnAfterSeconds`/`BreachAfterSeconds` olarak API ile eşleştirildi; gerçek config
> değerleri (20/60sn onaylar, 60/180sn eskalasyonlar) artık görünüyor ve boş kuyruk artık
> kırmızı görünmüyor.

> 🐞 **Bulundu ve düzeltildi — çakışan iki stylesheet:** Sayfa hem `css/sla.css`'i
> (linked, token tabanlı, dark mode destekli) hem de `Sla.razor.css`'i (Blazor CSS
> isolation, tamamen hardcoded hex, eski bir inline `<style>` bloğundan taşınmış) aynı anda
> yüklüyordu. Blazor'un scoping'i her seçiciye ekstra bir `[b-xxxxxxxx]` öznitelik seçicisi
> eklediği için `Sla.razor.css` daha yüksek özgüllükle **her çakışan kuralda `sla.css`'i
> eziyordu** — yani sayfa görünüşte token sistemini kullanıyor gibi dursa da gerçek
> değerler eski, hardcoded dosyadan geliyordu (rastlantısal olarak aynı renklere denk
> geldiği için görsel fark yoktu). `sla.css` zaten tüm seçicileri kapsadığından
> `Sla.razor.css` tamamen silindi.
>
> Ayrıca sayfanın "Yenile" butonu ve "Auto (5sn)" checkbox'ı class'sız/inline-style'lı
> çıplak HTML elemanlarıydı (`<button style="margin-left:8px;">`) — koyu temada bu, panelin
> geri kalanından kopuk, çözünmemiş beyaz bir kutu olarak görünüyordu. `traces.css`'teki
> `.btn-secondary`/`.auto-refresh-toggle` deseni `sla.css`'e de eklendi.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: [SlaApiService](../Services/SlaApiService.md).
- **Model bağımlılığı**: [AdminModels](../Models/AdminModels.md) — `SlaStatus`, `SlaEvent`.
- **Backend karşılığı**: `SlaEndpoints`.

## Bağımlılıklar
- [SlaApiService](../Services/SlaApiService.md).
- `css/sla.css` — linked, token tabanlı, dark mode destekli. (Eski `Sla.razor.css`
  component-scoped dosyası kaldırıldı — yukarıya bakın.)
