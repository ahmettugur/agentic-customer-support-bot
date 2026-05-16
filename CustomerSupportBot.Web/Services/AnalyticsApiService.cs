using System.Net.Http.Json;
using CustomerSupportBot.Web.Models;

namespace CustomerSupportBot.Web.Services;

/// <summary>
/// Admin analytics endpoint'leri için C# servis katmanı.
/// admin.js'teki analyticsDashboard ve sessionAnalytics çağrılarının karşılığı.
/// </summary>
public sealed class AnalyticsApiService(HttpClient http)
{
    public async Task<AnalyticsDashboard?> GetDashboardAsync()
    {
        try
        {
            return await http.GetFromJsonAsync<AnalyticsDashboard>("/analytics/dashboard");
        }
        catch
        {
            return null;
        }
    }

    public async Task<object?> GetSessionAnalyticsAsync(string sessionId)
    {
        try
        {
            return await http.GetFromJsonAsync<object>($"/analytics/session/{sessionId}");
        }
        catch
        {
            return null;
        }
    }
}
