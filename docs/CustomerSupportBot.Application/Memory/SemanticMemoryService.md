# SemanticMemoryService

**Dosya:** `Services/Memory/SemanticMemoryService.cs`

## 1. Ne İşe Yarar

Semantic search ile geçmiş konuşma fragmentlerini bulur — embedding vektör benzerliği kullanır.

## 2. Hangi Amaçla Kullanılır

ContextProvider pipeline'ında `SemanticMemoryContextProvider` bu servisi çağırarak reasoning prompt'una geçmiş bağlam enjekte eder.

## Bağlantılar

- [MemoryPortService.md](MemoryPortService.md) — Memory orkestratör
- [../Providers/SemanticMemoryContextProvider.md](../Providers/ContextProviders.md) — Bu servisi kullanan provider
