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

**KVKK:** İçerik kaydı açıkken Langfuse kişisel veri saklayan bir sistem olur. Bunun için `Telemetry:Langfuse`
açıkken (aşağıya bakın) müşteri/oturum silme akışı Langfuse'taki kayıtları da siler. Üretimde yine de Langfuse'un
kendi saklama süresini ayarlayın ya da içerik kaydını kapalı tutun.

## Müşteri puanı: skorlar ve veri silme (`Telemetry:Langfuse`)

Trace'ler OTel Collector üzerinden gider; aşağıdaki iki özellik ise Langfuse'un **REST API**'sini kullanır ve
proje anahtarları ister. Varsayılan **kapalıdır**:

```json
"Telemetry": {
  "Langfuse": { "Enabled": true, "BaseUrl": "http://localhost:3000", "PublicKey": "pk-lf-...", "SecretKey": "sk-lf-..." }
}
```

Anahtarlar `deploy/.env`'deki `LANGFUSE_INIT_PROJECT_*` değerleridir; yerelde `appsettings.Development.json`, üretimde
ortam değişkeni/secret store ile verin (`Telemetry__Langfuse__SecretKey`).

- **Skor:** Müşteri puan verince (`POST` puanlama ucu) `customer_rating` adlı 1–5 arası sayısal skor, o oturuma
  bağlı olarak Langfuse'a yazılır; yorum varsa skorun yorumu olur. Skor kimliği oturuma sabittir: yeniden puanlama
  skoru günceller (yorum yoksa eski yorum temizlenir). Langfuse'ta *Sessions* altında oturumun yanında görünür; düşük
  puanlı oturumlar süzülüp trace'leri incelenebilir. Gönderim en iyi gayrettir (3 sn sınırı, hata yutulur): Langfuse
  erişilemese de puan kaydedilir.
- **Veri silme:** `DataPrivacyService` oturum silerken (saklama süresi dolunca ya da müşteri talebiyle) o oturumun
  trace'lerini ve puan skorunu; müşteri silerken `user.id` müşteri numarası olan trace'leri Langfuse'tan siler.
  **Langfuse erişilemezse silme hata verir** ve oturum yerinde kalır; silme sonraki turda/talepte tekrar denenir
  (kişisel veri silinmeden "silindi" raporlanmaz). Silme Langfuse tarafında asenkrondur, birkaç saniye sürer.

> Langfuse v4 (`events_only` modu) eski `traces`/`sessions`/`scores` okuma uçlarını kapatmıştır. Silme bu yüzden
> gözlem (`/api/public/v2/observations`) üzerinden `sessionId`/`userId` süzmesiyle trace'leri bulur.

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
