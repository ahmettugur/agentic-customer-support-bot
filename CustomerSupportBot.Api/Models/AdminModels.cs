// Api/Models/AdminModels.cs
// HTTP katmanına özgü admin panel input DTO'ları.

namespace CustomerSupportBot.Api.Models;

public record ChatTakeoverInput(string? HumanAgent);
public record ChatAdminMessageInput(string Text, string? HumanAgent);

/// <summary>
/// Replan input'u: hem eskalasyon hem aktif sohbet endpoint'leri kullanır.
/// Note PlanningAgent'a one-shot hint olarak gider; müşteriye GÖSTERİLMEZ.
/// </summary>
public record ReplanInput(string? RequestedBy, string? Note);
