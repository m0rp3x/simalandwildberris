namespace WBSL.Data.Services.EventBus;

public class InMemoryEventBus : IEventBus
{
    private readonly IServiceProvider _sp;
    private readonly Dictionary<Type, List<Type>> _handlers = new();

    public InMemoryEventBus(IServiceProvider sp)
    {
        _sp = sp;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken ct = default)
    {
        if (!_handlers.TryGetValue(typeof(TEvent), out var handlerTypes))
            return;

        var tasks = handlerTypes.Select(handlerType => InvokeHandlerSafe(handlerType, @event, ct));
        await Task.WhenAll(tasks);
    }

    private async Task InvokeHandlerSafe<TEvent>(
        Type handlerType,
        TEvent @event,
        CancellationToken ct)
    {
        using var scope   = _sp.CreateScope();
        var       handler = (IEventHandler<TEvent>)scope.ServiceProvider.GetRequiredService(handlerType);
        try
        {
            await handler.HandleAsync(@event, ct).ContinueWith(t => scope.Dispose(), TaskScheduler.Default);
        }
        catch (Exception ex)
        {
            string ErrorMessage = $"Ошибка в обработчике {handlerType.Name} для события {typeof(TEvent).Name}: {ex.Message}";
            await Console.Error.WriteLineAsync(ErrorMessage);
        }
    }
    
    public void Subscribe<TEvent, THandler>()
        where THandler : IEventHandler<TEvent>
    {
        var eventType   = typeof(TEvent);
        var handlerType = typeof(THandler);

        if (!_handlers.ContainsKey(eventType))
            _handlers[eventType] = new List<Type>();

        _handlers[eventType].Add(handlerType);
    }
}