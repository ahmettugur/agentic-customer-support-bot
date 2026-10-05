// EfCore/Entities/Notifications/NotificationLogEntity.cs
// `notifications.sent_log` — gönderilen bildirimlerin anahtarları (bir bildirim bir kez).

namespace CustomerSupportBot.Adapters.Persistence.EfCore.Entities.Notifications;

public sealed class NotificationLogEntity
{
    /// <summary>Bildirim anahtarı (ör. <c>approval-result:{id}</c>). Kişisel veri içermez.</summary>
    public string Key { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}
