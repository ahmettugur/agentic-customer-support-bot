# Knowledge.razor

## Ne İşe Yarar
Bilgi tabanı (Knowledge Base) yönetim sayfasıdır. Makale oluşturma, düzenleme, silme ve yayın durumu kontrol işlemlerini sunar.

## Hangi Amaçla Kullanılır
`/admin/knowledge` (veya benzeri admin yolu) route'unda, admin kullanıcıların bilgi tabanını yönetmesini sağlar.

## Sorumlulukları
- Makale listesini göstermek (filtreleme, arama).
- Yeni makale oluşturma formu.
- Mevcut makaleyi düzenleme.
- Makale silme (onay dialog'u ile).
- Yayın durumu toggle (published/draft).
- İndeksleme durumu uyarıları gösterme.

## Diğer Katman ve Bileşenlerle İlişkileri
- **DI ile inject edilen**: [KnowledgeApiService](../Services/KnowledgeApiService.md), [ToastService](../Services/ToastService.md).
- **Model bağımlılığı**: [KnowledgeModels](../Models/KnowledgeModels.md).
- **Backend karşılığı**: `MemoryEndpoints`.

## Bağımlılıklar
- [KnowledgeApiService](../Services/KnowledgeApiService.md), [ToastService](../Services/ToastService.md).
