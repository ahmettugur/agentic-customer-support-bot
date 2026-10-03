# Deployment — Kurulum ve Dağıtım

Bu doküman Docker Compose altyapısını, servis haritasını ve production hazırlık adımlarını anlatır.

---

## 1. Docker Compose Servis Haritası

```yaml
# docker-compose.yml — 8 servis
services:
  postgres:        # Ana veritabanı
  redis:           # Opsiyonel cache
  qdrant:          # Vektör veritabanı (semantic memory)
  elasticsearch:   # Jaeger trace storage
  kibana:          # Elasticsearch görselleştirme
  otel-collector:  # OpenTelemetry trace routing
  jaeger:          # Distributed tracing UI
```

### Port Haritası

| Servis | Host Port | Container Port | Açıklama |
|--------|-----------|----------------|----------|
| **PostgreSQL** | 5433 | 5432 | Ana veritabanı |
| **Redis** | 6380 | 6379 | Cache (opsiyonel) |
| **Qdrant HTTP** | 7333 | 6333 | Vektör DB HTTP API |
| **Qdrant gRPC** | 7334 | 6334 | Vektör DB gRPC (uygulama bunu kullanır) |
| **Elasticsearch** | 9200 | 9200 | Jaeger backend |
| **Kibana** | 5601 | 5601 | ES görselleştirme UI |
| **Jaeger OTLP gRPC** | 4317 | 4317 | OTLP receiver (uygulama buraya yazar) |
| **Jaeger OTLP HTTP** | 4318 | 4318 | OTLP receiver |
| **Jaeger UI** | 16686 | 16686 | Tracing UI |
| **OTel Collector gRPC** | 4327 | 4317 | OTLP receiver (opsiyonel, `otel` profili) |
| **OTel Collector HTTP** | 4328 | 4318 | OTLP receiver (opsiyonel, `otel` profili) |

> **Tüm host portları yalnızca `127.0.0.1`'e bağlıdır** (ör. `"127.0.0.1:5433:5432"`). Redis,
> Elasticsearch/Kibana ve Qdrant bu yığında kimlik doğrulamasız çalışır; `0.0.0.0`'a açık olmaları
> aynı ağdaki herkese (kafe Wi-Fi'ı dahil) erişim veriyordu. Uygulama host'ta çalıştığı için
> `localhost` üzerinden erişim değişmez.

> **Bu compose dosyası yerel geliştirme içindir, üretim için değil.** Paylaşımlı/uzak bir ortamda
> en az: Redis `requirepass` (+ bağlantı dizesinde parola), Qdrant `QDRANT__SERVICE__API_KEY`,
> Elasticsearch/Kibana `xpack.security.enabled=true` açılmalıdır.

### Gizli değerler — `deploy/.env`

Postgres parolası artık compose dosyasında değil, git'e girmeyen `deploy/.env` dosyasındadır
(`.gitignore`'da `.env`; şablon `deploy/.env.example`):

```bash
cp deploy/.env.example deploy/.env   # sonra POSTGRES_PASSWORD'ü doldurun
docker compose -f deploy/docker-compose.yml up -d
```

`POSTGRES_PASSWORD` tanımlı değilse compose açık bir hata mesajıyla durur
(`${POSTGRES_PASSWORD:?...}`). Değer, API'nin `ConnectionStrings:PostgreSQL` parolasıyla
(user-secrets / ortam değişkeni) aynı olmalıdır.

> ⚠️ Daha önce parola `docker-compose.yml` içinde açık metin olarak commit edilmişti — git
> geçmişinde duruyor. Dosyadan kaldırmak geçmişten silmez; o parola **değiştirilmelidir**.

### İmaj sürümleri sabit

`postgres:18`, `redis:8.2`, `qdrant/qdrant:v1.15.5`, `otel/opentelemetry-collector-contrib:0.138.0`
(Elasticsearch/Kibana `9.2.0`, Jaeger `2.17.0` zaten sabitti). Eskiden `latest`/etiketsiz imajlar
bir sonraki `pull`'da ana sürüm atlatabiliyordu — Postgres'te bu, veri dizininin yeni sürümle
açılamaması demektir. Postgres yalnızca ana sürüme (18) sabitlenir; küçük güncellemeler veri
uyumluluğunu bozmaz.

### OTel Collector (Opsiyonel)

Jaeger v2 doğrudan OTLP alabildiği için OTel Collector artık opsiyoneldir. `docker compose --profile otel up` ile ayrıca açılabilir. Port çakışması oluşmaması için OTel Collector host portları 4327/4328 olarak ayarlanmıştır.

`deploy/otel-collector-config.yaml`:

- **`debug` exporter pipeline'a bağlı değildir.** `verbosity: detailed` ile her span'in tüm
  niteliklerini (kullanıcı sorguları, tool parametreleri) collector stdout'una döküyordu; log bu
  verilerin saklanması için tasarlanmış bir yüzey değildir. Hata ayıklarken geçici olarak
  `traces.exporters` listesine eklenebilir (tanım `verbosity: basic` ile duruyor).
- **`resource` işlemcisi, `insert` ile:** eskiden `attributes` işlemcisi `service.name`'i
  `upsert` ediyordu — o işlemci **span** niteliklerine yazar, resource'a değil; her span'e
  uygulamanın gerçek adıyla (`CustomerSupportBot`) çelişen `AI.Api` değerini basıyordu. `insert`
  mevcut değeri korur, yalnızca adını bildirmeyen bir kaynak için varsayılan sağlar.
- İşlemci sırası: `memory_limiter` (ilk) → `resource` → `batch` (son).
- Yapılandırma `otelcol-contrib validate` ile doğrulanabilir:
  `docker run --rm -v "$PWD/deploy/otel-collector-config.yaml:/c.yaml:ro" otel/opentelemetry-collector-contrib:0.138.0 validate --config=/c.yaml`

---

## 2. Hızlı Başlangıç

### Minimum Kurulum (PostgreSQL + Qdrant)

```powershell
docker compose up -d postgres qdrant
cd CustomerSupportBot
dotnet user-secrets set "AI:OpenAI:ApiKey" "sk-..."
dotnet run
# → http://localhost:5021
```

### Tam Stack (Observability dahil)

```powershell
docker compose up -d postgres qdrant redis jaeger elasticsearch kibana
cd CustomerSupportBot
dotnet run
# → http://localhost:5021   (uygulama)
# → http://localhost:16686  (Jaeger UI)
# → http://localhost:5601   (Kibana)
```

---

## 3. Servis Detayları

### PostgreSQL

```yaml
postgres:
  image: postgres:18
  ports: ["127.0.0.1:5433:5432"]
  environment:
    POSTGRES_USER: postgres
    POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?...}   # deploy/.env
    PGDATA: /var/lib/postgresql/data
  volumes:
    - postgres_data:/var/lib/postgresql
```

> **Postgres 18+ uyumu**: Volume `/var/lib/postgresql`'e mount edilir (alt dizin değil), `PGDATA` ile veri dizini explicit olarak `/var/lib/postgresql/data` alt dizinine yönlendirilir. Bu sayede `pg_upgrade --link` mount sınırı sorunlarına takılmaz. Detay: [docker-library/postgres#37](https://github.com/docker-library/postgres/issues/37).

Uygulama varsayılan olarak PostgreSQL kullanır (`Persistence:Provider = "Postgres"`). Development ortamında migration'lar otomatik çalışır (`MigrateIfDevelopmentAsync`).

### Qdrant

```yaml
qdrant:
  image: qdrant/qdrant:v1.15.5
  ports:
    - "127.0.0.1:7333:6333"  # HTTP API
    - "127.0.0.1:7334:6334"  # gRPC (uygulama bunu kullanır)
  volumes:
    - qdrant_storage:/qdrant/storage:z
```

Üç collection kullanılır: `cs_knowledge`, `cs_episodic`, `cs_lessons`. `KnowledgeBaseIngestor` uygulama başlarken `KnowledgeBase/*.md` dosyalarını chunk'layıp Qdrant'a yazar.

### Redis

```yaml
redis:
  image: redis:8.2
  ports: ["127.0.0.1:6380:6379"]
```

Connection string `appsettings.json`'da tanımlı (`localhost:6379`). **Not**: Docker Compose host portu **6380**, appsettings portu **6379** — farklı. Docker dışında çalıştırıyorsanız port uyumunu kontrol edin.

---

## 4. Health Check'ler

Tüm servisler health check tanımlıdır:

| Servis | Health Check | Interval |
|--------|-------------|----------|
| PostgreSQL | `pg_isready -U postgres` | 10s |
| Redis | `redis-cli ping` | 10s |
| Qdrant | `curl http://localhost:6333/healthz` | 30s |
| Elasticsearch | `curl http://localhost:9200/_cluster/health` | 30s |
| Kibana | `curl http://localhost:5601/api/status` | 30s |
| OTel Collector | `curl http://localhost:13133/` | 30s |
| Jaeger | `curl http://localhost:13133/status` | 30s |

---

## 5. Volume'lar

```yaml
volumes:
  postgres_data:       # PostgreSQL verileri
  redis_data:          # Redis AOF dosyaları
  qdrant_storage:      # Qdrant vektör verileri
  qdrant_snapshots:    # Qdrant snapshot'lar
  elasticsearch_data:  # Jaeger trace geçmişi
```

---

## 6. Production Hazırlık

### Güvenlik

- [ ] `Jwt:SigningKey` değiştir (en az 32 karakter rastgele)
- [ ] `Auth:DefaultAdminPassword` değiştir
- [ ] PostgreSQL parolasını environment variable'a taşı
- [ ] API key'leri environment variable veya Azure Key Vault kullanarak sakla
- [ ] `appsettings.Development.json` production'a deploy etme
- [ ] `Cors:AllowedOrigins`'e panel/chat istemcisinin origin'ini ekle (boş liste Development dışında tüm cross-origin istekleri reddeder)
- [ ] Load balancer / ters proxy arkasındaysan `ForwardedHeaders:KnownProxies` (IP) veya `ForwardedHeaders:KnownNetworks` (CIDR) ayarla — aksi hâlde IP tabanlı hız sınırları (`auth`, `general`) tüm kullanıcıları tek proxy adresi altında toplar
- [ ] Qdrant API key etkinleştir (`QDRANT__SERVICE__API_KEY`)

### Uygulama

- [ ] `dotnet publish -c Release` ile derle
- [ ] Dockerfile oluştur (repo'da henüz yok)
- [ ] `Persistence:Provider = "Postgres"` olduğundan emin ol
- [ ] `SemanticMemory:Enabled = true` ve Qdrant erişilebilir
- [ ] `Telemetry:Otlp:Endpoint` set et (trace exporter)

### Altyapı

- [ ] PostgreSQL: Backup politikası belirle
- [ ] Qdrant: Snapshot schedule ayarla
- [ ] Redis: Persistence (AOF) etkin olsun
- [ ] Jaeger v2 konfigürasyon dosyasını (`jaeger-v2-config.yaml`) production'a uygun şekilde düzenle

---

## Çapraz Referanslar

- **Konfigürasyon detayları** → [operations.md](operations.md)
- **Veritabanı yapısı** → [CustomerSupportBot.Adapters.Persistence/README.md](CustomerSupportBot.Adapters.Persistence/README.md)
- **Güvenlik** → [security.md](security.md)
- **Telemetri stack** → [CustomerSupportBot.Adapters.Telemetry/README.md](CustomerSupportBot.Adapters.Telemetry/README.md)
