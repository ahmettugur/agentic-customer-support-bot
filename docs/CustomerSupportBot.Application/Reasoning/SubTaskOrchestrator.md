# SubTaskOrchestrator

**Dosya:** `Services/Reasoning/SubTaskOrchestrator.cs`

## 1. Ne İşe Yarar

Compound query'lerde alt görev planını doğrular ve paralel/sıralı grupları oluşturur.
`ValidateExecutionPlan` fan-out sınırı, specialist adı, sıra, dependency ve entity bağlarını
yürütmeden önce kontrol eder. `Partition`, yalnızca tüm tool'ları salt-okunur ajanları paralel
gruplar; karma tool setli ajanlar intent etiketinden bağımsız olarak sıralıdır.

## 2. Hangi Amaçla Kullanılır

"Siparişim nerede VE şikayet açmak istiyorum" gibi çoklu istekleri ayrı agent'lara yönlendirir.

Bir alt görev yalnızca `Dependencies` içinde belirttiği tamamlanmış görevlerin soru/yanıtını
history olarak görür; ilgisiz kardeşlerin ve ana compound sorgunun bağlamı sızdırılmaz.

## Bağlantılar

- [ReasoningService.md](ReasoningService.md) — SubTask'ları üreten pipeline
- [../../CustomerSupportBot.Domain/Model/SubTask.md](../../CustomerSupportBot.Domain/Model/SubTask.md) — Alt görev modeli
