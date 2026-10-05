# Temsilci Çevrimiçi/Uzakta Durumu — Tasarım

**Tarih:** 2026-10-05 · **Dal:** `feature/agent-presence`

## Amaç

Akıllı yönlendirme (`SkillsBasedRouter`) eskalasyonu yalnızca `IsActive` ve kapasiteye bakarak bir
temsilciye öneriyor. Paneli kapalı, molada ya da mesaisi bitmiş bir temsilci de öneri alıyor; müşteri
kimsenin bakmadığı bir kuyrukta bekliyor. Temsilcinin anlık durumu yönlendirmeye girmeli.

Başarı ölçütleri:
- Temsilci panelden durumunu seçer: **Çevrimiçi**, **Uzakta**, **Çevrimdışı**.
- Paneli açık olmayan temsilci kendiliğinden çevrimdışı sayılır (kalp atışı zaman aşımı).
- Yeni eskalasyon yalnızca **çevrimiçi** temsilciye önerilir (ayarla kapatılabilir).
- Yönetici kimin çevrimiçi olduğunu görür.
- Çok pod'lu kurulumda durum tüm pod'larda aynıdır ve yük sayaçlarını bozmaz.

## Durum modeli

- Saklanan: `HumanAgent.Presence` (`Offline` | `Online` | `Away`), `PresenceChangedAt`, `LastSeenAt`.
- **Geçerli durum** (okuma anında hesaplanır): saklanan durum `Offline` değilse ve `LastSeenAt`
  `PresenceTimeoutSeconds`'tan yeniyse saklanan durum; aksi hâlde `Offline`.
  `HumanAgent.EffectivePresence(now, timeout)`.
- Panel açıkken 30 sn'de bir kalp atışı (`LastSeenAt`). Panel kapanınca ayrıca çağrı gerekmez: zaman
  aşımı (varsayılan 90 sn) temsilciyi çevrimdışına düşürür. Birden çok sekme sorun olmaz.
- Panel açılışı: saklanan durum `Offline` ise `Online` yapılır; `Online`/`Away` ise korunur (kalp atışı
  yeterli). Yani molaya "Uzakta" diye çıkan temsilci sayfayı yenileyince çevrimiçine dönmez.

## Yönlendirme

`RoutingOptions`:

| Ayar | Varsayılan | Anlamı |
|---|---|---|
| `RequireOnlineAgent` | `true` | Yeni eskalasyon yalnızca geçerli durumu `Online` olan temsilciye önerilir. `false` = eski davranış. |
| `PresenceTimeoutSeconds` | `90` | Kalp atışı bu süre gelmezse temsilci çevrimdışı sayılır. |

Çevrimiçi aday yoksa öneri boş kalır, not: "Çevrimiçi temsilci yok; eskalasyon açık kuyrukta bekliyor."
Eskalasyon yine oluşur ve tüm temsilcilerin "açık" listesinde görünür. Uzakta/çevrimdışı olan temsilcinin
mevcut atamaları değişmez.

**Kapsam dışı:** temsilci çevrimiçi olunca bekleyen atanmamış eskalasyonları otomatik atama; müşteriye
"şu an temsilci yok" mesajı; mesai takvimi.

## Mimari

- **Domain:** `AgentPresence` enum'u, `HumanAgent` alanları ve `EffectivePresence`.
- **Outbound `IHumanAgentRegistry`:** `SetPresence(id, presence, nowUtc)`, `TouchPresence(id, nowUtc)`.
- **Inbound `IAgentPresencePort` / `AgentPresenceService`:** kendi durumunu oku/ayarla/kalp atışı;
  yöneticiye tüm aktif temsilcilerin geçerli durumu (`AgentPresenceInfo`).
- **Postgres:** `hitl.human_agents` tablosuna `presence`, `presence_changed_at`, `last_seen_at`
  (migration `AddAgentPresence`). Durum yazımı yalnızca bu sütunları günceller.
- **Pod'lar arası:** durum değişikliği ve kalp atışı **yalnızca durum alanlarını** taşıyan ayrı bir
  kanala yayınlanır (`csbot:humanagent:presence`). Tam kayıt yayınlansaydı, her 30 sn'lik kalp atışı
  yayınlayan pod'un elindeki (eski olabilecek) yük sayacını tüm pod'lara yayardı. Tam kayıt yayınında
  (`upserted`) da durum alanları birleştirilir: daha yeni `PresenceChangedAt`/`LastSeenAt` korunur.
  Yönetici güncellemesi (`Update`) durum sütunlarına dokunmaz.
- **Uçlar:**
  - `GET /agent/presence`, `PUT /agent/presence` (`{ presence }`), `POST /agent/presence/connect`
    (panel açılışı kuralı sunucuda), `POST /agent/presence/heartbeat` — temsilcinin kendisi
    (`linked_agent_id`); bağlı kayıt yoksa 400, temsilci kaydı yoksa 404.
  - `GET /agents/presence` — yalnız Admin: tüm aktif temsilcilerin geçerli durumu ve yükü.
- **Arayüz:** temsilci panelinin başlığında durum seçici (renkli nokta); 30 sn'lik kalp atışı.
  Yönetici: Eskalasyonlar sekmesinde temsilci durumu şeridi; yeniden atama penceresinde her temsilcinin
  durumu.

## Test

- Domain: geçerli durum (zaman aşımı, çevrimdışı, uzakta).
- Router: çevrimdışı/uzakta/zaman aşımına uğramış temsilci önerilmez; `RequireOnlineAgent=false` eski
  davranış; aday yoksa not.
- `AgentPresenceService`: panel açılışı kuralı (Offline → Online, Away korunur), bilinmeyen temsilci.
- Postgres (Testcontainers): durum kalıcı; diğer pod'a yayılır; kalp atışı yük sayacını ezmez.
- API: rol/claim kuralları, geçersiz durum 400.
- Tarayıcı: seçici, kalp atışı, yönetici şeridi.
