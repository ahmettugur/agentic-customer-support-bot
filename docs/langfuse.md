# Langfuse — LLM gözlemlenebilirliği

Langfuse, LLM çağrılarını "generation" olarak gösteren açık kaynak bir arayüzdür: her çağrının modeli, token
kullanımı, maliyeti, süresi ve (açıksa) prompt/yanıt metni; çağrılar oturum ve müşteri bazında gruplanır.
Jaeger altyapı ayrıntısı (HTTP, EF Core, Redis) için kullanılmaya devam eder; Langfuse yalnızca ajan ve LLM
span'lerini alır.

```
Uygulama ──OTLP gRPC (4327)──▶ otel-collector ──▶ Jaeger   (tüm span'ler, içerik silinmiş)
                                                       └──▶ Langfuse (ajan/LLM span'leri + /chat kök span'i)
```

## Kurulum (yerel)

Langfuse ana yığının parçasıdır (`deploy/docker-compose.yml`): `langfuse-web`, `langfuse-worker`, ClickHouse (olay
deposu) ve MinIO (ham yükleme deposu). Postgres ve Redis uygulamanınkilerle paylaşılır, ama ayrı yerlerde:

- **Postgres:** aynı sunucuda ayrı `langfuse` veritabanı. İlk açılışta Langfuse'un migration adımı onu kendisi
  oluşturur; uygulamanın `CustomerSupportDb`'sine dokunmaz.
- **Redis:** 1 numaralı veritabanı (`redis://redis:6379/1`); uygulama 0'ı kullanır. Langfuse kuyrukları `noeviction`
  ister, Redis bu politikayla çalıştırılır (`--maxmemory-policy noeviction`) — uygulamanın Redis'ine `maxmemory`
  koyarken bunu değiştirmeyin.

1. Gizli değerler: `deploy/.env.example`'daki `LANGFUSE_*` satırlarını `deploy/.env`'e kopyalayıp doldurun
   (`openssl rand -hex 32` → `LANGFUSE_ENCRYPTION_KEY`, tam 64 hex karakter olmalı). Eksik değer varsa compose açık
   bir hata mesajıyla durur.
2. Yığını başlatın:

   ```bash
   cd deploy && docker compose -p aibot up -d
   ```

   İlk açılışta "headless init" organizasyonu, projeyi, API anahtarlarını ve kullanıcıyı oluşturur; collector aynı
   anahtarlarla kimlik doğrular, elle anahtar üretmek gerekmez.
3. Uygulama span'leri collector'a gönderir: `Telemetry:Otlp:Endpoint = http://localhost:4327` (varsayılan).
   `4317` doğrudan Jaeger'dır; o uçla Langfuse'a bir şey gitmez.
4. Arayüz: <http://localhost:3000> — `LANGFUSE_INIT_USER_EMAIL` / `LANGFUSE_INIT_USER_PASSWORD` ile giriş.

## Langfuse'ta ne görünür

| Langfuse alanı | Kaynak |
|---|---|
| Trace adı | Sohbet ucunun ASP.NET span'i (`POST /chat/stream`) |
| Ajan adımları | MAF `UseOpenTelemetry` — `invoke_agent` span'leri |
| Generation (model, token) | `TelemetryChatClient` span'i — `gen_ai.request.model`, `gen_ai.response.model`, `gen_ai.usage.input_tokens/output_tokens`, `langfuse.observation.type = generation` |
| Maliyet | Uygulamanın hesapladığı tutar: `langfuse.observation.cost_details` (`Telemetry:Pricing`). Langfuse'un fiyat tablosunda olmayan modellerde de görünür. |
| Session | `session.id` / `langfuse.session.id` — sohbet oturumu kimliği |
| User | `user.id` / `langfuse.user.id` — müşteri numarası (ajan turu içindeki çağrılarda) |
| Girdi / çıktı | Yalnızca `Telemetry:CaptureLlmContent = true` iken — aşağıya bakın |

## Prompt ve yanıt içeriği (`Telemetry:CaptureLlmContent`)

Varsayılan **kapalıdır**: prompt ve yanıt müşteri verisi taşır. Açmak için:

```bash
Telemetry__CaptureLlmContent=true
```

Açıkken:

- Girdi, mesaj listesi olarak yazılır (`[{"role":"user","content":"…"}]`); araç çağrıları `→ order_status_tool({…})`,
  sonuçları `← call-id: …` biçiminde görünür — modelin hangi veriyle karar verdiği izlenebilir.
- Her metin span'e yazılmadan önce `InputGuard.RedactPii` ile maskelenir: kart numarası, telefon, TC kimlik no ve
  e-posta. **Ad-soyad ve adres gibi serbest kişisel veri maskelenmez.**
- Girdi ve çıktı ayrı ayrı `Telemetry:MaxLlmContentChars` (varsayılan 8000) karakterle sınırlanır; sınırı aşan
  girdi JSON yerine kısaltılmış düz metin olur.
- İçerik yalnızca Langfuse'ta saklanır: collector Jaeger'a giden kopyadan `langfuse.observation.input/output`
  niteliklerini siler (`attributes/strip-llm-content`).

**KVKK:** İçerik kaydı açıkken Langfuse kişisel veri saklayan bir sistem olur. Uygulamanın müşteri verisi silme
akışı (`DataPrivacyService`) Langfuse'taki kayıtları silmez; Langfuse'un kendi saklama süresi ayarlanmalı ya da
üretimde içerik kaydı kapalı tutulmalıdır.

## Sorun giderme

- **Langfuse'ta trace yok:** Uygulama `4327`'ye mi gönderiyor? Collector sağlığı: <http://localhost:13133/>;
  günlük: `docker logs aibot_otel-collector`. 401 görülüyorsa `.env`'deki anahtarlar ilk açılıştakiyle
  aynı değil (headless init yalnızca ilk açılışta çalışır; değiştirdiyseniz Langfuse'ta yeni anahtar oluşturun).
- **Generation yerine düz span:** `langfuse.observation.type` ve `gen_ai.*` nitelikleri `TelemetryChatClient`'tan
  gelir; `Telemetry:Enabled` kapalıysa bu istemci devrede değildir.
- **Maliyet yok:** `Telemetry:Pricing`'de modelin fiyatı tanımlı mı (`default` satırı yedek fiyattır).

İlgili: [`deploy/docker-compose.yml`](../deploy/docker-compose.yml),
[`deploy/otel-collector-config.yaml`](../deploy/otel-collector-config.yaml),
[TelemetryChatClient](CustomerSupportBot.Adapters.Telemetry/Chat/TelemetryChatClient.md).
