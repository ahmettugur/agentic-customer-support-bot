using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CustomerSupportBot.Web.Models;

namespace CustomerSupportBot.Web.Services;

/// <summary>
/// /memory/articles uçlarının istemcisi.
///
/// Okuma yollarında hata yutulur (boş liste); yazma yollarında <b>yutulmaz</b> —
/// kullanıcı kaydın gerçekten gidip gitmediğini bilmek zorunda.
/// </summary>
public sealed class KnowledgeApiService(HttpClient http)
{
    public async Task<List<KnowledgeArticleDto>> ListAsync()
    {
        try
        {
            return await http.GetFromJsonAsync<List<KnowledgeArticleDto>>("/memory/articles/") ?? [];
        }
        catch
        {
            return [];
        }
    }

    public async Task<KnowledgeArticleSaveDto> CreateAsync(KnowledgeArticleInput input)
    {
        var response = await http.PostAsJsonAsync("/memory/articles/", input);
        return await ReadSaveResultAsync(response);
    }

    public async Task<KnowledgeArticleSaveDto> UpdateAsync(string id, KnowledgeArticleInput input)
    {
        var response = await http.PutAsJsonAsync($"/memory/articles/{Uri.EscapeDataString(id)}", input);
        return await ReadSaveResultAsync(response);
    }

    public async Task DeleteAsync(string id)
    {
        var response = await http.DeleteAsync($"/memory/articles/{Uri.EscapeDataString(id)}");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await DescribeErrorAsync(response));
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static async Task<KnowledgeArticleSaveDto> ReadSaveResultAsync(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await DescribeErrorAsync(response));

        return await response.Content.ReadFromJsonAsync<KnowledgeArticleSaveDto>()
            ?? throw new InvalidOperationException("Sunucu beklenmeyen bir yanıt döndü.");
    }

    /// <summary>Sunucunun hata kodunu okunabilir Türkçe mesaja çevirir.</summary>
    private static async Task<string> DescribeErrorAsync(HttpResponseMessage response)
    {
        var code = await ReadErrorCodeAsync(response);

        return code switch
        {
            "title_required" => "Başlık zorunlu.",
            "title_too_long" => "Başlık çok uzun (en fazla 256 karakter).",
            "content_required" => "İçerik zorunlu.",
            "category_too_long" => "Kategori çok uzun (en fazla 64 karakter).",
            "article_not_found" => "Makale bulunamadı; başka biri silmiş olabilir.",
            _ when response.StatusCode == HttpStatusCode.TooManyRequests
                => "İstek sınırı aşıldı, birazdan tekrar deneyin.",
            _ when response.StatusCode == HttpStatusCode.Unauthorized ||
                   response.StatusCode == HttpStatusCode.Forbidden
                => "Bu işlem için yetkiniz yok.",
            _ => $"İşlem başarısız ({(int)response.StatusCode})."
        };
    }

    private static async Task<string?> ReadErrorCodeAsync(HttpResponseMessage response)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("error", out var e) ? e.GetString() : null;
        }
        catch
        {
            return null;
        }
    }
}
