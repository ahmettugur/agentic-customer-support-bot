# SideEffectIdempotencyCache

**Dosya:** `Services/Tools/SideEffectIdempotencyCache.cs`

## 1. Ne İşe Yarar

Yan etkili tool çağrılarının (sipariş oluştur, iptal et, iade iste) **tekrar tespiti** (idempotency) sağlar. Aynı tool + parametreler kombinasyonu kısa sürede tekrar çağrılırsa cache'den dönülür.

> 💡 **Analiz notu:** LLM bazen aynı tool'u iki kez çağırır (retry loop). Bu cache olmazsa 2 aynı sipariş oluşturulur. Banka havalesi gibi düşün — "havale yap" butonuna iki kez basınca para iki kez gitmemeli.

## Bağlantılar

- [OrderToolsService.md](OrderToolsService.md) — Bu cache'i kullanan tool servisi
