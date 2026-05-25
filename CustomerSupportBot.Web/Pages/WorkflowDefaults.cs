namespace CustomerSupportBot.Web.Pages;

internal static class WorkflowDefaults
{
    public static readonly string SampleJson = @"{
  ""name"": ""Sipariş Durumu Hızlı Yanıt"",
  ""description"": ""4+ haneli sipariş numarası varsa OrderStatus tool'unu çağırır."",
  ""isActive"": true,
  ""triggerKeywords"": [""sipariş"", ""durumu"", ""kargo""],
  ""inputPatterns"": { ""orderId"": ""(\\d{4,})"" },
  ""steps"": [
    { ""type"": ""Branch"", ""label"": ""Sipariş ID var mı?"", ""condition"": ""orderId exists"", ""skipNext"": 2 },
    { ""type"": ""Respond"", ""template"": ""Sipariş numaranızı paylaşır mısınız (ör. 1030)?"" },
    { ""type"": ""Branch"", ""condition"": ""true == true"", ""skipNext"": 99 },
    { ""type"": ""Lookup"", ""tool"": ""order_status_tool"", ""parameters"": { ""orderId"": ""$orderId"" }, ""storeAs"": ""lookup"" },
    { ""type"": ""Respond"", ""template"": ""\uD83D\uDCE6 {lookup}"" }
  ]
}";
}
