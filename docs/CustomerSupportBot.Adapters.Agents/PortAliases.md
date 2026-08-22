# PortAliases

- **Kaynak:** `CustomerSupportBot.Adapters.Agents/PortAliases.cs`
- **Tür:** `Global Usings`
- **Namespace:** `CustomerSupportBot.Adapters.Agents`

## Ne işe yarar?

`PortAliases.cs`, `CustomerSupportBot.Adapters.Agents` projesi genelinde en sık kullanılan Application katmanı Outbound port namespace'lerini (`Persistence`, `Observability`, `Outbound`) `global using` direktifleri ile projeye dahil eder.

## İçerik

```csharp
global using CustomerSupportBot.Application.Ports.Outbound.Persistence;
global using CustomerSupportBot.Application.Ports.Outbound.Observability;
global using CustomerSupportBot.Application.Ports.Outbound;
```
