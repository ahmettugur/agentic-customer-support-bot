// Terminal stil kodlari ve stil uygulama karari.
//
// Renk KULLANILMAZ, yalnizca yogunluk/vurgu (kalin, sonuk, egik, alti cizili, ters).
// Sebep: terminal temasi koyu da acik da olabilir; sabit bir renk secmek yaziyi bazi
// temalarda okunamaz hale getirir. Yogunluk kodlari her temada calisir.

namespace CustomerSupportBot.A2AClient.Sample.Rendering;

/// <summary>ANSI kacis dizileri ve "stil uygulanacak mi" karari.</summary>
public sealed class AnsiStyle(bool enabled)
{
    private const string Esc = "[";

    public const string Reset = Esc + "0m";
    public const string Bold = Esc + "1m";
    public const string Dim = Esc + "2m";
    public const string Italic = Esc + "3m";
    public const string Underline = Esc + "4m";
    public const string Reverse = Esc + "7m";
    public const string Strike = Esc + "9m";

    public bool Enabled { get; } = enabled;

    /// <summary>
    /// Otomatik karar: cikti bir dosyaya/boruya yonlendirilmisse kacis dizileri metne
    /// karisip dosyayi kirletir, o yuzden kapatilir. <c>NO_COLOR</c> yaygin bir sozlesmedir.
    ///
    /// <para>
    /// Bu karar AYRI BIR TIPTE tutuluyor ki test edilebilsin: ortama bagli statik bir alan
    /// olsaydi, stilli ciktiyi dogrulamak icin gercek bir terminal gerekirdi — ve otomatik
    /// testte oyle bir terminal yoktur.
    /// </para>
    /// </summary>
    public static AnsiStyle Auto() => new(
        !System.Console.IsOutputRedirected &&
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")));

    /// <summary>Metni stille sarar; stil kapaliysa metni oldugu gibi dondurur.</summary>
    public string Wrap(string text, params string[] styles) =>
        !Enabled || styles.Length == 0 || text.Length == 0
            ? text
            : string.Concat(styles) + text + Reset;
}
