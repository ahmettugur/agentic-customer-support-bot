// Adapters.Persistence/FileSystem/FileSystemPromptRepository.cs
// Sistemdeki tüm LLM prompt'ları Prompts/ dizinindeki .md dosyalarında tutulur.
// Bu servis uygulama başlangıcında tüm .md dosyalarını belleğe yükler ve
// Get/Render metodlarıyla erişim sağlar.
//
// Placeholder sentaksı: {{ANAHTAR}} — Render(key, vars) çağrısında değiştirilir.
// Bulunmayan placeholder'lar boş string ile değiştirilir.

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using CustomerSupportBot.Application.Ports.Driven;
using Microsoft.Extensions.Logging;

namespace CustomerSupportBot.Adapters.Persistence.FileSystem;

/// <summary>
/// Markdown dosyalarından prompt yükler, {{PLACEHOLDER}} ikame eden servis.
/// Tüm prompt'lar uygulama başlangıcında belleğe yüklenir — her istekte disk I/O yok.
/// </summary>
public class FileSystemPromptRepository : IPromptRepository
{
    private static readonly Regex PlaceholderPattern =
        new(@"\{\{\s*([A-Za-z0-9_]+)\s*\}\}", RegexOptions.Compiled);

    private readonly ConcurrentDictionary<string, string> _prompts = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<FileSystemPromptRepository> _logger;
    private readonly string _rootDirectory;

    public FileSystemPromptRepository(ILogger<FileSystemPromptRepository> logger)
    {
        _logger = logger;
        _rootDirectory = ResolveRootDirectory();
        LoadAll();
    }

    /// <summary>
    /// Placeholder içermeyen saf prompt'u döner. Anahtar yoksa <see cref="KeyNotFoundException"/>.
    /// </summary>
    public string Get(string key)
    {
        if (_prompts.TryGetValue(key, out var value))
        {
            return value;
        }

        throw new KeyNotFoundException(
            $"Prompt '{key}' bulunamadı. Kayıtlı anahtarlar: {string.Join(", ", _prompts.Keys)}");
    }

    /// <summary>
    /// Prompt'u yükler ve {{ANAHTAR}} formatındaki placeholder'ları değiştirir.
    /// Bulunmayan placeholder'lar boş string ile değiştirilir.
    /// </summary>
    public string Render(string key, IDictionary<string, string?>? variables = null)
    {
        var template = Get(key);
        if (variables == null || variables.Count == 0)
        {
            // Yine de placeholder'ları temizle — değişken sağlanmayan template döndürmek yanıltıcı olur.
            return PlaceholderPattern.Replace(template, _ => "");
        }

        return PlaceholderPattern.Replace(template, m =>
        {
            var name = m.Groups[1].Value;
            return variables.TryGetValue(name, out var val) ? val ?? "" : "";
        });
    }

    /// <summary>
    /// Kayıtlı tüm prompt anahtarlarını döner. Debug ve introspection amaçlı.
    /// </summary>
    public IReadOnlyCollection<string> Keys => _prompts.Keys.ToList();

    // ─── YÜKLEME ───

    private void LoadAll()
    {
        if (!Directory.Exists(_rootDirectory))
        {
            _logger.LogError("Prompts dizini bulunamadı: {Path}", _rootDirectory);
            throw new DirectoryNotFoundException(
                $"Prompts dizini bulunamadı: {_rootDirectory}. " +
                "CustomerSupportBot.Api.csproj'da <None Update=\"Prompts\\**\\*.md\"> kuralı var mı?");
        }

        var mdFiles = Directory.EnumerateFiles(_rootDirectory, "*.md", SearchOption.AllDirectories).ToList();

        foreach (var file in mdFiles)
        {
            // README/NOTES gibi insanlara yönelik dokümantasyon dosyalarını atla —
            // Prompt olarak yüklenmesinler.
            var fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName.Equals("README", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("NOTES", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var key = BuildKey(file);
            if (string.IsNullOrWhiteSpace(key)) continue;

            var content = File.ReadAllText(file);
            _prompts[key] = content;
        }

        _logger.LogInformation(
            "FileSystemPromptRepository: {Count} prompt yüklendi — {Keys}",
            _prompts.Count,
            string.Join(", ", _prompts.Keys));

        if (_prompts.IsEmpty)
        {
            throw new InvalidOperationException(
                $"Prompts dizininde hiç .md dosyası bulunamadı: {_rootDirectory}");
        }
    }

    /// <summary>
    /// Dosya yolundan anahtar üretir. Örn:
    ///   ".../Prompts/agents/planning-agent.md" → "agents/planning-agent"
    /// Path separator'ü platform bağımsız '/' ile normalize edilir.
    /// </summary>
    private string BuildKey(string absoluteFilePath)
    {
        var relative = Path.GetRelativePath(_rootDirectory, absoluteFilePath);
        var withoutExt = Path.ChangeExtension(relative, null);
        return withoutExt.Replace(Path.DirectorySeparatorChar, '/')
                         .Replace(Path.AltDirectorySeparatorChar, '/');
    }

    /// <summary>
    /// Prompts dizinini bulur. Önce çalışma dizini, sonra BaseDirectory (publish).
    /// </summary>
    private static string ResolveRootDirectory()
    {
        // 1) Uygulama base dizini (publish + normal build çıktısı)
        var baseDir = Path.Combine(AppContext.BaseDirectory, "Prompts");
        if (Directory.Exists(baseDir)) return baseDir;

        // 2) Current working directory (dotnet run, test)
        var cwd = Path.Combine(Directory.GetCurrentDirectory(), "Prompts");
        if (Directory.Exists(cwd)) return cwd;

        // Bulunamadıysa baseDir'i döndür ve LoadAll içinde daha açıklayıcı hata fırlat.
        return baseDir;
    }
}
