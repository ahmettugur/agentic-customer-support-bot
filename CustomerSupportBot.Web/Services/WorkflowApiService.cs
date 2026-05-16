using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Web.Models;

namespace CustomerSupportBot.Web.Services;

public sealed class WorkflowApiService(HttpClient http)
{
    private static readonly JsonSerializerOptions _pretty = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public async Task<List<WorkflowListEntry>> GetListAsync()
    {
        try
        {
            var result = await http.GetFromJsonAsync<WorkflowListResponse>("/workflows");
            return result?.Items ?? [];
        }
        catch { return []; }
    }

    public async Task<string?> GetRawAsync(string id)
    {
        try
        {
            var doc = await http.GetFromJsonAsync<JsonElement>($"/workflows/{Uri.EscapeDataString(id)}");
            return JsonSerializer.Serialize(doc, _pretty);
        }
        catch { return null; }
    }

    public async Task<(bool Ok, string? Error, string? SavedId)> SaveAsync(string? id, string rawJson)
    {
        try
        {
            var body = JsonSerializer.Deserialize<JsonElement>(rawJson);
            var content = JsonContent.Create(body);
            HttpResponseMessage response = id is not null
                ? await http.PutAsync($"/workflows/{Uri.EscapeDataString(id)}", content)
                : await http.PostAsync("/workflows", content);

            if (!response.IsSuccessStatusCode)
                return (false, $"HTTP {(int)response.StatusCode}", null);

            var saved = await response.Content.ReadFromJsonAsync<JsonElement>();
            var savedId = saved.TryGetProperty("id", out var p) ? p.GetString() : id;
            return (true, null, savedId);
        }
        catch (JsonException ex) { return (false, $"JSON hatası: {ex.Message}", null); }
        catch (Exception ex) { return (false, ex.Message, null); }
    }

    public async Task<(bool Ok, string? Error)> DeleteAsync(string id)
    {
        try
        {
            var response = await http.DeleteAsync($"/workflows/{Uri.EscapeDataString(id)}");
            return response.IsSuccessStatusCode ? (true, null) : (false, $"HTTP {(int)response.StatusCode}");
        }
        catch (Exception ex) { return (false, ex.Message); }
    }

    public async Task<string> TestAsync(string id, string input)
    {
        try
        {
            var response = await http.PostAsJsonAsync(
                $"/workflows/{Uri.EscapeDataString(id)}/test", new { input });
            var doc = await response.Content.ReadFromJsonAsync<JsonElement>();
            return JsonSerializer.Serialize(doc, _pretty);
        }
        catch (Exception ex) { return $"Hata: {ex.Message}"; }
    }
}
