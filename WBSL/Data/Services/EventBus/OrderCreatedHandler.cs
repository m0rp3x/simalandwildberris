using WBSL.Data.Services.Wildberries;

namespace WBSL.Data.Services.EventBus;

public class OrderCreatedEvent
{
    public long OrderId { get; }
    public OrderCreatedEvent(long orderId) => OrderId = orderId;
}

public class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly WildberriesSupplyService _supply;
    public OrderCreatedHandler(WildberriesSupplyService supply) 
        => _supply = supply;

    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken ct = default)
        => _supply.CreateSupplyAndAttachOrderAsync(
            date: DateTime.UtcNow, 
            orderId: @event.OrderId,
            ct: ct 
        );
}