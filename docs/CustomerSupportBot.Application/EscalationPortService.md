# EscalationPortService

**Dosya:** `Services/Escalation/EscalationPortService.cs`  
**Implements:** `IEscalationPort`  
**Yaşam döngüsü:** Singleton

## Ne yapar?

Eskalasyon isteklerini yönetir ve `IEscalationSink` üzerindeki driven port event'lerini `IEscalationPort` driving port event'lerine köprüler. API katmanı bu servis üzerinden eskalasyonları listeler ve karar verir; admin SSE akışı da bu event'lerle beslenir.

---

## Constructor bağımlılıkları

| Bağımlılık | Açıklama |
|-----------|---------|
| `IEscalationSink` | Eskalasyon kayıt ve sorgulama deposu |
| `ILogger<EscalationPortService>` | Loglama |

---

## Event köprüleme (Bridge Pattern)

Constructor'da `IEscalationSink` event'lerine abone olunur ve bu event'ler **aynı isimle** driving port event'leri olarak yeniden yayınlanır (yeniden adlandırma yapılmaz):

```
IEscalationSink.RequestCreated  →  IEscalationPort.RequestCreated
IEscalationSink.RequestDecided  →  IEscalationPort.RequestDecided
```

Bu sayede API katmanı `IEscalationSink`'i doğrudan bilmeden event akışına katılabilir.

---

## Metodlar

Tüm metodlar **senkrondur** (`Task` yok, `CancellationToken` almazlar).

### `Create`

```csharp
EscalationRequest Create(EscalationRequest request)
```

Yeni eskalasyon isteği oluşturur — ayrık parametreler yerine hazır `EscalationRequest` nesnesi alır. `IEscalationSink.Create` çağırır.

---

### `GetOpen`

```csharp
IReadOnlyList<EscalationRequest> GetOpen()
```

Açık (henüz karar verilmemiş) tüm eskalasyonları döner. `sessionId` filtresi **yoktur** — çağıran taraf gerekirse kendi filtreler.

---

### `GetRecent`

```csharp
IReadOnlyList<EscalationRequest> GetRecent(int count = 50)
```

Son `count` eskalasyonu döner (açık ve kapalı tümü).

---

### `Get`

```csharp
EscalationRequest? Get(string id)
```

Tekil eskalasyon kaydını döner.

---

### `Decide`

```csharp
bool Decide(string id, string action, string? assignedTo = null, string? resolution = null)
```

Eskalasyon kararı verir. `action` — `"acknowledge"` | `"resolve"` | `"dismiss"` string değeri alır (bool değil). `IEscalationSink.Decide` çağırır.

---

## EscalationPriority seviyeleri

| Seviye | Değer | Açıklama |
|--------|-------|---------|
| `Low` | 1 | Düşük öncelik |
| `Normal` | 2 | Normal öncelik |
| `High` | 3 | Yüksek öncelik (ComplaintAgent → otomatik High) |
| `Critical` | 4 | Kritik; SLA ihlalinde otomatik boost |

---

## EscalationPolicyService ile ilişki

`EscalationPortService` sadece CRUD ve event bridge işlevi görür. İş kuralları (hangi oturum eskalasyona alınsın, hangi temsilciye yönlendirilsin) `EscalationPolicyService` tarafından yönetilir. İkisi birbirinden bağımsız singleton'lardır.

---

## API endpoint'leri

Bkz. [Endpoints-Admin.md](../api/Endpoints-Admin.md) — eskalasyon endpoint'lerinin gerçek route'ları için.
