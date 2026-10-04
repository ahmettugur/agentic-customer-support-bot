# Temsilci Asistanı — Tasarım

**Tarih:** 2026-10-04 · **Dal:** `feature/agent-assist`

## Amaç

Bir sohbeti devralan temsilci (admin veya Agent rolü), müşterinin geçmişini baştan okumadan
durumu anlayıp hızlı ve doğru yanıt verebilsin. Başarı ölçütü: canlı sohbet panelinde tek
tıklamayla konuşma özeti, müşteri bağlamı, ilgili bilgi tabanı makaleleri ve düzenlenebilir bir
yanıt taslağı görmek. Taslak **asla otomatik gönderilmez** — temsilci karar verir.

## Kapsam

- Panel: canlı sohbet panelinde "🤖 Asistan" butonu → yan kart. İstek üzerine yüklenir (her panel
  açılışında LLM çağrısı yapılmaz — maliyet), "Yenile" ile tekrar üretilir.
- Kart içeriği:
  1. **Özet** ve **müşterinin şu anki talebi** (LLM)
  2. **Önerilen yanıt taslağı** (LLM) + "Taslağı kullan" → mesaj kutusunu doldurur
  3. **Duygu durumu** (oturum state'i — LLM yok)
  4. **Müşteri profili** (özet, ton, ilgi alanları, ortalama puan — LLM yok)
  5. **İlgili bilgi tabanı makaleleri** (vektör arama, en fazla 3 — LLM yok)
  6. **Açık işler:** oturumun açık eskalasyonları (gerekçesiyle) ve bekleyen onayları
- Kapsam dışı: hazır yanıt şablonları, taslağın otomatik gönderimi, çoklu dil.

## Mimari

- **Inbound port** `IAgentAssistPort.GetAssistAsync(sessionId, ct) → AgentAssistResult?`
  (oturum yoksa `null`). Uygulaması `AgentAssistService` (Application/Services/Escalation).
- Bağımlılıklar (hepsi mevcut): `ISessionManager` (state + geçmiş), `ICustomerProfileStore`,
  `IMemoryPort` (Knowledge araması), `IEscalationSink`, `IApprovalQueue`, `IGeneralChatClient`,
  `IPromptRepository`, `IContextSanitizer`.
- **Prompt:** `Prompts/services/agent-assist.md` (proje kuralı: tüm LLM prompt'ları bu klasörde).
  Model çıktısı JSON: `{ "summary", "customerRequest", "suggestedReply" }`.
- **Uçlar:** `GET /chat-sessions/{sid}/assist` (admin grubu) ve
  `GET /agent/chat-sessions/{sid}/assist` (agent grubu) — mevcut `history`/`sentiment` uçlarıyla aynı
  yetki ve hız sınırı grupları. Web `AdminApiService` mevcut rol önekini kullanır.

## Veri akışı

1. Oturum + son 20 mesaj okunur; profil yalnızca `AuthenticatedCustomerId` ile (JWT'den bağlı
   kimlik — `State.CustomerId` değil, başkasının profili gösterilmesin).
2. Bilgi tabanı sorgusu: müşterinin son 2 mesajı; `IMemoryPort.Enabled` değilse atlanır.
3. LLM'e giden kullanıcı mesajı: geçmiş, profil satırı, makale parçaları ve açık işler —
   **veri** olarak etiketli bloklarda (`IContextSanitizer.WrapRetrieved`); prompt, bu blokların
   içindeki talimatların uygulanmamasını söyler (müşteri metni prompt injection taşıyabilir).
4. Taslak kuralları (prompt): yalnızca verilen bağlamdaki bilgiler; onay bekleyen işlemi
   "tamamlandı" diye sunmamak; kaydın kime ait olduğunu sızdırmamak; Türkçe, kısa, nazik.

## Hata yönetimi

- LLM hatası / bozuk JSON → özet ve taslak boş, `AssistError` dolu; LLM'siz bölümler (profil,
  duygu, makaleler, açık işler) yine döner. Kart hatayı gösterir, "Yenile" ile tekrar denenir.
- Vektör arama hatası → makale listesi boş (loglanır), diğer bölümler etkilenmez.
- Oturum yok → 404.

## Test

- Servis: özet/taslak JSON'unun ayrıştırılması (kod bloğu içinde gelse de); LLM hatasında kısmi
  sonuç; profilin `AuthenticatedCustomerId` ile seçilmesi (`State.CustomerId` yok sayılır);
  makalelerin ve açık işlerin oturuma göre süzülmesi; hafıza kapalıyken arama yapılmaması;
  LLM'e giden geçmişin veri bloğunda olması.
- Uç: admin ve agent uçlarının 200/404 dönmesi, müşteri rolüyle erişimin reddi.
- Arayüz: tarayıcıda kartın yüklenmesi ve "Taslağı kullan"ın mesaj kutusunu doldurması.
