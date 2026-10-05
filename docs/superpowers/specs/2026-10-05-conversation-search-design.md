# Konuşma Arama — Tasarım

**Tarih:** 2026-10-05 · **Dal:** `feature/conversation-search`

## Amaç

Yönetici geçmiş bir konuşmayı bulamıyor: panelde yalnızca aktif sohbetler ve son eskalasyon/onay
kayıtları var. Müşteri şikayeti, kalite denetimi ya da "geçen hafta kargo gecikmesi konuşmaları" gibi
sorular için konuşmalar aranabilmeli.

Başarı ölçütleri:
- Mesaj metninde arama (Türkçe büyük/küçük harf duyarsız: "İADE" = "iade" = "Iade").
- Filtreler: müşteri numarası, tarih aralığı, kapanış nedeni, etiket. Hiç filtre yoksa son konuşmalar.
- Sonuçta eşleşen mesajdan bir alıntı (terim vurgulu), mesaj sayısı, kapanış nedenleri ve etiketler.
- Sonuçtan konuşmanın tamamı açılır (mevcut döküm penceresi).
- Tek sorgu: oturum sayısı arttıkça oturum başına sorgu yapılmaz.

## Mimari

- **Inbound `IConversationSearchPort` / `ConversationSearchService`:** girdiyi doğrular ve normalleştirir,
  sayfalar (25'er; bir fazlası istenir → `HasMore`), alıntıyı üretir.
  - Metin: kırpılır; 2–200 karakter. Etiket kapanıştaki kuralla normalleştirilir.
  - `From` > `To` → hata.
- **Outbound `IConversationSearchStore` / `PostgresConversationSearchStore`:** tek SQL sorgusu:
  `chat.sessions` filtrelenir (müşteri `state->>'AuthenticatedCustomerId'`, tarih `last_activity`,
  neden/etiket `chat.conversation_dispositions` üzerinden `EXISTS`, metin `chat.messages` üzerinden
  `EXISTS`), son etkinliğe göre sıralanır; sayfadaki her oturum için mesaj sayısı, eşleşen ilk mesaj
  (metin yoksa ilk müşteri mesajı), nedenler ve etiketler alt sorgularla gelir.
- **Türkçe katlama:** veritabanının yerel ayarına (`lower()` 'C' yerelinde yalnızca ASCII'yi çevirir)
  bağlı kalmamak için hem SQL'de hem C#'ta aynı katlama: `İ I ı → i`, `Ç Ğ Ö Ş Ü → ç ğ ö ş ü`, sonra
  küçük harf. Arama `LIKE '%…%'` (özel karakterler kaçırılır). Bilinçli gevşeklik: "ı" ile "i" aynı sayılır
  ("kapı" ≈ "kapi").
- **Uç:** `GET /conversations/search?q=&customerId=&from=&to=&reason=&tag=&page=` — yalnız Admin. Geçersiz
  girdi 400. `from`/`to` UTC anlarıdır (`to` hariç); gün sınırlarını panel tarayıcının yerel saatine göre
  hesaplar — sunucu saat dilimi varsaymaz.
- **Arayüz:** yönetici panelinde "🔎 Konuşmalar" sekmesi: filtre formu (neden listesi kapanış
  seçeneklerinden), sonuç kartları, "Daha fazla", "Görüntüle" → döküm penceresi.

## Bilinen sınır

Metin araması dizinsizdir (`LIKE '%…%'` sıralı tarar). Saklama süresi (180 gün) tabloyu sınırlar; çok
büyük hacimde `pg_trgm` GIN dizini eklenebilir (eklenti yetkisi gerektirir — kapsam dışı).

## Test

- Servis: doğrulama, sayfalama, alıntı (eşleşmenin çevresi, Türkçe harf farkıyla), etiket normalleştirme.
- Postgres (Testcontainers): metin (Türkçe harf varyantları, `%`/`_` kaçırma), müşteri, tarih, neden,
  etiket filtreleri; mesaj sayısı; eşleşen ilk mesaj; metin yokken ilk müşteri mesajı; sayfalama.
- API: yalnız Admin, 400, parametrelerin servise geçişi.
- Tarayıcı: filtreleme, vurgulu alıntı, döküm açma, daha fazla.
