using WBSL.Data.Services.Wildberries;

namespace WBSL.Data.Services.EventBus;

public class OrderCreatedEvent
{
    public List<long> OrderIds { get; }
    public OrderCreatedEvent(List<long> orderIds) => OrderIds = orderIds;
}

public class OrderCreatedHandler : IEventHandler<OrderCreatedEvent>
{
    private readonly WildberriesSupplyService _supply;
    public OrderCreatedHandler(WildberriesSupplyService supply) 
        => _supply = supply;

    public Task HandleAsync(OrderCreatedEvent @event, CancellationToken ct = default)
        => _supply.CreateSupplyAndAttachOrderAsync(
            date: DateTime.UtcNow, 
            orderIds: @event.OrderIds,
            ct: ct 
        );
}