## [ÖN-ANALİZ REASONING ÇIKTISI — yalnızca bilgilendirme amaçlı]

Aşağıdaki ön-analiz kullanıcı sorgusu üzerinde yapıldı. Planlama ve specialist kararlarında bu analizi **dikkate al**:

{{REASONING_LINES}}

### Önemli kurallar

- **Tutarlılık**: Clarification sorusu üreteceksen, yukarıdaki *"Gerekli bilgiler"* listesiyle **tutarlı ol** (aynı alanı iste).
- **Tek mesaj, tüm alanlar**: *"Gerekli bilgiler"* listesinde 1'den fazla alan varsa, **tek clarification mesajında tümünü birden** iste (ping-pong sorgulama yapma; kullanıcıyı birden fazla tura sokma).
- **Yanlış ön-analiz**: Eğer ön-analiz yanlışsa farklı karar verebilirsin — ancak `rationale` alanında **gerekçeni** belirt.
- **Sipariş sorgulama önceliği**:
  - `order_id` varsa `customer_id` **isteme**.
  - Sadece `customer_id` varsa `order_id` **isteme** (`get_last_order_tool` son siparişi getirir).
  - İkisi de yoksa sadece *"sipariş no VEYA müşteri kimliği"* şeklinde **tek seçenek** sun.
- **Şikayet**: `order_id` zorunlu; `customer_id` eksikse tool `order_id`'den türetir — kullanıcıya `customer_id`'yi **iki kez sorma**.
