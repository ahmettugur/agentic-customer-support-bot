// Tests/AppSettingsJson.cs
// appsettings dosyalarını uygulamanın okuduğu gibi okur: .NET JSON yapılandırması yorum satırlarını kabul eder, JsonDocument varsayılan olarak etmez.

using System.Text.Json;

namespace CustomerSupportBot.Api.IntegrationTests;

internal static class AppSettingsJson
{
    public static JsonDocument Parse(string json) => JsonDocument.Parse(json, new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    });
}
