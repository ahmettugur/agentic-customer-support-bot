// Api/Extensions/McpServicesExtensions.cs
// MCP sunucusunun kaydı. Yalnızca Mcp:Enabled iken çağrılır (bkz. Program.cs).

using CustomerSupportBot.Adapters.Agents.Mcp;
using CustomerSupportBot.Api.Endpoints;
using ModelContextProtocol.Server;

namespace CustomerSupportBot.Api.Extensions;

public static class McpServicesExtensions
{
    public static IServiceCollection AddMcpChannel(this IServiceCollection services)
    {
        services.AddMcpServer()
            .WithHttpTransport(o =>
            {
                // STATELESS: her istek bağımsızdır. Durumlu oturum, ilk çağıranın kimliğiyle oturum açıp oturum kimliğini
                // paylaşan/çalan birine müşteri verisi açabilirdi; burada taşınacak bir oturum durumu da yok.
                o.Stateless = true;

                // Tool kümesi YOLA göre seçilir ve bilinmeyen yol boş küme alır (güvenli varsayılan).
                o.ConfigureSessionOptions = (http, options, _) =>
                {
                    var catalog = http.RequestServices.GetRequiredService<McpToolCatalog>();
                    var functions = McpEndpoints.ToolSetFor(http.Request.Path, catalog);

                    options.ServerInfo = new() { Name = "CustomerSupportBot", Version = "1.0.0" };
                    options.ToolCollection = new McpServerPrimitiveCollection<McpServerTool>();
                    foreach (var function in functions)
                    {
                        options.ToolCollection.Add(McpServerTool.Create(function, new McpServerToolCreateOptions
                        {
                            // İstemciler araçları bu ipuçlarıyla sınıflandırır; hepsi doğrudur (kümede yazma tool'u yok).
                            ReadOnly = true,
                            Destructive = false,
                            Idempotent = true,
                            OpenWorld = false
                        }));
                    }
                    return Task.CompletedTask;
                };
            });
        return services;
    }
}
