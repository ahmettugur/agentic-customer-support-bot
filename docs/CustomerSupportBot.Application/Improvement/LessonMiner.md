# LessonMiner

**Dosya:** `Services/Improvement/LessonMiner.cs`

## 1. Ne İşe Yarar

Düşük SelfCritique puanlı veya negatif sentiment'li konuşma trace'lerini analiz ederek iyileştirme önerileri çıkarır.

## 2. Hangi Amaçla Kullanılır

Background job olarak çalışır. ReasoningTrace'lerdeki SelfCritique skoru threshold'un altındaysa o trace'i inceleyerek "Lesson" (öğrenilmiş ders) üretir.

> 💡 **Analiz notu:** Bir futbol takımının maç sonrası video analizi gibi — kötü giden şeyleri inceler ve "bir dahaki maçta şunu yapalım" önerileri çıkarır.

## Bağlantılar

- [ImprovementsPortService.md](ImprovementsPortService.md) — Lesson CRUD
- [../../CustomerSupportBot.Domain/Model/SelfCritique.md](../../CustomerSupportBot.Domain/Model/SelfCritique.md) — Kalite değerlendirmesi
