# InputGuard

**Dosya:** `Services/Chat/InputGuard.cs`

## 1. Ne İşe Yarar

Kullanıcı girdisini workflow'a girmeden önce doğrular — uzunluk limiti, rate limit, boş mesaj kontrolü, spam tespiti.

> 💡 **Analiz notu:** Bir binanın güvenlik kapısı gibi — içeri girmeden önce kimlik kontrolü ve güvenlik taraması.

## Bağlantılar

- [ChatPortService.md](ChatPortService.md) — Bu guard'ı çağıran orkestratör
