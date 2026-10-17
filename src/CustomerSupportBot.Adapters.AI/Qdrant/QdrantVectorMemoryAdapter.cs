// Adapters.AI/Qdrant/QdrantVectorMemoryAdapter.cs
// DRIVEN ADAPTER — IVectorMemoryPort → Qdrant implementasyonu.
// Her MemoryKind ayrı koleksiyona yazılır → SearchAsync filter'a ihtiyaç duymaz.

using System.Globalization;
using CustomerSupportBot.Application.Ports.Outbound.AI;
using CustomerSupportBot.Domain.Model.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace CustomerSupportBot.Adapters.AI.Qdrant;

/// <summary>
/// Qdrant vektör deposu adapter'ı.
/// SemanticMemoryService bu adapter aracılığıyla episodic memory'e erişir.
/// </summary>
public sealed class QdrantVectorMemoryAdapter : IVectorMemoryPort
{
    private readonly QdrantClient _client;
    private readonly ILogger<QdrantVectorMemoryAdapter> _logger;
    private readonly bool _allowDestructiveDimensionMigration;
    private readonly SemanticMemoryOptions.HybridOptions _hybrid;

    // Sözcüksel yan koleksiyon: aynı nokta kimlikleri ve etiketlerle, yalnızca sparse vektör tutar.
    // Qdrant mevcut bir koleksiyona sonradan sparse vektör eklemeye izin vermez; yan koleksiyon mevcut
    // koleksiyonlara dokunmadan eklenir.
    internal const string LexicalSuffix = "__lex";
    private const string SparseVectorName = "text";
    private const int BackfillBatchSize = 200;

    // Payload alan adları — search hit → MemoryDocument hidrasyonu için.
    private const string PayloadKind = "_kind";
    private const string PayloadText = "_text";
    private const string PayloadTitle = "_title";
    private const string PayloadSource = "_source";
    private const string PayloadSessionId = "_sessionId";
    private const string PayloadCreatedAt = "_createdAt";
    private const string PayloadDocId = "_docId"; // string id'yi koruruz

    public QdrantVectorMemoryAdapter(
        IOptions<SemanticMemoryOptions> options,
        ILogger<QdrantVectorMemoryAdapter> logger)
    {
        _logger = logger;
        var q = options.Value.VectorStore;
        _allowDestructiveDimensionMigration = q.AllowDestructiveDimensionMigration;
        _hybrid = options.Value.Retrieval.Hybrid;
        _client = string.IsNullOrWhiteSpace(q.ApiKey)
            ? new QdrantClient(q.Host, q.Port, q.UseHttps)
            : new QdrantClient(q.Host, q.Port, q.UseHttps, q.ApiKey);
    }

    /// <summary>
    /// Boyut uyuşmazlığında ne yapılacağına karar verir: <c>true</c> → koleksiyon silinip
    /// yeniden oluşturulur, <c>false</c> → dokunulmaz. İzin yoksa <b>fırlatır</b>.
    ///
    /// <para>
    /// Karar bilinçli olarak ayrı bir metotta: gerçek <c>QdrantClient</c> mock'lanamadığı için
    /// <see cref="EnsureCollectionAsync"/>'in tamamı birim testiyle sürülemez. Kararın kendisi
    /// — asıl riskli kısım — böylece test altına girer.
    /// </para>
    /// </summary>
    internal static bool RequiresRecreate(
        string collection, int existingDim, int expectedDim, bool allowDestructive)
    {
        if (existingDim <= 0 || existingDim == expectedDim) return false;

        if (!allowDestructive)
        {
            throw new InvalidOperationException(
                $"Qdrant koleksiyonu '{collection}' {existingDim} boyutlu, beklenen {expectedDim}. "
              + "Veri kaybı olmadan devam edilemez. Koleksiyonu yeniden oluşturmak KASITLIYSA "
              + "SemanticMemory:VectorStore:AllowDestructiveDimensionMigration=true verin; "
              + "aksi hâlde embedding boyutunu eski değerine döndürün.");
        }

        return true;
    }

    public async Task EnsureCollectionAsync(string collection, int dimension, CancellationToken ct = default)
    {
        await EnsureDenseCollectionAsync(collection, dimension, ct).ConfigureAwait(false);
        await EnsureLexicalCollectionAsync(collection, ct).ConfigureAwait(false);
    }

    private async Task EnsureDenseCollectionAsync(string collection, int dimension, CancellationToken ct)
    {
        var exists = await _client.CollectionExistsAsync(collection, ct).ConfigureAwait(false);
        if (exists)
        {
            // Boyut uyuşmazlığı: VARSAYILAN DAVRANIŞ SİLMEK DEĞİL, DURMAKTIR.
            //
            // Buradaki silme eskiden koşulsuzdu ve "dev-friendly" diye işaretlenmişti — ama
            // ortam ayrımı yoktu, dolayısıyla üretimde de çalışıyordu. Embedding modelini
            // değiştirmek (ör. -small → -large) knowledge base'i, dersleri ve episodic belleği
            // sessizce siliyordu; üstelik knowledge base için re-ingest de atlanıyordu
            // (KnowledgeBaseIngestionService kaynak hash'i değişmediği için "değişmemiş" sayar).
            // Sonuç: boş bilgi tabanı, geri getirilemeyen bellek.
            try
            {
                var info = await _client.GetCollectionInfoAsync(collection, ct).ConfigureAwait(false);
                var existingDim = (int)(info?.Config?.Params?.VectorsConfig?.Params?.Size ?? 0);
                if (RequiresRecreate(collection, existingDim, dimension, _allowDestructiveDimensionMigration))
                {
                    _logger.LogWarning(
                        "Qdrant collection {Collection} dim={Existing}, beklenen={Expected}. "
                      + "AllowDestructiveDimensionMigration=true olduğu için yeniden oluşturuluyor "
                      + "(mevcut veriler SİLİNİYOR).",
                        collection, existingDim, dimension);
                    await _client.DeleteCollectionAsync(collection, cancellationToken: ct).ConfigureAwait(false);
                    // Sözcüksel kopya eski belgeleri taşır; ana koleksiyonla birlikte sıfırlanır.
                    await TryDeleteLexicalCollectionAsync(collection, ct).ConfigureAwait(false);
                }
                else
                {
                    return;
                }
            }
            catch (InvalidOperationException)
            {
                // Boyut uyuşmazlığı kararı YUTULMAZ. Aşağıdaki genel catch, Qdrant'a
                // ulaşılamamasını tolere etmek içindir; fail-closed kararını da yutarsa
                // "veri kaybını engelledik" güvencesi kâğıt üzerinde kalır — uygulama
                // başlar ve yanlış boyutlu koleksiyonla çalışmaya devam ederdi.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Qdrant collection {Collection} bilgisi alınamadı; mevcut sayılıp atlandı.", collection);
                return;
            }
        }

        await _client.CreateCollectionAsync(
            collection,
            new VectorParams { Size = (ulong)dimension, Distance = Distance.Cosine },
            cancellationToken: ct).ConfigureAwait(false);

        _logger.LogInformation("Qdrant collection oluşturuldu: {Collection} (dim={Dim})",
            collection, dimension);
    }

    public async Task UpsertAsync(
        string collection,
        IReadOnlyList<(MemoryDocument Doc, float[] Vector)> items,
        CancellationToken ct = default)
    {
        if (items.Count == 0) return;

        var points = new List<PointStruct>(items.Count);
        foreach (var (doc, vec) in items)
        {
            var p = new PointStruct
            {
                Id = ToPointId(doc.Id),
                Vectors = vec
            };
            p.Payload[PayloadKind] = doc.Kind.ToString();
            p.Payload[PayloadText] = doc.Text ?? "";
            p.Payload[PayloadDocId] = doc.Id;
            p.Payload[PayloadCreatedAt] = doc.CreatedAt.ToString("O", CultureInfo.InvariantCulture);
            if (doc.Title != null) p.Payload[PayloadTitle] = doc.Title;
            if (doc.Source != null) p.Payload[PayloadSource] = doc.Source;
            if (doc.SessionId != null) p.Payload[PayloadSessionId] = doc.SessionId;
            foreach (var kv in doc.Tags)
                p.Payload[kv.Key] = kv.Value ?? "";

            points.Add(p);
        }

        try
        {
            await _client.UpsertAsync(collection, points, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ExceptionTranslator.Translate(ex, $"Qdrant upsert başarısız (collection={collection}).");
        }

        await UpsertLexicalAsync(collection, points, ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<MemorySearchHit>> SearchAsync(
        string collection,
        float[] query,
        int topK,
        float minScore,
        IReadOnlyDictionary<string, string>? tagFilter = null,
        CancellationToken ct = default)
    {
        var filter = BuildTagFilter(tagFilter);

        IReadOnlyList<ScoredPoint> hits;
        try
        {
            hits = await _client.QueryAsync(
                collection, query, filter: filter,
                limit: (ulong)topK,
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Qdrant search başarısız (collection={Collection}); boş liste dönüyorum", collection);
            return Array.Empty<MemorySearchHit>();
        }

        var output = new List<MemorySearchHit>(hits.Count);
        foreach (var h in hits)
        {
            if (h.Score < minScore) continue;
            output.Add(new MemorySearchHit { Document = HydrateDocument(h.Payload), Score = h.Score });
        }
        return output;
    }

    public async Task<IReadOnlyList<MemorySearchHit>> HybridSearchAsync(
        string collection,
        float[] query,
        string queryText,
        int topK,
        float minScore,
        IReadOnlyDictionary<string, string>? tagFilter = null,
        CancellationToken ct = default)
    {
        var filter = BuildTagFilter(tagFilter);
        var candidates = Math.Max(topK, _hybrid.Candidates);

        var denseTask = QueryDenseAsync(collection, query, filter, candidates, ct);
        var lexicalTask = QueryLexicalAsync(collection, queryText, filter, candidates, ct);
        await Task.WhenAll(denseTask, lexicalTask).ConfigureAwait(false);

        // minScore yalnızca dense sinyale uygulanır: sözcüksel skorlar farklı ölçekte (IDF ağırlıklı).
        var dense = denseTask.Result.Where(h => h.Score >= minScore).ToList();
        var lexical = lexicalTask.Result;

        var byId = new Dictionary<string, ScoredPoint>();
        foreach (var h in lexical) byId[DocIdOf(h)] = h;
        foreach (var h in dense) byId[DocIdOf(h)] = h;   // dense yük (payload) öncelikli

        var fused = ReciprocalRankFusion.Fuse(
            [dense.Select(DocIdOf).ToList(), lexical.Select(DocIdOf).ToList()], _hybrid.RrfK, topK);
        var max = ReciprocalRankFusion.MaxScore(signals: 2, _hybrid.RrfK);

        return fused
            .Select(f => new MemorySearchHit
            {
                Document = HydrateDocument(byId[f.Id].Payload),
                Score = (float)Math.Min(1d, f.Score / max)
            })
            .ToList();
    }

    private static string DocIdOf(ScoredPoint p) =>
        p.Payload.TryGetValue(PayloadDocId, out var id) && !string.IsNullOrEmpty(id.StringValue) ? id.StringValue : p.Id.Uuid;

    private async Task<IReadOnlyList<ScoredPoint>> QueryDenseAsync(
        string collection, float[] query, Filter? filter, int limit, CancellationToken ct)
    {
        try
        {
            return await _client.QueryAsync(collection, query, filter: filter, limit: (ulong)limit, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Qdrant dense arama başarısız (collection={Collection}); hibritte yalnızca sözcüksel sinyal kullanılır", collection);
            return Array.Empty<ScoredPoint>();
        }
    }

    private async Task<IReadOnlyList<ScoredPoint>> QueryLexicalAsync(
        string collection, string queryText, Filter? filter, int limit, CancellationToken ct)
    {
        var sparse = SparseTextEncoder.EncodeQuery(queryText);
        if (sparse.Indices.Length == 0) return Array.Empty<ScoredPoint>();

        try
        {
            return await _client.QueryAsync(
                LexicalName(collection),
                query: new Query { Nearest = new VectorInput { Sparse = ToSparse(sparse) } },
                usingVector: SparseVectorName,
                filter: filter,
                limit: (ulong)limit,
                cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Qdrant sözcüksel arama başarısız (collection={Collection}); hibritte yalnızca dense sinyal kullanılır", collection);
            return Array.Empty<ScoredPoint>();
        }
    }

    private static Filter? BuildTagFilter(IReadOnlyDictionary<string, string>? tagFilter)
    {
        if (tagFilter is not { Count: > 0 }) return null;
        var filter = new Filter();
        foreach (var kv in tagFilter)
            filter.Must.Add(new Condition { Field = new FieldCondition { Key = kv.Key, Match = new Match { Keyword = kv.Value } } });
        return filter;
    }

    // ─── sözcüksel yan koleksiyon ───

    internal static string LexicalName(string collection) => collection + LexicalSuffix;

    private static SparseVector ToSparse(SparseVec v)
    {
        var sparse = new SparseVector();
        sparse.Indices.AddRange(v.Indices);
        sparse.Values.AddRange(v.Values);
        return sparse;
    }

    private static PointStruct ToLexicalPoint(PointId id, IDictionary<string, Value> payload, SparseVec vector)
    {
        var point = new PointStruct { Id = id, Vectors = new Vectors { Vectors_ = new NamedVectors() } };
        point.Vectors.Vectors_.Vectors[SparseVectorName] = new Vector { Sparse = ToSparse(vector) };
        foreach (var kv in payload) point.Payload[kv.Key] = kv.Value;
        return point;
    }

    private static string LexicalSource(IDictionary<string, Value> payload) =>
        $"{(payload.TryGetValue(PayloadTitle, out var t) ? t.StringValue : "")} "
        + (payload.TryGetValue(PayloadText, out var x) ? x.StringValue : "");

    /// <summary>
    /// Yan koleksiyon yoksa kurar ve ana koleksiyondaki mevcut belgelerle doldurur (hibritten önce yazılmış
    /// veriler için tek seferlik migrasyon). Hibrit isteğe bağlı bir özellik olduğundan hata başlangıcı
    /// DURDURMAZ: yalnızca loglanır, arama dense'e düşer.
    /// </summary>
    private async Task EnsureLexicalCollectionAsync(string collection, CancellationToken ct)
    {
        var lexical = LexicalName(collection);
        try
        {
            if (await _client.CollectionExistsAsync(lexical, ct).ConfigureAwait(false)) return;

            var config = new SparseVectorConfig();
            config.Map[SparseVectorName] = new SparseVectorParams { Modifier = Modifier.Idf };
            await _client.CreateCollectionAsync(lexical, new VectorParamsMap(), sparseVectorsConfig: config, cancellationToken: ct)
                .ConfigureAwait(false);

            var copied = await BackfillLexicalAsync(collection, lexical, ct).ConfigureAwait(false);
            _logger.LogInformation("Sözcüksel indeks kuruldu: {Collection} ({Count} belge)", lexical, copied);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Sözcüksel indeks kurulamadı ({Collection}); hibrit arama dense'e düşecek", lexical);
        }
    }

    private async Task<int> BackfillLexicalAsync(string collection, string lexical, CancellationToken ct)
    {
        var copied = 0;
        PointId? offset = null;
        do
        {
            var page = await _client.ScrollAsync(collection, limit: BackfillBatchSize, offset: offset,
                payloadSelector: true, cancellationToken: ct).ConfigureAwait(false);

            var batch = new List<PointStruct>();
            foreach (var point in page.Result)
            {
                var vector = SparseTextEncoder.EncodeDocument(LexicalSource(point.Payload));
                if (vector.Indices.Length > 0) batch.Add(ToLexicalPoint(point.Id, point.Payload, vector));
            }
            if (batch.Count > 0)
            {
                await _client.UpsertAsync(lexical, batch, cancellationToken: ct).ConfigureAwait(false);
                copied += batch.Count;
            }
            offset = page.NextPageOffset;
        } while (offset is not null);
        return copied;
    }

    /// <summary>Ana koleksiyona yazılan noktaların sözcüksel kopyasını yazar. En iyi gayret: hata loglanır.</summary>
    private async Task UpsertLexicalAsync(string collection, IReadOnlyList<PointStruct> mainPoints, CancellationToken ct)
    {
        try
        {
            var lexicalPoints = new List<PointStruct>();
            foreach (var p in mainPoints)
            {
                var vector = SparseTextEncoder.EncodeDocument(LexicalSource(p.Payload));
                if (vector.Indices.Length > 0) lexicalPoints.Add(ToLexicalPoint(p.Id, p.Payload, vector));
            }
            if (lexicalPoints.Count > 0)
                await _client.UpsertAsync(LexicalName(collection), lexicalPoints, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Sözcüksel indeks yazılamadı (collection={Collection}); hibrit sonuçlar bu belgeler için eksik kalabilir", collection);
        }
    }

    /// <summary>
    /// Ana koleksiyondaki bir silmeyi yan koleksiyona yansıtır. Yan koleksiyon belge METNİNİ kopyaladığından
    /// silme (KVKK dahil) ona da uygulanmazsa silinen veri aranabilir kalırdı. Yan koleksiyon yoksa sorun değil.
    /// </summary>
    private async Task MirrorDeleteAsync(string collection, Func<string, Task> delete)
    {
        try
        {
            await delete(LexicalName(collection)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Sözcüksel indeksten silinemedi (collection={Collection})", collection);
        }
    }

    private async Task TryDeleteLexicalCollectionAsync(string collection, CancellationToken ct)
    {
        try
        {
            var lexical = LexicalName(collection);
            if (await _client.CollectionExistsAsync(lexical, ct).ConfigureAwait(false))
                await _client.DeleteCollectionAsync(lexical, cancellationToken: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Sözcüksel koleksiyon silinemedi ({Collection})", collection);
        }
    }

    public async Task DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        try
        {
            await _client.DeleteAsync(collection, ToPointGuid(id),
                cancellationToken: ct).ConfigureAwait(false);
            await MirrorDeleteAsync(collection, lex => _client.DeleteAsync(lex, ToPointGuid(id), cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ExceptionTranslator.Translate(ex, $"Qdrant delete başarısız (collection={collection}, id={id}).");
        }
    }

    public async Task DeleteWhereTagNotAsync(
        string collection, string tagKey, string tagValue, CancellationToken ct = default)
    {
        try
        {
            var filter = new Filter();
            filter.MustNot.Add(new Condition
            {
                Field = new FieldCondition { Key = tagKey, Match = new Match { Keyword = tagValue } }
            });

            await _client.DeleteAsync(collection, filter, cancellationToken: ct).ConfigureAwait(false);
            await MirrorDeleteAsync(collection, lex => _client.DeleteAsync(lex, filter, cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ExceptionTranslator.Translate(
                ex, $"Qdrant filtreli delete başarısız (collection={collection}, tag={tagKey}).");
        }
    }

    public async Task DeleteBySessionsAsync(
        string collection, IReadOnlyCollection<string> sessionIds, CancellationToken ct = default)
    {
        if (sessionIds.Count == 0) return;
        try
        {
            var any = new RepeatedStrings();
            any.Strings.AddRange(sessionIds);
            var filter = new Filter();
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition { Key = PayloadSessionId, Match = new Match { Keywords = any } }
            });

            await _client.DeleteAsync(collection, filter, cancellationToken: ct).ConfigureAwait(false);
            await MirrorDeleteAsync(collection, lex => _client.DeleteAsync(lex, filter, cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ExceptionTranslator.Translate(
                ex, $"Qdrant oturum silme başarısız (collection={collection}, sessions={sessionIds.Count}).");
        }
    }

    public async Task DeleteWhereTagAsync(
        string collection, string tagKey, string tagValue, CancellationToken ct = default)
    {
        try
        {
            var filter = new Filter();
            filter.Must.Add(new Condition
            {
                Field = new FieldCondition { Key = tagKey, Match = new Match { Keyword = tagValue } }
            });

            await _client.DeleteAsync(collection, filter, cancellationToken: ct).ConfigureAwait(false);
            await MirrorDeleteAsync(collection, lex => _client.DeleteAsync(lex, filter, cancellationToken: ct)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw ExceptionTranslator.Translate(
                ex, $"Qdrant etiketli silme başarısız (collection={collection}, tag={tagKey}).");
        }
    }

    public async Task<long> CountAsync(string collection, CancellationToken ct = default)
    {
        try
        {
            var info = await _client.GetCollectionInfoAsync(collection, ct).ConfigureAwait(false);
            return (long)(info.PointsCount);
        }
        catch
        {
            return 0;
        }
    }

    // ─── helpers ───

    private static PointId ToPointId(string id) => new() { Uuid = ToPointGuid(id).ToString() };

    /// <summary>
    /// Doküman id'sini Qdrant point GUID'ine çevirir. Upsert ve delete <b>aynı</b> eşlemeyi
    /// kullanmak zorunda; aksi halde silme yanlış noktayı hedefler.
    /// </summary>
    internal static Guid ToPointGuid(string id)
    {
        // Qdrant point id ya UUID ya da uint64. GUID kullanıyoruz; parse edemezsek deterministik hash → guid.
        if (Guid.TryParse(id, out var g)) return g;
        // Stable hash → guid v5-benzeri
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(id));
        var guidBytes = new byte[16];
        Array.Copy(bytes, guidBytes, 16);
        return new Guid(guidBytes);
    }

    private static MemoryDocument HydrateDocument(IDictionary<string, Value> payload)
    {
        var doc = new MemoryDocument
        {
            Id = payload.TryGetValue(PayloadDocId, out var idV) ? idV.StringValue : Guid.NewGuid().ToString(),
            Text = payload.TryGetValue(PayloadText, out var txt) ? txt.StringValue : "",
            Title = payload.TryGetValue(PayloadTitle, out var ttl) ? ttl.StringValue : null,
            Source = payload.TryGetValue(PayloadSource, out var src) ? src.StringValue : null,
            SessionId = payload.TryGetValue(PayloadSessionId, out var sid) ? sid.StringValue : null,
        };

        if (payload.TryGetValue(PayloadKind, out var kindV) &&
            Enum.TryParse<MemoryKind>(kindV.StringValue, ignoreCase: true, out var k))
            doc.Kind = k;

        if (payload.TryGetValue(PayloadCreatedAt, out var ca) &&
            DateTime.TryParse(ca.StringValue, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var dt))
            doc.CreatedAt = dt;

        // Diğer tüm key'leri Tags'a kopyala (rezerve isimler hariç)
        foreach (var kv in payload)
        {
            if (kv.Key.StartsWith('_')) continue;
            doc.Tags[kv.Key] = kv.Value.StringValue ?? "";
        }

        return doc;
    }
}
