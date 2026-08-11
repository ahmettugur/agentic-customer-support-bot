# ReplanService

**Dosya:** `Services/Reasoning/ReplanService.cs`  
**Implements:** `IReplanService`

## 1. Ne İşe Yarar

Admin'in "Yeniden Planla" butonuna bastığında `SessionState.ForceReplanNextTurn` flag'ini set eder ve opsiyonel bir not ekler. Bir sonraki turda PlanningAgent bu flag'i görür ve fresh context ile başlar.

## 2. Hangi Amaçla Kullanılır

Admin panelinden müdahale — bot yanlış agent'a yönlendirdiyse admin düzeltme yapabilir.

> 💡 **Analiz notu:** GPS navigasyonun "rota yeniden hesapla" butonu gibi — bot yanlış yöne gittiğinde admin düzeltir.

## Bağlantılar

- [../../CustomerSupportBot.Domain/Model/SessionState.md](../../CustomerSupportBot.Domain/Model/SessionState.md) — ForceReplanNextTurn flag
