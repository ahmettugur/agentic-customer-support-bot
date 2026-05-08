# Routing — Smart Routing ve Skills-Based Escalation

Bu doküman eskalasyon yönlendirme mekanizmasını, skills-based eşleştirme algoritmasını ve insan temsilci yönetimini anlatır.

---

## 1. Genel Bakış

Bot bir sorunu çözemediğinde `needs_escalation` termination reason'ı üretir. Bu noktada **SkillsBasedRouter** devreye girer:

```
ResponseAgent → TERMINATE: reason=escalation_needed
    │
    ▼
ApprovalGateService.ProcessPendingEscalations
    │
    ├─ Reasoning trace'den required skills çıkar
    ├─ Müşteri profilinden ek skill'ler ekle (VIP → "vip")
    ├─ IHumanAgentRegistry'den adayları tara
    ├─ Skill match + dil match + load balance → skor hesapla
    ├─ En yüksek skorlu temsilciyi ata
    └─ EscalationRequest alanlarını doldur:
       SuggestedAgentId, SuggestedAgentName, MatchScore,
       RequiredSkills, Priority, RoutingNote
```

---

## 2. Konfigürasyon

```json
{
  "Routing": {
    "Enabled": true,
    "LoadBalancingEnabled": true,
    "LanguageWeight": 0.2,
    "MinMatchScore": 0.1,
    "IntentSkillMap": {
      "şikayet": ["complaint"],
      "sipariş_oluşturma": ["order"],
      "ürün_bilgisi": ["product"]
    },
    "ProfileKeywordSkillMap": {
      "VIP": "vip",
      "kurumsal": "enterprise"
    },
    "SeedAgents": [
      {
        "Id": "agent-ayse",
        "DisplayName": "Ayşe Yılmaz",
        "Skills": ["complaint", "refund", "vip"],
        "Languages": ["tr"],
        "MaxConcurrentLoad": 5,
        "Priority": 1
      }
    ]
  }
}
```

| Ayar | Açıklama |
|------|----------|
| `Enabled` | `false` → routing devre dışı; eskalasyonlar atanmamış kalır |
| `LoadBalancingEnabled` | Yük dengeleme aktif mi? |
| `LanguageWeight` | Skor formülünde dil eşleşme ağırlığı (0-1) |
| `MinMatchScore` | Bu eşik altında `SuggestedAgentId` boş bırakılır |
| `IntentSkillMap` | Reasoning intent → skill tag mapping |
| `ProfileKeywordSkillMap` | Müşteri profil anahtar kelimeleri → skill tag |
| `SeedAgents` | Uygulama başlangıcında yüklenen insan temsilci listesi |

---

## 3. Skill Çıkarım Süreci

Router, aşağıdaki kaynaklardan skill gereksinimlerini çıkarır:

### 3.1 IntentSkillMap (Reasoning trace'den)

```
Reasoning intent = "şikayet"
IntentSkillMap["şikayet"] = ["complaint"]
→ RequiredSkills += "complaint"
```

### 3.2 ProfileKeywordSkillMap (Müşteri profilinden)

```
CustomerProfile.Tags = ["VIP", "sık müşteri"]
ProfileKeywordSkillMap["VIP"] = "vip"
→ RequiredSkills += "vip"
```

### 3.3 Diğer Kaynaklardan

- Tool adından: `complaint_registration_tool` → `"complaint"`
- Escalation reason metninden keyword taraması

---

## 4. Eşleştirme Algoritması

`SkillsBasedRouter` her aday temsilci için skor hesaplar:

```
Score = SkillMatchRatio × (1 - LanguageWeight)
      + LanguageMatch × LanguageWeight
      - LoadPenalty
```

| Bileşen | Açıklama |
|---------|----------|
| **SkillMatchRatio** | Temsilcinin sahip olduğu matching skill sayısı / toplam required skill |
| **LanguageMatch** | Müşteri dili temsilcinin dil listesinde varsa 1.0, yoksa 0.0 |
| **LoadPenalty** | `CurrentLoad / MaxConcurrentLoad` — yüksek yük ceza alır |

Eğer `Score < MinMatchScore` ise temsilci önerilmez.

---

## 5. Yük Yönetimi (Load Tracking)

### Load Artışı

Eskalasyon oluşturulduğunda atanan temsilcinin `CurrentLoad` +1 artar:

```csharp
registry.IncrementLoad(suggestedAgentId);
```

### Load Azalışı

`WireRoutingLoadTracking` uygulama başlangıcında `IEscalationSink.RequestDecided` event'ine bağlanır:

```csharp
escalations.RequestDecided += (_, args) =>
{
    if (args.AssignedAgentId is not null)
        registry.DecrementLoad(args.AssignedAgentId);
};
```

Eskalasyon resolve veya dismiss olduğunda temsilcinin yükü otomatik azalır.

---

## 6. İnsan Temsilci Yönetimi

### API Endpoint'leri

| Endpoint | Metod | Açıklama |
|----------|-------|----------|
| `/agents` | `GET` | Tüm temsilcileri listele |
| `/agents` | `POST` | Yeni temsilci ekle |
| `/agents/{id}` | `PUT` | Temsilci bilgilerini güncelle |
| `/agents/{id}` | `DELETE` | Temsilci sil |

### HumanAgent Modeli

```json
{
  "id": "agent-ayse",
  "displayName": "Ayşe Yılmaz",
  "skills": ["complaint", "refund", "vip"],
  "languages": ["tr"],
  "maxConcurrentLoad": 5,
  "currentLoad": 2,
  "priority": 1,
  "isAvailable": true
}
```

---

## 7. EscalationRequest Routing Alanları

Routing sonrası `EscalationRequest`'e eklenen alanlar:

```json
{
  "id": "esc-001",
  "sessionId": "s123",
  "reason": "Müşteri iade konusunda ısrar ediyor",
  "requiredSkills": ["complaint", "refund"],
  "priority": "Normal",
  "suggestedAgentId": "agent-ayse",
  "suggestedAgentName": "Ayşe Yılmaz",
  "matchScore": 0.85,
  "routingNote": "Skill match: complaint, refund (2/2). Dil: tr ✓. Load: 2/5."
}
```

---

## 8. Devre Dışı Bırakma

`Routing.Enabled = false` yapıldığında:

- Eskalasyonlar routing alanları boş olarak oluşturulur
- Admin manuel atama yapabilir
- Yük takibi yapılmaz

---

## Çapraz Referanslar

- **Eskalasyon akışı** → [runtime.md](runtime.md#b-eskalasyon-yolu)
- **HITL pattern** → [patterns.md](patterns.md#20-human-in-the-loop)
- **API endpoint'leri** → [api.md](api.md#10-smart-routing-endpoints)
- **Mimari** → [architecture.md](architecture.md)
