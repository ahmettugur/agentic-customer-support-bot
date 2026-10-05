// Postgres/PostgresConversationSearchStore.cs
// Konuşma araması — tek SQL sorgusu: oturumlar filtrelenir ve sayfalanır, sayfadaki her oturumun mesaj
// sayısı, alıntı mesajı, kapanış nedenleri ve etiketleri alt sorgularla gelir.

using CustomerSupportBot.Adapters.Persistence.EfCore;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportBot.Adapters.Persistence.Postgres;

/// <summary>
/// <para>
/// <b>Türkçe katlama:</b> mesaj metni <c>lower(translate(text, 'İIıÇĞÖŞÜ', 'iiiçğöşü'))</c> ile, arama metni
/// C#'ta <c>TurkishText.Fold</c> ile aynı biçime getirilir. <c>lower()</c> veritabanının yerel ayarına bağlıdır
/// ('C' yerelinde yalnızca ASCII'yi çevirir); Türkçe büyük harfler bu yüzden <c>translate</c> ile açıkça çevrilir.
/// </para>
/// <para>
/// <b>Bilinen sınır:</b> <c>LIKE '%…%'</c> dizinsizdir. Saklama süresi tabloyu sınırlar; çok büyük hacimde
/// <c>pg_trgm</c> GIN dizini eklenebilir.
/// </para>
/// </summary>
public sealed class PostgresConversationSearchStore(IDbContextFactory<CustomerSupportDbContext> dbFactory) : IConversationSearchStore
{
    public async Task<IReadOnlyList<ConversationSearchRow>> SearchAsync(ConversationSearchCriteria c, CancellationToken ct = default)
    {
        var pattern = c.FoldedText is null ? null : "%" + EscapeLike(c.FoldedText) + "%";
        DateTime? from = c.FromUtc is { } f ? DateTime.SpecifyKind(f, DateTimeKind.Utc) : null;
        DateTime? to = c.ToUtc is { } t ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : null;

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.Database.SqlQuery<Row>($"""
            WITH page AS (
                SELECT s.session_id, s.created_at, s.last_activity, s.state->>'AuthenticatedCustomerId' AS customer_id
                FROM chat.sessions s
                WHERE ({c.CustomerId}::text IS NULL OR s.state->>'AuthenticatedCustomerId' = {c.CustomerId}::text)
                  AND ({from}::timestamptz IS NULL OR s.last_activity >= {from}::timestamptz)
                  AND ({to}::timestamptz IS NULL OR s.last_activity < {to}::timestamptz)
                  AND ({c.ReasonCode}::text IS NULL OR EXISTS (
                        SELECT 1 FROM chat.conversation_dispositions d
                        WHERE d.session_id = s.session_id AND d.reason_code = {c.ReasonCode}::text))
                  AND ({c.Tag}::text IS NULL OR EXISTS (
                        SELECT 1 FROM chat.conversation_dispositions d
                        WHERE d.session_id = s.session_id AND {c.Tag}::text = ANY (d.tags)))
                  AND ({pattern}::text IS NULL OR EXISTS (
                        SELECT 1 FROM chat.messages m
                        WHERE m.session_id = s.session_id
                          AND lower(translate(m.text, 'İIıÇĞÖŞÜ', 'iiiçğöşü')) LIKE {pattern}::text ESCAPE '\'))
                ORDER BY s.last_activity DESC, s.session_id
                LIMIT {c.Take} OFFSET {c.Offset}
            )
            SELECT
                p.session_id AS "SessionId",
                p.customer_id AS "CustomerId",
                p.created_at AS "CreatedAt",
                p.last_activity AS "LastActivity",
                (SELECT count(*) FROM chat.messages m WHERE m.session_id = p.session_id)::int AS "MessageCount",
                (SELECT m.text FROM chat.messages m
                   WHERE m.session_id = p.session_id
                     AND ({pattern}::text IS NULL OR lower(translate(m.text, 'İIıÇĞÖŞÜ', 'iiiçğöşü')) LIKE {pattern}::text ESCAPE '\')
                   ORDER BY CASE WHEN {pattern}::text IS NULL AND m.role <> 'user' THEN 1 ELSE 0 END, m.id
                   LIMIT 1) AS "MatchedText",
                ARRAY(SELECT DISTINCT d.reason_code FROM chat.conversation_dispositions d
                        WHERE d.session_id = p.session_id AND d.reason_code <> '' ORDER BY 1) AS "Reasons",
                ARRAY(SELECT DISTINCT tag FROM chat.conversation_dispositions d, unnest(d.tags) AS tag
                        WHERE d.session_id = p.session_id ORDER BY 1) AS "Tags"
            FROM page p
            ORDER BY p.last_activity DESC, p.session_id
            """).ToListAsync(ct);

        return rows.Select(r => new ConversationSearchRow(
            r.SessionId, r.CustomerId, r.CreatedAt, r.LastActivity, r.MessageCount, r.MatchedText, r.Reasons, r.Tags)).ToList();
    }

    /// <summary><c>LIKE</c> joker karakterleri arama metninde düz karakterdir.</summary>
    private static string EscapeLike(string s) => s.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    private sealed record Row(
        string SessionId, string? CustomerId, DateTime CreatedAt, DateTime LastActivity, int MessageCount,
        string? MatchedText, string[] Reasons, string[] Tags);
}
