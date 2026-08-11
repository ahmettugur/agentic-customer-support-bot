# ChatPortService

**Dosya:** `Services/Chat/ChatPortService.cs`  
**Implements:** `IChatPort`

## 1. Ne İşe Yarar

Projenin **ana orkestratör**üdür — kullanıcı mesajını alır, reasoning pipeline'ı çalıştırır, workflow'u başlatır, yanıtı döner. Tüm agentic akış bu servisten geçer.

## 2. Hangi Amaçla Kullanılır

SignalR hub veya HTTP endpoint kullanıcı mesajını aldığında bu servisi çağırır. Servis sırayla:

1. Session'ı yükler/oluşturur
2. ReasoningService'i çağırır
3. WorkflowRunner'ı çalıştırır (PlanningAgent → SpecialistAgent → ResponseAgent)
4. Yanıtı döner ve session'ı günceller

> 💡 **Analiz notu:** Bir orkestra şefi gibi — hangi enstrüman (agent) ne zaman çalacak, sıralama ne olacak hepsini yönetir.

## 3. Sorumlulukları

- ✅ Mesaj işleme akışını orkestre etmek
- ✅ Session yönetimi (yükle, oluştur, güncelle)
- ✅ Reasoning pipeline'ı tetiklemek
- ✅ WorkflowRunner'ı başlatmak
- ✅ Yanıtı real-time push etmek
- ❌ Agent mantığını yürütmek — bu WorkflowRunner'ın işi

## Bağlantılar

- [SessionPortService.md](SessionPortService.md) — Session CRUD
- [InputGuard.md](InputGuard.md) — Girdi validasyonu
- [ContextPipeline.md](ContextPipeline.md) — Bağlam pipeline
- [../Reasoning/ReasoningService.md](../Reasoning/ReasoningService.md) — Reasoning pipeline
