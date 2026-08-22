# Schemas

- **Kaynak:** `CustomerSupportBot.Adapters.Persistence/EfCore/Schemas.cs`
- **Tür:** `internal static class`
- **Namespace:** `CustomerSupportBot.Adapters.Persistence.EfCore`

## Ne işe yarar?

`Schemas`, PostgreSQL veritabanında kullanılan tüm şema adlarını tek bir merkezde toplayan ve sabit olarak tanımlayan sınıftır.

## Hangi amaçla kullanılır`?

EF Core Entity Configuration sınıflarında (`builder.ToTable("products", Schemas.Catalog)`) tablo şemalarını sabit dizgeler yerine tip güvenli olarak belirtmek için kullanılır.

## Tanımlı Şemalar

```csharp
internal static class Schemas
{
    public const string Chat = "chat";
    public const string Hitl = "hitl";
    public const string Observability = "observability";
    public const string Analytics = "analytics";
    public const string Auth = "auth";
    public const string Personalization = "personalization";
    public const string Improvement = "improvement";
    public const string Catalog = "catalog";
    public const string Knowledge = "knowledge";
}
```
