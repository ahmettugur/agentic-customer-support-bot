// Application/Services/Mcp/McpOptions.cs
// MCP (Model Context Protocol) sunucusu ayarları.

namespace CustomerSupportBot.Application.Services.Mcp;

/// <summary>
/// MCP, A2A ile AYNI kimlik altyapısını kullanır: <c>/mcp/catalog</c> Partner token'ı, <c>/mcp/customer</c> tek müşteriye
/// kilitli özne token'ı ister (token değişimi: <c>/auth/a2a/token-exchange</c>). Bu yüzden <c>Mcp:Enabled</c> için
/// <c>A2A:Enabled</c> de açık olmalıdır. Varsayılan KAPALI: kapalı kanal yayında olmaz.
/// </summary>
public sealed class McpOptions
{
    public const string SectionName = "Mcp";

    public bool Enabled { get; set; }

    /// <summary>İstek gövdesi üst sınırı (bayt). Tool argümanları küçüktür; büyük gövde kötüye kullanımdır.</summary>
    public int MaxRequestBytes { get; set; } = 16 * 1024;
}
