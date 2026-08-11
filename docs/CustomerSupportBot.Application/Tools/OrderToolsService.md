# OrderToolsService

**Dosya:** `Services/Tools/OrderToolsService.cs`

## 1. Ne İşe Yarar

Sipariş domain tool'larını implement eder — sipariş sorgulama, sipariş oluşturma, sipariş iptal, iade talebi, son sipariş getirme.

## 2. Hangi Amaçla Kullanılır

`OrderAgent` bu tool'ları çağırır. Yüksek riskli tool'lar (sipariş oluşturma, iptal, iade) HITL onay gate'inden geçer.

> 💡 **Analiz notu:** Sipariş departmanı gibi — "sipariş ver", "siparişi iptal et", "iade iste" isteklerini gerçekleştirir.

## Bağlantılar

- [ComplaintToolsService.md](ComplaintToolsService.md) — Benzer tool servisi
- [ProductToolsService.md](ProductToolsService.md) — Benzer tool servisi
