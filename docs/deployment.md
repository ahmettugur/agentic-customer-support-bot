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
  sandbox:         # Python kod çalıştırma (izole)
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
| **Python Sandbox** | 8100 | 8100 | Kod çalıştırma API |

### OTel Collector (Opsiyonel)

Jaeger v2 doğrudan OTLP alabildiği için OTel Collector artık opsiyoneldir. `docker compose --profile otel up` ile ayrıca açılabilir. Port çakışması oluşmaması için OTel Collector host portları 4327/4328 olarak ayarlanmıştır.

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
  image: postgres
  ports: ["5433:5432"]
  environment:
    POSTGRES_USER: postgres
    POSTGRES_PASSWORD: <password>
    PGDATA: /var/lib/postgresql/data
  volumes:
    - postgres_data:/var/lib/postgresql
```

> **Postgres 18+ uyumu**: Volume `/var/lib/postgresql`'e mount edilir (alt dizin değil), `PGDATA` ile veri dizini explicit olarak `/var/lib/postgresql/data` alt dizinine yönlendirilir. Bu sayede `pg_upgrade --link` mount sınırı sorunlarına takılmaz. Detay: [docker-library/postgres#37](https://github.com/docker-library/postgres/issues/37).

Uygulama varsayılan olarak PostgreSQL kullanır (`Persistence:Provider = "Postgres"`). Development ortamında migration'lar otomatik çalışır (`MigrateIfDevelopmentAsync`).

### Qdrant

```yaml
qdrant:
  image: qdrant/qdrant:latest
  ports:
    - "7333:6333"  # HTTP API
    - "7334:6334"  # gRPC (uygulama bunu kullanır)
  volumes:
    - qdrant_storage:/qdrant/storage:z
```

Üç collection kullanılır: `cs_knowledge`, `cs_episodic`, `cs_lessons`. `KnowledgeBaseIngestor` uygulama başlarken `KnowledgeBase/*.md` dosyalarını chunk'layıp Qdrant'a yazar.

### Python Sandbox

```yaml
sandbox:
  image: python:3.12-slim
  ports: ["8100:8100"]
  networks: [sandbox_net]  # İzole ağ
  deploy:
    resources:
      limits:
        cpus: "1.0"
        memory: 512M
```

İzole ortamda Python kodu çalıştırır. **sandbox_net** sadece iç ağ — dış erişim yok. `./sandbox/sandbox_api.py` dosyası gereklidir (repo'da olmayabilir).

### Redis

```yaml
redis:
  image: redis
  ports: ["6380:6379"]
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
- [ ] CORS policy'sini kısıtla (sadece bilinen origin'ler)
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
