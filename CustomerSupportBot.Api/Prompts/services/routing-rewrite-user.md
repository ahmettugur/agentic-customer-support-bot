## Müşteri sorusu

{{ORIGINAL_QUERY}}

## Dahili mesaj

{{ROUTING_MESSAGE}}

---

## Örnekler

Aşağıdaki örnekler, farklı türde dahili mesajların kullanıcıya nasıl yansıtılacağını gösterir.
Örüntüye bak, özgün bir yanıt üret — örnek metni birebir kopyalama.

### Örnek 1 — Eksik bilgi talebi

**Müşteri sorusu:** Siparişim nerede?

**Dahili mesaj:** `OrderAgent: order_id bilgisi eksik, kullanıcıdan iste. complaint_id gerekmiyor.`

**Beklenen yanıt:** Sipariş durumunuzu kontrol edebilmem için sipariş numaranızı paylaşır mısınız?

---

### Örnek 2 — Agent yönlendirmesi (bilgi yeterli)

**Müşteri sorusu:** 1042 numaralı siparişim ne zaman gelecek?

**Dahili mesaj:** `OrderAgent: route to order inquiry, order_id=1042 verified`

**Beklenen yanıt:** 1042 numaralı siparişinizin teslimat bilgilerini hemen kontrol ediyorum.

---

### Örnek 3 — Birden fazla eksik alan

**Müşteri sorusu:** Şikayetimi iletmek istiyorum.

**Dahili mesaj:** `ComplaintAgent: order_id ve şikayet konusu eksik, ikisi birden iste`

**Beklenen yanıt:** Şikayetinizi kayıt altına alabilmem için sipariş numaranızı ve şikayetinizin konusunu belirtir misiniz?

---

### Örnek 4 — Ürün sorusu, ek bilgi yok

**Müşteri sorusu:** Bu ürün su geçirmez mi?

**Dahili mesaj:** `ProductAgent: route to product info, no entity needed`

**Beklenen yanıt:** Ürünün su geçirmezlik özelliğini sizin için araştırıyorum.

---

### Örnek 5 — Çoklu görev (compound query)

**Müşteri sorusu:** 1030 numaralı siparişimi iptal et ve iade sürecini başlat.

**Dahili mesaj:** `OrderAgent: cancel 1030 → ComplaintAgent: initiate refund for 1030`

**Beklenen yanıt:** 1030 numaralı siparişinizin iptal ve iade işlemlerini başlatıyorum; her iki adımı sırasıyla tamamlayacağım.
