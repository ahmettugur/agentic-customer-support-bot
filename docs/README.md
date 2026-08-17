# CustomerSupportBot — Dokümantasyon

Bu dizin **Agentic Customer Support Bot** projesinin tüm teknik dokümantasyonunu barındırır.

> 💡 **Analiz notu:** Bu dokümanlar size projeyi tanıtmak için yazılmıştır. Her dosya "ne işe yarar", "neden böyle tasarlandı" ve "diğer parçalarla nasıl bağlantılı" sorularını yanıtlar.

---

## Mimari Bakış

```
┌──────────────────────────────────────────────────────┐
│                  CustomerSupportBot.Web               │ ← Blazor Server UI
│                  CustomerSupportBot.Api                │ ← HTTP/SignalR endpoints
├──────────────────────────────────────────────────────┤
│              CustomerSupportBot.Application            │ ← Port servisleri & iş akışları
├──────────────────────────────────────────────────────┤
│                CustomerSupportBot.Domain               │ ← Saf iş modeli (bağımlılık yok)
├──────────────────────────────────────────────────────┤
│  Adapters.AI  │  Adapters.Agents  │  Adapters.Persist  │ ← Altyapı implementasyonları
└──────────────────────────────────────────────────────┘
```

---

## Katman Dokümanları

| Katman | README | Dosya Sayısı | Açıklama |
| -------- | -------- | :------------: | ---------- |
| [Domain](CustomerSupportBot.Domain/README.md) | ✅ | 40 | Saf iş modeli — Model, Services, Exceptions |
| [Application](CustomerSupportBot.Application/README.md) | ✅ | 43 | Port servisleri — Chat, Reasoning, Tools, HITL |
| [Api](CustomerSupportBot.Api/README.md) | ✅ | 15 | Endpoint'ler, Workers, Infrastructure |
| [Adapters.AI](CustomerSupportBot.Adapters.AI/README.md) | ✅ | 8 | LLM client'lar, embedding, vector memory |
| [Adapters.Agents](CustomerSupportBot.Adapters.Agents/README.md) | ✅ | 27 | Workflow runner, agent'lar, routing |
| [Adapters.Persistence](CustomerSupportBot.Adapters.Persistence/README.md) | ✅ | 14 | EF Core, InMemory, Postgres, seed data |
| [Web](CustomerSupportBot.Web/README.md) | ✅ | 32 | Blazor Server UI — Pages, Components, Layout |

---

## Konu Bazlı Rehberler

| Doküman | Kapsam |
| --------- | -------- |
| [architecture.md](architecture.md) | Hexagonal mimari ve katman kuralları |
| [agentic-patterns.md](agentic-patterns.md) | Agentic AI desenleri (MAF, ReAct, HITL) |
| [reasoning.md](reasoning.md) | Reasoning pipeline detayları |
| [intelligence.md](intelligence.md) | AI/LLM entegrasyon detayları |
| [security.md](security.md) | JWT, HITL güvenlik, poisoning koruması |
| [deployment.md](deployment.md) | Deploy kılavuzu |
| [developer-guide.md](developer-guide.md) | Geliştirici rehberi |
| [debugging-chat.md](debugging-chat.md) | Chat debug rehberi |
| [operations.md](operations.md) | Operasyon rehberi (SLA, monitoring) |
| [evaluation.md](evaluation.md) | Kalite değerlendirmesi |
| [class-reference.md](class-reference.md) | Hızlı class referansı |

---

## Mimari Kararlar (ADR)

Geri alınması pahalı, gerekçesi koddan okunamayan kararlar. "Neden böyle yapılmamış?"
sorusunun cevabı burada.

| ADR | Karar |
|-----|-------|
| [0001](adr/0001-workflow-durability.md) | MAF workflow'u durable execution için değil, tur-içi koordinasyon için kullanılıyor — checkpoint/resume kapalı, kalıcılık Postgres'te |
| [0002](adr/0002-conversation-context-window.md) | Konuşma özeti, özetlediği turların **yerine** geçer (yanına değil); prompt büyüklüğü `EstimatedTokens` ile ölçülür |

---

## Okuma Sırası (Stajyerler İçin)

1. **Bu README** — genel bakış
2. **[architecture.md](architecture.md)** — hexagonal mimari ve katman kuralları
3. **[Domain/README.md](CustomerSupportBot.Domain/README.md)** — iş modeli (en basit katman)
4. **[Application/README.md](CustomerSupportBot.Application/README.md)** — port servisleri
5. **[agentic-patterns.md](agentic-patterns.md)** — agentic AI desenleri
6. **[reasoning.md](reasoning.md)** — reasoning pipeline
7. **Adapters** — ilgilendiğin alt konuya göre
8. **[Api](CustomerSupportBot.Api/README.md)** ve **[Web](CustomerSupportBot.Web/README.md)** — frontend
