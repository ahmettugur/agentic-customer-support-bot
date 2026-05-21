namespace CustomerSupportBot.Application.Ports.Driving;

public interface IHitlEventSubscription : IDisposable;

/// <summary>
/// Chat streaming adapter'ının session bazlı approval/escalation event'lerini
/// dinlemek için kullandığı primary port.
/// </summary>
public interface IHitlEventPort
{
    IHitlEventSubscription Subscribe(string sessionId, Func<string, object, Task> onEvent);
    IHitlEventSubscription SubscribeToChatEvents(string sessionId, Func<string, object, Task> onEvent);
}
