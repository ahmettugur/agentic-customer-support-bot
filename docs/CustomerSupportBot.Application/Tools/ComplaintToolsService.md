# ComplaintToolsService

**Dosya:** `Services/Tools/ComplaintToolsService.cs`

## 1. Ne İşe Yarar

Şikayet domain tool'larını implement eder — şikayet sorgulama, şikayet oluşturma, şikayet detay getirme.

## 2. Hangi Amaçla Kullanılır

`ComplaintAgent` bu tool'ları çağırır. `ToolResult` formatında standart dönüş üretir.

> 💡 **Analiz notu:** Şikayet yönetim departmanı gibi — "şikayet kaydet", "şikayeti sorgula" isteklerini DB üzerinden gerçekleştirir.

## Bağlantılar

- [OrderToolsService.md](OrderToolsService.md) — Benzer tool servisi
- [../../CustomerSupportBot.Domain/Model/ToolResult.md](../../CustomerSupportBot.Domain/Model/ToolResult.md) — Dönüş formatı
