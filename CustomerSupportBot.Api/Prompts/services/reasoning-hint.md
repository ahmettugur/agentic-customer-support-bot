## [ÖN-ANALİZ REASONING ÇIKTISI — yalnızca bilgilendirme amaçlı]

Aşağıdaki ön-analiz kullanıcı sorgusu üzerinde yapıldı. Planlama ve specialist kararlarında bu analizi **dikkate al**:

{{REASONING_LINES}}

### Önemli kurallar

- **Tutarlılık**: Clarification sorusu üreteceksen, yukarıdaki *"Gerekli bilgiler"* listesiyle **tutarlı ol** (aynı alanı iste).
- **Tek mesaj, tüm alanlar**: *"Gerekli bilgiler"* listesinde 1'den fazla alan varsa, **tek clarification mesajında tümünü birden** iste (ping-pong sorgulama yapma; kullanıcıyı birden fazla tura sokma).
- **Yanlış ön-analiz**: Eğer ön-analiz yanlışsa farklı karar verebilirsin — ancak `rationale` alanında **gerekçeni** belirt.
- **Sipariş sorgulama önceliği**: `customer_id` login'den otomatik geldiği için asla eksik olamaz.
  - `order_id` varsa `order_status_tool` kullanılacak.
  - `order_id` yoksa `get_last_order_tool` otomatik son siparişi getirecek — hiçbir şey isteme.
- **Şikayet**: sadece `order_id` ve açıklama zorunlu; `customer_id` bir tool parametresi bile değildir, hiç gündeme getirme.
