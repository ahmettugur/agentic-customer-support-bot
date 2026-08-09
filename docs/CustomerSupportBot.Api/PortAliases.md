# PortAliases — Api

## Ne İşe Yarar
Api projesinde sık kullanılan model namespace'ini `global using` ile proje geneline açan yardımcı dosyadır.

## Hangi Amaçla Kullanılır
Her endpoint dosyasında tekrar eden `using CustomerSupportBot.Api.Models` import'ını ortadan kaldırır.

## Sorumlulukları
- `CustomerSupportBot.Api.Models` global using — Admin panel HTTP DTO'ları.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Etkilediği proje**: Tüm `CustomerSupportBot.Api` dosyaları.

## Bağımlılıklar
Yok — salt `global using` bildirimi.
