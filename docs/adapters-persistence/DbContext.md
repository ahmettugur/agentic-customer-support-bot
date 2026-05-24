# EF Core DbContext

**Dosyalar:**  
- `EfCore/CustomerSupportDbContext.cs` — ana DbContext  
- `EfCore/Schemas.cs` — şema sabitleri  
- `EfCore/PersistenceOptions.cs` — provider seçimi  
- `EfCore/DesignTimeDbContextFactory.cs` — migration üretimi  
- `EfCore/PersistenceServiceCollectionExtensions.cs` — DI kaydı  

---

## CustomerSupportDbContext

**Base:** `DbContext` (EF Core 9 + Npgsql)

8 PostgreSQL şemasında 16 tablo barındırır. Tüm entity konfigürasyonları `assembly.GetTypes()` ile otomatik uygulanır — yeni entity eklendiğinde DbContext dosyasına dokunmak gerekmez.

### DbSet'ler

| DbSet | Şema | Tablo |
|-------|------|-------|
| `Sessions` | `chat` | `sessions` |
| `Messages` | `chat` | `messages` |
| `ChatBridgeMessages` | `chat` | `bridge_messages` |
| `ChatSessionModes` | `chat` | `session_modes` |
| `ApprovalRequests` | `hitl` | `approval_requests` |
| `Escalations` | `hitl` | `escalations` |
| `HumanAgents` | `hitl` | `human_agents` |
| `ReasoningTraces` | `observability` | `reasoning_traces` |
| `LlmCallUsages` | `observability` | `llm_call_usage` |
| `Ratings` | `analytics` | `ratings` |
| `SlaEvents` | `analytics` | `sla_events` |
| `Users` | `auth` | `users` |
| `RefreshTokens` | `auth` | `refresh_tokens` |
| `CustomerProfiles` | `personalization` | `customer_profiles` |
| `Lessons` | `improvement` | `lessons` |
| `WorkflowDefinitions` | `workflow` | `workflow_definitions` |

---

## Schemas.cs

```csharp
public static class Schemas
{
    public const string Chat           = "chat";
    public const string Hitl           = "hitl";
    public const string Observability  = "observability";
    public const string Analytics      = "analytics";
    public const string Auth           = "auth";
    public const string Personalization = "personalization";
    public const string Improvement    = "improvement";
    public const string Workflow       = "workflow";
}
```

Tüm entity konfigürasyonlarında tablo şeması bu sabitler üzerinden referans verilir.

---

## PersistenceOptions

```json
{
  "Persistence": {
    "Provider": "Postgres",
    "ConnectionString": "Host=...;Database=csbot;Username=csbot;Password=..."
  }
}
```

| Değer | Açıklama |
|-------|---------|
| `"InMemory"` | EF Core kullanılmaz |
| `"Postgres"` | DbContext + DbContextFactory kayıt edilir |

---

## PersistenceServiceCollectionExtensions

Postgres seçildiğinde:

```csharp
services.AddDbContext<CustomerSupportDbContext>(opts =>
    opts.UseNpgsql(connectionString,
        npgsql => npgsql.EnableRetryOnFailure(3,
            TimeSpan.FromSeconds(5), null)));

services.AddDbContextFactory<CustomerSupportDbContext>(...);
```

**Neden `DbContextFactory`?**  
Singleton adapter'lar (örn. `PostgresSessionManager`) `DbContext`'i doğrudan inject edemez — Scoped bir servis Singleton'a inject edilemez. `IDbContextFactory<T>` kullanarak her işlemde kısa ömürlü `DbContext` yaratılır:

```csharp
await using var db = await _factory.CreateDbContextAsync(ct);
// işlem
```

---

## DesignTimeDbContextFactory

Migration üretimi için `dotnet ef` araçlarının kullandığı factory.

```csharp
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CustomerSupportDbContext>
{
    public CustomerSupportDbContext CreateDbContext(string[] args)
    {
        // appsettings.json oku → connection string al → DbContext oluştur
    }
}
```

**Migration oluşturmak için:**
```bash
cd CustomerSupportBot.Adapters.Persistence
dotnet ef migrations add <MigrationName> --project . --startup-project ../CustomerSupportBot.Api
dotnet ef database update
```

---

## Entity konfigürasyonları

`EfCore/Configurations/` altında her entity için `IEntityTypeConfiguration<T>` sınıfı vardır. Önemli konfigürasyonlar:

- **JSONB sütunlar:** `StateJson`, `ParametersJson`, `SkillsJson`, `StepsJson` gibi karmaşık tipler PostgreSQL JSONB olarak saklanır
- **Enum'lar:** `Status`, `Mode`, `Role` string olarak saklanır (migration stabilitesi için)
- **FK ilişkileri:** `messages.session_id → sessions.session_id`, `users.linked_agent_id → human_agents.id`

### JSONB örnekleri

```csharp
// SessionConfiguration
b.Property(e => e.StateJson)
    .HasColumnType("jsonb")
    .HasColumnName("state");

// ApprovalRequestConfiguration
b.Property(e => e.ParametersJson)
    .HasColumnType("jsonb")
    .HasColumnName("parameters");
```

JSONB domain model→entity→domain dönüşümleri `PersistenceHydrator` veya her adapter'ın kendi mapper metodunda yapılır.
