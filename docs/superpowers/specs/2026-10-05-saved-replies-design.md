# Hazır Yanıtlar — Tasarım

**Tarih:** 2026-10-05 · **Dal:** `feature/saved-replies`

## Amaç

Temsilci asistanı her görüşme için taslak yazıyor ama ekibin ortak kullandığı standart yanıt kütüphanesi
yok. Sık kullanılan yanıtlar (kargo gecikmesi, iade süreci, kapanış mesajı) bir kez yazılsın, herkes kullansın.

## Kapsam

- Ekip çapında kütüphane: yönetici ekler/düzenler/siler; yönetici ve temsilci kullanır.
- Alanlar: başlık (≤100), metin (≤2000), isteğe bağlı kısayol (`kargo-gecikme`; küçük harf, rakam, `-`/`_`,
  Türkçe harfler; ≤40; benzersiz).
- Canlı sohbette "📋 Hazır yanıt" düğmesi: aranabilir liste (başlık/metin/kısayol); seçilen yanıt mesaj
  kutusuna eklenir (boşsa yerine konur, doluysa sonuna eklenir) — gönderilmeden önce düzenlenebilir.
- Yönetim: yalnız yöneticinin gördüğü "Hazır Yanıtlar" sekmesi (liste + ekle/düzenle/sil).
- Kapsam dışı: değişken yer tutucuları (`{müşteri adı}`), kişisel (temsilciye özel) yanıtlar, kategoriler.

## Mimari

- Domain `SavedReply`; port `ISavedReplyStore` (outbound), `ISavedReplyPort` (inbound, doğrulama).
- Postgres `hitl.saved_replies` (kısayolda benzersiz indeks, büyük/küçük harf duyarsız). Önbellek yok:
  tablo küçük, liste yalnızca seçici açılınca okunur.
- Uçlar: `GET /saved-replies?q=` ve `GET /agent/saved-replies?q=` (okuma); `POST /saved-replies`,
  `PUT /saved-replies/{id}`, `DELETE /saved-replies/{id}` (yalnız Admin). Doğrulama hatası 400, aynı
  kısayol 409, bulunamadı 404.

## Test

- Servis: doğrulama, kısayol benzersizliği (büyük/küçük harf), arama.
- Postgres deposu (Testcontainers): CRUD, benzersiz kısayol, arama.
- Uçlar: rol kuralları (temsilci okur, yazamaz), durum kodları.
- Arayüz: seçiciden ekleme, yönetim sekmesi.
