# Uçtan uca: temsilci ↔ müşteri sesli görüşme

Elle (ya da Playwright MCP ile) yürütülen kontrol listesi. Tarayıcı WebRTC + MediaRecorder gerektirdiği için
otomatik test paketine girmez.

## Hazırlık

```bash
docker compose -p aibot -f deploy/docker-compose.yml up -d postgres redis coturn
dotnet run --project src/CustomerSupportBot.Api --launch-profile https   # https://localhost:7095
dotnet run --project src/CustomerSupportBot.Web                          # http://localhost:5288
```

- İki ayrı tarayıcı bağlamı (müşteri, temsilci). Chrome'u sahte cihazla başlat:
  `--use-fake-ui-for-media-stream --use-fake-device-for-media-stream --ignore-certificate-errors`.
- Müşteri girişi `http://localhost:5288/`, temsilci girişi `http://localhost:5288/login` (admin / Admin123!).

## Adımlar

| # | Adım | Beklenen |
|---|------|----------|
| 1 | Müşteri bir mesaj yazar (`#userInput`). Temsilci sohbeti devralır (`POST /chat-sessions/{id}/takeover` ya da eskalasyonda "Devral ve sohbet et"). | Sohbet "Canlı sohbetler" sekmesinde görünür. |
| 2 | Temsilci "Sohbeti aç" → başlıkta **Sesli görüşme**. | Müşteride `.cvc-card`: "admin sizi arıyor" + kayıt/döküm rıza metni, **Reddet** / **Kabul et**. |
| 3 | Müşteri **Kabul et**. | 20 sn içinde temsilcide "Görüşmede" + **Kayıt** rozeti; müşteride süre (mm:ss). |
| 4 | 25 sn bekle; `docker exec aibot_postgres psql -U postgres -d CustomerSupportDb -c "select track, sequence from voice.recording_chunks where call_id = '…'"` | Her iz (Agent, Customer) için en az 2 parça; parçalar ~10 sn, `offset_ms` artan. |
| 5 | Döküm satırları | Gerçek anahtarla metin; OpenAI anahtarı yoksa 3 denemeden sonra "(döküm alınamadı)". Satırlar yalnızca temsilcide görünür, müşteride görünmez. |
| 6 | Görüşme açıkken aynı temsilci ikinci kez arar (`POST /chat-sessions/{id}/voice-calls`) ya da başka sohbeti açar. | `409 {"error":"voice_call_busy"}`; başka sohbette düğme pasif, ipucu "Zaten bir sesli görüşmedesiniz." |
| 7 | Müşteri **Bitir**. | İki tarafta kart/çubuk kapanır; iki tarafın geçmişinde "Sesli görüşme · X dk Y sn" notu. |
| 8 | Temsilci döküm satırındaki zamana tıklar. | "Sesli görüşme kaydı" oynatıcısı açılır; **Baştan dinle** iki izi birlikte çalar (`/voice-calls/{id}/chunks/{chunkId}` → 200 `audio/webm`). |
| 9 | Yeni arama; müşteri **Reddet**. | Temsilcide "Müşteri reddetti" bildirimi, çubuk kapanır; görüşme `Declined`. |

## Son koşu — 2026-10-06 (feat/agent-voice-call)

- 1–5, 7–9: geçti. Görüşme 3 dk 28 sn; 2 × 12 parça; dökümler `Failed` (yerelde OpenAI anahtarı yok, beklenen).
- 6: API düzeyinde geçti (409 `voice_call_busy`); "başka sohbette pasif düğme" ikinci canlı sohbet gerektirdiği için
  tarayıcıda denenmedi.
- 9: görüşme 0,2 sn içinde `Declined`; "Müşteri reddetti" bildirimi göründü, çubuk kapandı, düğme geri geldi.
- Koşu sırasında bulunan ve düzeltilen: boş `Realtime:ApiKey` ses dökümünde `OpenAI:ApiKey`'e düşmüyordu;
  oynatıcı gövdesinde iç boşluk yoktu; ret/cevapsız bildirimi JS'in çubuğu önce temizlemesi yüzünden hiç
  gösterilmiyordu (artık sinyal `callId` ile eşleşiyor).
