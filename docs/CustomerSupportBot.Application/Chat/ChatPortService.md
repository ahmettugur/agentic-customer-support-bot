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
- ✅ Oturumu login'li müşteriye bağlamak ve sahipliğini doğrulamak — aşağıya bakın
- ❌ Agent mantığını yürütmek — bu WorkflowRunner'ın işi

## 4. Oturum sahipliği

`BindAuthenticatedCustomerAsync`, `SessionIdentityBinder.TryBindAsync`'e delege eder ve ihlalde
`UnauthorizedSessionAccessException` fırlatır (→ HTTP 403).

Eskiden burada yalnızca **bağlama** vardı: oturum zaten başka bir müşteriye bağlıysa metot
sessizce çıkıyor, tur o oturumun kimliğiyle devam ediyordu. Yani müşteri B, müşteri A'nın
`sessionId`'sini göndererek A'nın konuşma geçmişini alabiliyor ve tool'ları A adına
çalıştırabiliyordu.

Kontrol iki katmanlıdır — asıl koruma uç katmanındadır (`ChatEndpoints`), buradaki ise
derinlemesine savunmadır: bu port'u çağıracak başka bir giriş (ör. A2A) uç kontrolünü
atlarsa koruma yerinde kalır. Ayrıntı: [security.md](../../security.md#oturum--müşteri-bağı).

## Bağlantılar

- [SessionPortService.md](SessionPortService.md) — Session CRUD
- [InputGuard.md](InputGuard.md) — Girdi validasyonu
- [ContextPipeline.md](ContextPipeline.md) — Bağlam pipeline
- [../Reasoning/ReasoningService.md](../Reasoning/ReasoningService.md) — Reasoning pipeline
