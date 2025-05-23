namespace WBSL.Data.Services.EventBus;

public interface IEventBus
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default);
    void Subscribe<TEvent, THandler>()
        where THandler : IEventHandler<TEvent>;
}

public interface IEventHandler<TEvent>
{
    Task HandleAsync(TEvent @event, CancellationToken ct = default);
}
