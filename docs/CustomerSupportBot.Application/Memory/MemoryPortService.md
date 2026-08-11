# MemoryPortService

**Dosya:** `Services/Memory/MemoryPortService.cs`

## 1. Ne İşe Yarar

Müşteri hafıza (memory) operasyonlarını yönetir — konuşma geçmişinin semantic embedding'lerle saklanması ve geri çağrılması.

## 2. Hangi Amaçla Kullanılır

Müşteri bir sonraki görüşmede önceki konuşmalardan bağlam almak için kullanılır. Semantic search ile en ilgili geçmiş bilgileri bulur.

> 💡 **Analiz notu:** Bir doktorun hasta dosyasını açması gibi — önceki ziyaretlerdeki notları okuyarak mevcut durumu daha iyi anlar.

## Bağlantılar

- [SemanticMemoryService.md](SemanticMemoryService.md) — Semantic search operasyonları
- [KnowledgeArticleService.md](KnowledgeArticleService.md) — Bilgi bankası
