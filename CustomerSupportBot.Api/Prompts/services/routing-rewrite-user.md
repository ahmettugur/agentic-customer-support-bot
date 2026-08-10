## Müşteri sorusu

{{ORIGINAL_QUERY}}

## Dahili mesaj

{{ROUTING_MESSAGE}}

---

## Örnekler

Aşağıdaki örnekler, farklı türde dahili mesajların kullanıcıya nasıl yansıtılacağını gösterir.
Örüntüye bak, özgün bir yanıt üret — örnek metni birebir kopyalama.

### Örnek 1 — Eksik bilgi talebi

**Müşteri sorusu:** Bir ürün sipariş etmek istiyorum.

**Dahili mesaj:** `OrderAgent: product_name ve quantity eksik, ikisi birden iste`

**Beklenen yanıt:** Tabii, hangi üründen kaç adet istediğinizi yazar mısınız?

> ⚠️ Müşteri kimliği **hiçbir zaman** eksik bilgi olarak istenmez — kullanıcı giriş yapmış
> durumda, kimliği sistemden biliniyor. Aynı şekilde *"siparişim nerede?"* gibi bir soruda
> sipariş numarası da sorulmaz; sistem otomatik olarak son siparişi getirir.

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

**Müşteri sorusu:** 1030 numaralı siparişimi iptal et, bir de X100 kulaklığın fiyatına bakar mısın?

**Dahili mesaj:** `OrderAgent: cancel 1030 → ProductAgent: price lookup for X100`

**Beklenen yanıt:** 1030 numaralı siparişiniz için iptal talebinizi ilettim, onaylandığında haber vereceğiz. X100 kulaklığın fiyatına da hemen bakıyorum.

> ⚠️ Sipariş oluşturma / iptal / iade ve şikayet kaydı bir yönetici onayından geçer — bunları
> *"iptal ettim"*, *"başlattım"*, *"tamamladım"* diye **olmuş gibi** anlatma; *"talebinizi
> ilettim / onaya gönderdim"* de.
