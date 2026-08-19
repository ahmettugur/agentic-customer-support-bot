// Ajan yanitlarindaki Markdown'i terminalde okunabilir hale getirir.
//
// Neden gerekli: uzak ajanlar duz metin degil MARKDOWN uretiyor. Ham basildiginda kullanici
// cevabi degil "**1057**" gibi isaretleri okuyor.
//
// Neden Markdig: ilk surum regex tabanliydi ve yalnizca kalin/madde imi gibi bir alt kumeyi
// kapsiyordu. Tam kapsam istendiginde regex yaklasimi yetersiz kalir — tablolar, IC ICE
// listeler, kod bloklari ve baglantilar dogru ayristirilamaz; ayrica kod blogu ICINDEKI
// yildizlar bicimlendirilmemeli, bunu duz regex ayirt edemez. Markdig gercek bir ayristirici
// oldugu icin bu ayrimlari dogru yapar; biz yalnizca AST'yi gezip terminale ceviriyoruz.

using Markdig;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using System.Text;

namespace CustomerSupportBot.A2AClient.Sample.Rendering;

/// <summary>
/// Markdown'i ANSI stilli terminal metnine cevirir.
///
/// <para>
/// Kapsam: basliklar, kalin/egik/ustu cizili, satir ici kod, kod bloklari, blok alintilar,
/// sirali/sirasiz ve IC ICE listeler, gorev listeleri, tablolar, baglantilar, gorseller,
/// yatay ayiriclar, zorunlu satir sonlari, HTML parcalari.
/// </para>
/// </summary>
public static class MarkdownConsole
{
    // UseAdvancedExtensions: tablolar, ustu cizili (~~), gorev listeleri ([x]) ve otomatik
    // baglantilar bu uzantilarla gelir. Olmadan Markdig bunlari duz metin sayardi.
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

    /// <summary>Markdown metni terminale bicimlendirilmis olarak yazar.</summary>
    public static void Write(string markdown, string indent = "  ")
    {
        foreach (var line in Render(markdown, AnsiStyle.Auto(), indent))
            System.Console.WriteLine(line);
    }

    /// <summary>
    /// Bicimlendirilmis satirlari uretir. Konsola yazmaktan AYRI durur ve stil karari
    /// disaridan verilir — ikisi de test edilebilirlik icin: gercek bir terminal olmadan
    /// stilli ciktiyi dogrulamak baska turlu mumkun olmaz.
    /// </summary>
    public static IReadOnlyList<string> Render(string markdown, AnsiStyle style, string indent = "  ")
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(markdown)) return lines;

        var document = Markdown.Parse(markdown, Pipeline);
        var writer = new BlockWriter(style, indent, lines);
        writer.WriteBlocks(document, "");
        writer.TrimTrailingBlank();
        return lines;
    }

    /// <summary>Blok duzeyindeki ogeleri satirlara ceviren gezgin.</summary>
    private sealed class BlockWriter(AnsiStyle style, string baseIndent, List<string> output)
    {
        private readonly InlineWriter _inline = new(style);

        /// <summary>Siki liste icindeyken bos satir eklenmesini durdurur.</summary>
        private bool _suppressBlank;

        public void WriteBlocks(ContainerBlock container, string prefix)
        {
            foreach (var block in container)
                WriteBlock(block, prefix);
        }

        private void WriteBlock(Block block, string prefix)
        {
            switch (block)
            {
                case HeadingBlock heading:
                    // h1/h2 alti cizili + kalin, digerleri kalin: seviye farki gorunur kalsin.
                    var headStyles = heading.Level <= 2
                        ? new[] { AnsiStyle.Bold, AnsiStyle.Underline }
                        : [AnsiStyle.Bold];
                    Emit(prefix + style.Wrap(_inline.Render(heading.Inline), headStyles));
                    Blank();
                    break;

                case ParagraphBlock paragraph:
                    foreach (var line in _inline.Render(paragraph.Inline).Split('\n'))
                        Emit(prefix + line);
                    Blank();
                    break;

                case ThematicBreakBlock:
                    Emit(prefix + style.Wrap(new string('─', 48), AnsiStyle.Dim));
                    Blank();
                    break;

                case QuoteBlock quote:
                    // Alinti cizgisi her satirin BASINA girer; ic ice alintilar dogal olarak
                    // "│ │ " haline gelir.
                    WriteBlocks(quote, prefix + style.Wrap("│ ", AnsiStyle.Dim));
                    break;

                case ListBlock list:
                    WriteList(list, prefix);
                    break;

                case CodeBlock code:
                    WriteCode(code, prefix);
                    break;

                case Table table:
                    WriteTable(table, prefix);
                    break;

                case HtmlBlock html:
                    // HTML terminalde anlamsizdir ama ATILMAZ: icerigi ham haliyle gostermek,
                    // sessizce kaybetmekten iyidir.
                    foreach (var line in RawLines(html))
                        Emit(prefix + style.Wrap(line, AnsiStyle.Dim));
                    Blank();
                    break;

                case ContainerBlock other:
                    WriteBlocks(other, prefix);
                    break;
            }
        }

        private void WriteList(ListBlock list, string prefix)
        {
            var number = list.IsOrdered && int.TryParse(list.OrderedStart, out var start) ? start : 1;

            // Siki (tight) listelerde ogeler arasina bos satir GIRMEZ — Markdown'da
            // IsLoose=false tam olarak bunu ifade eder. Ayrimi yok saysaydik kisa bir
            // madde listesi ekranda gereksiz yere iki katina cikardi.
            var previousSuppress = _suppressBlank;
            _suppressBlank = !list.IsLoose;

            foreach (var item in list.OfType<ListItemBlock>())
            {
                var marker = list.IsOrdered
                    ? style.Wrap($"{number++}.", AnsiStyle.Dim)
                    : style.Wrap("•", AnsiStyle.Dim);

                // Gorev listesi ([x] / [ ]) ise isaret yerine kutu gosterilir.
                var task = item.Descendants<TaskList>().FirstOrDefault();
                if (task is not null)
                    marker = task.Checked ? style.Wrap("✓", AnsiStyle.Bold) : "☐";

                var markerWidth = VisibleLength(marker);
                var continuation = prefix + new string(' ', markerWidth + 1);

                var before = output.Count;
                // Devam satirlari isaret genisligi kadar icerilir ki metin hizali kalsin.
                WriteBlocks(item, continuation);

                // Ilk satirdaki bosluk yerine isareti koy. Kesme noktasi baseIndent'i DE
                // icermek zorunda: Emit her satirin basina baseIndent ekliyor, onu saymazsak
                // isaret sola kayar ve metnin bir kismi kirpilir (olculdu, ilk surumde oyle oldu).
                if (output.Count > before)
                {
                    var cut = baseIndent.Length + continuation.Length;
                    var first = output[before];
                    output[before] = baseIndent + prefix + marker + " " + first[Math.Min(cut, first.Length)..];
                }
            }

            _suppressBlank = previousSuppress;
            Blank();
        }

        private void WriteCode(CodeBlock code, string prefix)
        {
            var language = (code as FencedCodeBlock)?.Info;
            if (!string.IsNullOrWhiteSpace(language))
                Emit(prefix + style.Wrap($"┌ {language}", AnsiStyle.Dim));

            foreach (var line in RawLines(code))
                Emit(prefix + style.Wrap("│ ", AnsiStyle.Dim) + style.Wrap(line, AnsiStyle.Dim));

            Blank();
        }

        private void WriteTable(Table table, string prefix)
        {
            // Hucreler once metne cevrilir; genislik hesabi GORUNUR uzunluga gore yapilir,
            // aksi halde ANSI kacis dizileri sutunlari kaydirirdi.
            var rows = new List<List<string>>();
            foreach (var row in table.OfType<TableRow>())
            {
                var cells = new List<string>();
                foreach (var cell in row.OfType<TableCell>())
                {
                    var sb = new StringBuilder();
                    foreach (var block in cell.OfType<ParagraphBlock>())
                        sb.Append(_inline.Render(block.Inline).Replace('\n', ' '));
                    cells.Add(sb.ToString().Trim());
                }
                rows.Add(cells);
            }

            if (rows.Count == 0) return;

            var columnCount = rows.Max(r => r.Count);
            var widths = new int[columnCount];
            foreach (var row in rows)
                for (var i = 0; i < row.Count; i++)
                    widths[i] = Math.Max(widths[i], VisibleLength(row[i]));

            for (var r = 0; r < rows.Count; r++)
            {
                var isHeader = r == 0 && table.OfType<TableRow>().FirstOrDefault()?.IsHeader == true;
                var cells = new List<string>();
                for (var c = 0; c < columnCount; c++)
                {
                    var text = c < rows[r].Count ? rows[r][c] : "";
                    var padded = text + new string(' ', widths[c] - VisibleLength(text));
                    cells.Add(isHeader ? style.Wrap(padded, AnsiStyle.Bold) : padded);
                }
                Emit(prefix + string.Join(style.Wrap(" │ ", AnsiStyle.Dim), cells).TrimEnd());

                if (isHeader)
                    Emit(prefix + style.Wrap(
                        string.Join("─┼─", widths.Select(w => new string('─', w))), AnsiStyle.Dim));
            }

            Blank();
        }

        private static IEnumerable<string> RawLines(LeafBlock block)
        {
            var lines = block.Lines;
            for (var i = 0; i < lines.Count; i++)
                yield return lines.Lines[i].Slice.ToString();
        }

        private void Emit(string line) => output.Add(baseIndent + line);

        private void Blank()
        {
            if (_suppressBlank) return;
            if (output.Count > 0 && output[^1].Length > 0) output.Add("");
        }

        /// <summary>Son bos satiri atar — cevabin altinda gereksiz bosluk kalmasin.</summary>
        public void TrimTrailingBlank()
        {
            while (output.Count > 0 && output[^1].Trim().Length == 0)
                output.RemoveAt(output.Count - 1);
        }

        /// <summary>ANSI kacis dizilerini saymadan gorunur uzunluk.</summary>
        private static int VisibleLength(string s)
        {
            var length = 0;
            for (var i = 0; i < s.Length; i++)
            {
                if (s[i] == '')
                {
                    while (i < s.Length && s[i] != 'm') i++;
                    continue;
                }
                length++;
            }
            return length;
        }
    }

    /// <summary>Satir ici ogeleri (kalin, egik, kod, baglanti...) metne ceviren gezgin.</summary>
    private sealed class InlineWriter(AnsiStyle style)
    {
        public string Render(ContainerInline? container)
        {
            if (container is null) return "";
            var sb = new StringBuilder();
            foreach (var inline in container)
                Append(sb, inline);
            return sb.ToString();
        }

        private void Append(StringBuilder sb, Inline inline)
        {
            switch (inline)
            {
                case LiteralInline literal:
                    sb.Append(literal.Content.ToString());
                    break;

                case EmphasisInline emphasis:
                    var inner = Render(emphasis);
                    sb.Append(style.Wrap(inner, EmphasisStyle(emphasis)));
                    break;

                case CodeInline code:
                    // Ters video: kod parcasi cevresindeki metinden net ayrilir ve renk
                    // gerektirmez, yani her temada okunur kalir.
                    sb.Append(style.Wrap(code.Content, AnsiStyle.Reverse));
                    break;

                case LinkInline link:
                    var label = Render(link);
                    if (link.IsImage)
                    {
                        sb.Append(style.Wrap($"[görsel: {(label.Length > 0 ? label : "adsız")}]", AnsiStyle.Dim));
                        if (!string.IsNullOrEmpty(link.Url)) sb.Append(style.Wrap($" {link.Url}", AnsiStyle.Dim));
                    }
                    else
                    {
                        // Terminalde tiklanabilir baglanti yoktur; adres ATILMAZ, cunku
                        // "buraya bakin" deyip adresi gizlemek bilgiyi yok etmek olurdu.
                        sb.Append(style.Wrap(label, AnsiStyle.Underline));
                        if (!string.IsNullOrEmpty(link.Url) && link.Url != label)
                            sb.Append(style.Wrap($" ({link.Url})", AnsiStyle.Dim));
                    }
                    break;

                case LineBreakInline lineBreak:
                    // Markdown'da satir sonundaki iki bosluk ZORUNLU satir sonudur; yumusak
                    // satir sonu ise akan metindir ve bosluga cevrilir.
                    sb.Append(lineBreak.IsHard ? '\n' : ' ');
                    break;

                case TaskList task:
                    // Isaret liste yazicisinda gosteriliyor; burada tekrar edilmez.
                    _ = task;
                    break;

                case AutolinkInline auto:
                    sb.Append(style.Wrap(auto.Url, AnsiStyle.Underline));
                    break;

                case HtmlInline html:
                    sb.Append(style.Wrap(html.Tag, AnsiStyle.Dim));
                    break;

                case ContainerInline container:
                    sb.Append(Render(container));
                    break;

                default:
                    // Tanimadigimiz bir oge: ham hali basilir, sessizce KAYBEDILMEZ.
                    sb.Append(inline.ToString());
                    break;
            }
        }

        private static string[] EmphasisStyle(EmphasisInline emphasis) => emphasis.DelimiterChar switch
        {
            '~' => [AnsiStyle.Strike],
            _ => emphasis.DelimiterCount >= 2 ? [AnsiStyle.Bold] : [AnsiStyle.Italic]
        };
    }
}
