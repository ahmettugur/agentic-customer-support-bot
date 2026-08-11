# SubTaskOrchestrator

**Dosya:** `Services/Reasoning/SubTaskOrchestrator.cs`

## 1. Ne İşe Yarar

Compound query'lerde (birden fazla niyet) alt görevleri paralel veya sıralı olarak yürütme planı oluşturur. `SubTask.Dependencies` alanına göre bağımlılık analizi yapar.

## 2. Hangi Amaçla Kullanılır

"Siparişim nerede VE şikayet açmak istiyorum" gibi çoklu istekleri ayrı agent'lara yönlendirir.

> 💡 **Analiz notu:** Bağımlılığı olmayan görevler paralel, bağımlı olanlar sıralı çalışır — proje yönetimindeki Gantt şeması gibi.

## Bağlantılar

- [ReasoningService.md](ReasoningService.md) — SubTask'ları üreten pipeline
- [../../CustomerSupportBot.Domain/Model/SubTask.md](../../CustomerSupportBot.Domain/Model/SubTask.md) — Alt görev modeli
