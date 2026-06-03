namespace CustomerSupportBot.Web.Pages;

internal static class WorkflowDefaults
{
    public static readonly string SampleJson = """
        {
          "name": "Siparis Durum Sorgulama",
          "description": "4+ haneli siparis numarasi varsa OrderStatus tool'unu cagirır.",
          "isActive": true,
          "startStepId": "br1",
          "triggerKeywords": ["siparis", "durumu", "kargo"],
          "inputPatterns": { "orderId": "(\\d{4,})" },
          "steps": [
            {
              "id": "br1",
              "type": "Branch",
              "label": "Siparis no var mi?",
              "condition": "orderId exists",
              "onTrue": "lk1",
              "onFalse": "r_ask"
            },
            {
              "id": "r_ask",
              "type": "Respond",
              "label": "Numara iste",
              "template": "Siparisınizi sorgulayabilmem icin siparis numaranizi paylasır mısınız? (or: 1030)"
            },
            {
              "id": "lk1",
              "type": "Lookup",
              "label": "OrderStatus cagir",
              "tool": "order_status_tool",
              "parameters": { "orderId": "$orderId" },
              "storeAs": "siparis",
              "next": "r_result"
            },
            {
              "id": "r_result",
              "type": "Respond",
              "label": "Sonucu goster",
              "template": "Siparis #{orderId} durumu:\n\n{siparis}"
            }
          ]
        }
        """;
}
