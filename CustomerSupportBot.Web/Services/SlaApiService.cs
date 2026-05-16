using System.Net.Http.Json;
using CustomerSupportBot.Web.Models;

namespace CustomerSupportBot.Web.Services;

public sealed class SlaApiService(HttpClient http)
{
    public async Task<SlaStatus?> GetStatusAsync()
    {
        try { return await http.GetFromJsonAsync<SlaStatus>("/sla/status"); }
        catch { return null; }
    }

    public async Task<List<SlaEvent>> GetEventsAsync(int count = 100)
    {
        try
        {
            var result = await http.GetFromJsonAsync<SlaEventsResponse>($"/sla/events?count={count}");
            return result?.Items ?? [];
        }
        catch { return []; }
    }
}
