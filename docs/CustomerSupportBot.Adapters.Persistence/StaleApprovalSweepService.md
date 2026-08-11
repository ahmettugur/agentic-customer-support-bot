# StaleApprovalSweepService

> 💡 **Analiz notu:** Temizlik servisi — admin 4 saat içinde karar vermezse bekleyen onay taleplerini otomatik reddeder. Böylece bot sonsuz "onay bekliyor" durumunda kalmaz.

## Ne İşe Yarar

`ApprovalOptions.StalePendingHours` süresini aşan bekleyen (Pending) onay taleplerini otomatik reddeden periyodik arka plan servisidir.

## Hangi Amaçla Kullanılır

Bloklamayan onay modelinde tool çağrısı admin kararını beklemez; bu sweep eski bekleyen kayıtları temizler.

## Sorumlulukları

- Her 15 dakikada bir bekleyen onay taleplerini kontrol etmek.
- `StalePendingHours` eşiğini aşanları `IApprovalQueue.DecideAsync` ile otomatik reddetmek.
- Otomatik red işleminin normal bildirim/SSE akışını tetiklemesini sağlamak.

## Diğer Katman ve Bileşenlerle İlişkileri

- **Extends**: `BackgroundService` — periyodik çalışır.
- **DI ile inject edilen**: `IApprovalQueue`, `ApprovalOptions`.
- **İlişkili sınıf**: `ApprovalGateService` (Adapters.Agents) — bloklamayan onay modelinin karşı tarafı.
- **Bildirim**: `DecideAsync` üzerinden red yapıldığı için SSE/push akışı normal şekilde çalışır.

## Kullanılma Nedeni ve Tasarım Yaklaşımı

Bloklamayan approval modelinde (`ApprovalGateService`) tool çağrıları admin kararını beklemeden ilerler. Admin karar vermezse approval talebi sonsuz "Pending" kalabilir — bu sweep 15 dakikada bir çalışarak eski kayıtları temizler. `DecideAsync` kullanıldığı için tüm yan etkiler (SSE bildirimi, log, state güncelleme) aynen çalışır.

## Bağımlılıklar

- `IApprovalQueue` — Pending listesi ve karar verme.
- `ApprovalOptions` — `StalePendingHours` eşik değeri.
