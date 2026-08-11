# ApprovalRequest

**Dosya:** `Model/ApprovalRequest.cs`  
**Tür:** `class` (mutable)  
**İlişkili:** `ApprovalStatus` enum (aynı dosyada)

## 1. Ne İşe Yarar

Yüksek riskli tool çağrıları (sipariş oluşturma, şikayet kaydı, sipariş iptali, iade) için oluşturulan **HITL onay kaydı**dır. Admin bu kaydı onaylar, reddeder veya timeout ile otomatik reddedilir.

## 2. Hangi Amaçla Kullanılır

Specialist agent bir side-effect tool çağırdığında `ApprovalGateService` bu modeli oluşturur, `IApprovalQueue`'ya yazar ve kararı async olarak bekler. Admin panelinde "Bekleyen Onaylar" listesinde görüntülenir.

> 💡 **Analiz notu:** ATM'den para çekerken "Bu işlemi onaylıyor musunuz?" ekranı gibi düşün. Bot sipariş oluşturmak istiyor ama önce admin (veya kullanıcı) onaylamalı. Bu class o onay isteğinin tüm bilgisini tutar.

## 3. Sorumlulukları

- ✅ Onay isteğinin tüm bağlamını (tool, parametreler, session, customer, justification) taşımak
- ✅ Onay yaşam döngüsünü (Pending → Approved/Rejected/Expired) takip etmek
- ✅ Execution sonucunu (`ExecutionResult`) taşımak
- ❌ Onay mantığını yürütmek — bu `ApprovalGateService`'in işi
- ❌ Tool'u çalıştırmak — bu `IApprovalExecutionRouter`'ın işi

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- **Kim oluşturur:** `ApprovalGateService.ExecuteWithApprovalGateAsync()`
- **Kim karar verir:** Admin endpoint'leri (`ApprovalEndpoints`)
- **Kim yürütür:** `IApprovalExecutionRouter` — onay sonrası tool'u gerçekten çalıştırır
- **Kim saklar:** `IApprovalQueue` implementasyonları (InMemory / Postgres)

## 5. Neden Böyle Tasarlandı (Analiz notu)

> 💡 **`PendingApproval` vs `Approved`:** `ToolResult.PendingApproval` true döndüğünde iş HENÜZ yapılmadı — sadece onay kuyruğuna eklendi. Bu ayrım kritik: aksi halde bot "siparişiniz oluşturuldu" der ama aslında admin henüz onaylamamıştır.

> 💡 **`ReasonRequired` computed property:** High-risk tool'lar (cancel, return) için admin gerekçe yazmalı. Bu bilgi sunucuda tek noktadan belirlenir — panel eskiden kendi listesini tutup senkron kaybedince 400 hatası alıyordu.

> 💡 **`Justification` alanı:** Bu eskiden "preToolCheck.reasoning" olarak belgelenmişti ama doğru değildi — preToolCheck tool çalıştıktan SONRA üretilir, onay ise ÖNCE tetiklenir. O anda en bilgilendirici gerekçe PlanningAgent'ın rationale'ıdır.

## 6. Metotlar / Üyeler

| Üye | Tip | Açıklama |
| ----- | ----- | ---------- |
| `Id` | `string` | Benzersiz kimlik (12 char) |
| `SessionId` | `string?` | Hangi oturuma ait |
| `CustomerId` | `string?` | JWT-doğrulanmış müşteri ID |
| `TraceId` | `string?` | Bağlı trace ID |
| `ToolName` | `string` | Çağrılan tool (ör. "order_placement_tool") |
| `AgentName` | `string?` | Tool'u çağıran agent |
| `Parameters` | `Dictionary<string, object?>` | Tool parametreleri (admin'e gösterilir) |
| `UserQuery` | `string?` | Kullanıcının sorusu (bağlam) |
| `Justification` | `string?` | Neden çağrılıyor gerekçesi |
| `RequestedAt` | `DateTime` | İstek zamanı |
| `DecidedAt` | `DateTime?` | Karar zamanı (null = Pending) |
| `Status` | `ApprovalStatus` | Mevcut durum |
| `DecidedBy` | `string?` | Karar veren admin |
| `DecisionReason` | `string?` | Red/expire gerekçesi |
| `TimeoutSeconds` | `int` | Timeout süresi (config'ten) |
| `ExecutionResult` | `string?` | Tool yürütme sonucu (onay sonrası) |
| `ExecutedAt` | `DateTime?` | Yürütme zamanı |
| `CustomerSeenAt` | `DateTime?` | Müşterinin sonucu gördüğü zaman |
| `ReasonRequired` | `bool` | **Computed** — high-risk tool mu? |

### ApprovalStatus Enum

| Değer | Açıklama |
| ------- | ---------- |
| `Pending` | Onay bekliyor |
| `Approved` | Admin onayladı |
| `Rejected` | Admin reddetti |
| `Expired` | Timeout ile otomatik reddedildi |

## 7. Constructor Bağımlılıkları

Yok — saf veri sınıfı.

## Bağlantılar

- [../../CustomerSupportBot.Application/ApprovalGateService.md](../../CustomerSupportBot.Application/ApprovalGateService.md) — Onay gate servisi
- [ToolResult.md](ToolResult.md) — `PendingApproval` flag'i
- [EscalationRequest.md](EscalationRequest.md) — Eskalasyon (farklı mekanizma ama benzer HITL akışı)
