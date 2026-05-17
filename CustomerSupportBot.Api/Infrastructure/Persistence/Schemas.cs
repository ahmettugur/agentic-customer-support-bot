// Infrastructure/Persistence/Schemas.cs
// PostgreSQL şema isimlerinin tek noktadan yönetimi.
// Yeni tablo eklenirken hangi şemaya gideceği buradan seçilir.

namespace CustomerSupportBot.Api.Infrastructure.Persistence;

internal static class Schemas
{
    public const string Chat = "chat";
    public const string Hitl = "hitl";
    public const string Observability = "observability";
    public const string Analytics = "analytics";
    public const string Auth = "auth";
    public const string Personalization = "personalization";
    public const string Improvement = "improvement";
    public const string Workflow = "workflow";
}
