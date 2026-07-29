# ReplanService

**Dosya:** `Services/Reasoning/ReplanService.cs`  
**Implements:** `IReplanService`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Bir session'ın son kullanıcı mesajını (veya admin notunu) yeniden değerlendirerek tüm bot pipeline'ını tekrar çalıştırır; yeni yanıtı müşteriye iletir. "Replan" özelliği, admin panelinden oturuma müdahale edilerek bot davranışının değiştirilmesini sağlar.

---

## Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `ISessionManager` | Session ve geçmiş okuma/yazma |
| `IChatBridge` | Müşteriye bot mesajı yayınlama |
| `IAgentTeamPort` | Ajan workflow pipeline'ı |
| `IReasoningPort` | Reasoning pipeline |
| `IApprovalContextAccessor` | HITL onay scope'u açma |
| `ILogger<ReplanService>` | Hata loglama |

---

## `ExecuteAsync`

```csharp
Task ExecuteAsync(string sessionId, CancellationToken ct = default)
```

**Tam akış:**

```
1. ISessionManager.Get(sessionId)
   → session yoksa erken çık

2. ISessionManager.GetHistory(sessionId)
   → son kullanıcı mesajını bul (role == User)

3. effectiveQuery belirleme:
   - session.State.ReplanNote != null → admin notunu query olarak kullan
   - aksi hâlde → son kullanıcı mesajını kullan

4. query boşsa erken çık

5. IChatBridge.PublishBotTyping(sessionId, true)   ← "yazıyor..." göster

6. IReasoningPort.ReasonAsync(effectiveQuery, session, history)
   → ReasoningResult

7. IApprovalContextAccessor.SetScope(sessionId, null, effectiveQuery)
   → HITL onay scope'u aç

8. IAgentTeamPort.RunAsync(effectiveQuery, history, session, reasoningResult)
   → bot yanıtı

9. ISessionManager.AppendAssistantMessage(sessionId, response)
   → yanıtı geçmişe yaz

10. IChatBridge.PublishBotMessage(sessionId, response)
    → müşteriye yayınla

11. (finally) IChatBridge.PublishBotTyping(sessionId, false)

Hata durumunda:
   IChatBridge.PublishSystemMessage(sessionId, "⚠️ ... bir sorun oluştu.")
```

---

## Admin notu önceliği

Replan tetiklendiğinde `session.State.ReplanNote` varsa bu not müşterinin orijinal sorusu yerine pipeline'a query olarak verilir. Bu sayede admin şunu yapabilir:

1. Müşteriye özel bir talimat yaz: `"Müşteri sipariş iade istiyor, VIP statüsünü göz önünde bulundur"`
2. Bot tüm pipeline'ı bu not üzerinden yeniden çalıştırır
3. Eski mesaj hâlâ geçmişte bağlam için durur

---

## Replan tetikleyicileri

`ReplanService.ExecuteAsync` doğrudan çağrılmaz. Üst katmanlar tetikler:

| Tetikleyici | Kod | Açıklama |
|------------|-----|---------|
| `ChatSessionPortService.ReplanSessionAsync` | Admin paneli → "Yeniden Planla" butonu | session note + replan |
| `ChatSessionPortService.ReplanEscalationAsync` | Admin paneli → eskalasyon kapatma | escalation resolve + replan |

---

## Hata işleme

`ExecuteAsync` exception'ı dışarı sızdırmaz. Hata oluşursa:
1. Error logu yazar
2. Müşteriye `PublishSystemMessage` ile anlaşılır bir uyarı gönderir (exception detayı saklanır)
3. Caller'a exception fırlatmaz

---

## IReplanService arayüzü

```csharp
public interface IReplanService
{
    Task ExecuteAsync(string sessionId, CancellationToken ct = default);
}
```
