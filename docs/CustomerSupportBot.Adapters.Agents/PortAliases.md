# PortAliases

## Ne İşe Yarar
Agents adapter katmanında sık kullanılan outbound port namespace'lerini `global using` ile proje geneline açan yardımcı dosyadır.

## Hangi Amaçla Kullanılır
Her dosyada tekrar eden `using CustomerSupportBot.Application.Ports.Outbound.Persistence` gibi uzun import'ları ortadan kaldırmak için kullanılır.

## Sorumlulukları
- `CustomerSupportBot.Application.Ports.Outbound.Persistence` global using.
- `CustomerSupportBot.Application.Ports.Outbound.Observability` global using.
- `CustomerSupportBot.Application.Ports.Outbound` global using.

## Diğer Katman ve Bileşenlerle İlişkileri
- **Etkilediği proje**: Tüm `CustomerSupportBot.Adapters.Agents` dosyaları — bu namespace'lere import yazmadan erişir.
- **Referans**: `CustomerSupportBot.Application` → Ports katmanı.

## Kullanılma Nedeni ve Tasarım Yaklaşımı
DRY prensibi — namespace tekrarını önler. Driven port alias'ları kaldırılmış, yalnızca namespace import'ları kalmıştır.

## Bağımlılıklar
Yok — salt `global using` bildirimleri.
