# ReasoningMessageBuilder

**Dosya:** `Services/Reasoning/ReasoningMessageBuilder.cs`  
**Yaşam döngüsü:** ReasoningService tarafından oluşturulur (internal)

## 1. Ne İşe Yarar

Reasoning LLM'ine gönderilecek mesaj listesini kurar: system prompt + verified entity bloğu + konuşma geçmişi + admin replan hint + kullanıcı sorgusu.

## 2. Hangi Amaçla Kullanılır

Reasoning Pipeline'ın **L1 katmanı**dır. EntityVerifier sonuçlarını ve session state bilgilerini system prompt template'ine enjekte ederek LLM'e bağlam sağlar.

> 💡 **Analiz notu:** Bir avukatın mahkemeye girmeden önce dosyasını hazırlaması gibi — önceki duruşma kayıtları (history), deliller (verified entities), müvekkil bilgileri (session state) hepsini tek bir "dava dosyası" haline getirir.

## 3. Sorumlulukları

- ✅ System prompt template'i doldurmak (IPromptRepository'den)
- ✅ Verified entity bloğunu prompt'a enjekte etmek
- ✅ History'yi eklemek
- ✅ Admin replan hint'ini eklemek (ForceReplanNextTurn=true ise)
- ❌ LLM çağrısı yapmak
- ❌ Entity doğrulaması yapmak

## 4. Metotlar

| Metot | Açıklama |
|-------|----------|
| `Build(query, session, history, verified)` | Tam mesaj listesi oluşturur |

## 7. Constructor Bağımlılıkları

```csharp
public ReasoningMessageBuilder(
    IPromptRepository prompts  // Prompt şablonlarını okuyan port
)
```

## Bağlantılar

- [ReasoningService.md](ReasoningService.md) — Bu builder'ı kullanan servis
- [EntityVerifier.md](EntityVerifier.md) — Verified entities kaynağı
