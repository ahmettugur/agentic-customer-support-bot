# Admin ve Genel Endpoint DTO'ları (AdminModels, EndpointModels)

- **Kaynaklar:**
  - `CustomerSupportBot.Api/Models/AdminModels.cs`
  - `CustomerSupportBot.Api/Models/EndpointModels.cs`
- **Namespace:** `CustomerSupportBot.Api.Models`
- **Tipler:** `ChatTakeoverInput`, `ChatAdminMessageInput`, `ReplanInput`, `RerouteInput`,
  `ImprovementDecision`, `AdminNoteInput`, `RatingInput`

## 1. Ne İşe Yarar

Admin/agent panelinin ve genel amaçlı uçların HTTP istek gövdelerini tanımlayan DTO'lardır.
`PortAliases.cs`'teki `global using CustomerSupportBot.Api.Models;` sayesinde tüm `Endpoints/`
dosyalarında ek `using` olmadan doğrudan kullanılabilirler.

## 2. Hangi Amaçla Kullanılır

Her tip, ait olduğu endpoint grubunun kabul ettiği JSON gövdenin şeklini tanımlar (bkz. tablo).

## 3. Sorumlulukları

Yalnızca alan tanımı; doğrulama mantığı yok (doğrulama, ilgili endpoint lambda'sında veya
Application port'unda yapılır — ör. `AnalyticsEndpoints`'teki `Stars` 1-5 kontrolü).

## 4. Diğer Katman ve Bileşenlerle İlişkileri

- [AdminAndHitl.md](../Endpoints/AdminAndHitl.md) — `ChatTakeoverInput`, `ChatAdminMessageInput`,
  `ReplanInput` burada kullanılır.
- [ObservabilityAndTelemetry.md](../Endpoints/ObservabilityAndTelemetry.md) — `RerouteInput`
  (`AgentsEndpoints`), `RatingInput` (`AnalyticsEndpoints`).
- [Intelligence.md](../Endpoints/Intelligence.md) — `ImprovementDecision`
  (`ImprovementsEndpoints`), `AdminNoteInput` (`PersonalizationEndpoints`).

## 5. Kullanılma Nedeni ve Tasarım Yaklaşımı

- **`ReplanInput.Note` müşteriye GÖSTERİLMEZ** — kod içi belge yorumu bunu açıkça belirtir: not,
  `PlanningAgent`'a tek seferlik (one-shot) bir ipucu olarak gider, konuşma geçmişine yazılmaz.
  Bu, DTO seviyesinde belgelenmiş önemli bir davranışsal kısıttır — bu alanı yanlışlıkla
  müşteriye yansıtan bir UI değişikliği admin/agent iç notlarını sızdırırdı.
- **Aynı `ReplanInput`, iki farklı endpoint grubunda (eskalasyon kartından ve doğrudan sohbet
  panelinden) paylaşılır:** iki farklı giriş noktasının aynı iş akışına (session'ı yeniden
  planlatma) gittiğini DTO seviyesinde de yansıtır — ayrı tipler tanımlansaydı, iki akışın aslında
  aynı şey olduğu daha az belirgin olurdu.
- **`RerouteInput`/`AdminNoteInput` `class` + `{ get; set; }`, diğerleri `record` (positional):**
  tutarsız bir stil farkı — muhtemelen farklı zamanlarda eklendiler. Davranışsal bir fark yok
  (ikisi de model binding ile aynı şekilde çalışır), sadece kod stili tutarsızlığı.

## 6. Metotlar / Üyeler

| Tip | Alanlar | Kullanıldığı Uç(lar) |
|---|---|---|
| `ChatTakeoverInput` | `HumanAgent?` | `POST /chat-sessions/{sid}/takeover`, `POST /agent/chat-sessions/{sid}/takeover` |
| `ChatAdminMessageInput` | `Text`, `HumanAgent?` | `POST /chat-sessions/{sid}/messages` (admin/agent) |
| `ReplanInput` | `RequestedBy?`, `Note?` | `POST /escalations/{id}/replan`, `POST /chat-sessions/{sid}/replan` (admin + agent varyantları) |
| `RerouteInput` | `AgentId?`, `Reason?` | `POST /escalations/{id}/reroute` |
| `ImprovementDecision` | `DecidedBy?`, `Reason?` | `POST /improvements/{id}/approve`, `/reject` |
| `AdminNoteInput` | `Note?` | `PUT /customers/{id}/profile/note` |
| `RatingInput` | `Stars`, `Feedback?` | `POST /sessions/{sid}/rating` |

## 7. Bağımlılıklar

Yok — saf veri tipleri.

## Bağlantılar

- [../README.md](../README.md) — Katman indeksi
- [../Endpoints/AdminAndHitl](../Endpoints/AdminAndHitl.md)
