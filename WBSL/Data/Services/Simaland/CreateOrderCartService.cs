using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Shared;
using WBSL.Data;
using WBSL.Data.Models;
using WBSL.Data.Services;
using WBSL.Data.Services.EventBus;
using WBSL.Models;

public record ItemDto(string Sid, int Qty);

public class CreateOrderResponse
{
    [JsonPropertyName("order_id")]
    public long OrderId { get; set; }
}

public interface IOrderConnector
{
    Task<CreateOrderResponse> CreateOrderAsync(List<ItemDto> items);
}


public class CreateOrderCartService
{
    private readonly QPlannerDbContext _db;
    private readonly ILogger<CreateOrderCartService> _log;
    private readonly IOrderConnector _connector;
    private readonly IEventBus _bus;

    public CreateOrderCartService(
        QPlannerDbContext db,
        ILogger<CreateOrderCartService> log,
        IOrderConnector connector,
        IEventBus bus)
    {
        _db = db;
        _log = log;
        _connector = connector;
        _bus            = bus;

    }
    public async Task TestBus(){
        await _bus.PublishAsync(new OrderCreatedEvent(1));
    }

    public async Task SyncOrdersAsync()
    {
        _log.LogInformation("SyncOrdersAsync started");
        await using var tx = await _db.Database.BeginTransactionAsync();

        var threshold = DateTime.UtcNow.AddHours(-3);
        var grouped = await _db.Orders
            .Where(o => o.ProcessedAt == null && o.CreatedAt <= threshold)
            .GroupBy(o => o.NmId)
            .Select(g => new { NmId = g.Key, Qty = g.Count() })
            .ToListAsync();

        if (!grouped.Any())
        {
            _log.LogInformation("No new orders to process");
            return;
        }

        var nmIds = grouped.Select(g => g.NmId).ToList();
        var cards = await _db.WbProductCards
            .AsNoTracking()
            .Where(c => nmIds.Contains(c.NmID) && !string.IsNullOrEmpty(c.VendorCode))
            .ToListAsync();

        var items = cards
            .Select(card => new ItemDto(
                Sid: card.VendorCode,
                Qty: grouped.First(g => g.NmId == card.NmID).Qty
            ))
            .ToList();

        if (!items.Any())
        {
            _log.LogWarning("No matching VendorCodes for NmIds: {NmIds}", string.Join(',', nmIds));
            return;
        }

        var sids = items.Select(i => i.Sid).ToList();

        var balances = await _db.Set<product>()
            .AsNoTracking()
            .Where(p => sids.Contains(p.sid.ToString()))
            .ToDictionaryAsync(p => p.sid.ToString(), p => p.balance ?? 0);

        var available = items
            .Where(i => balances.TryGetValue(i.Sid, out var bal) && bal >= i.Qty)
            .ToList();



        if (!available.Any())
        {
            _log.LogWarning("No items in stock to order (checked by balance)");
            return;
        }

        try
        {
            var response = await _connector.CreateOrderAsync(available);
            if (response.OrderId <= 0)
            {
                _log.LogWarning("Order was not created (unprocessable entity)");
                return;
            }

            _log.LogInformation("Order created successfully, id={OrderId}", response.OrderId);

            var now = DateTime.UtcNow;
            await _db.Orders
                .Where(o => o.ProcessedAt == null && nmIds.Contains(o.NmId))
                .ForEachAsync(o => o.ProcessedAt = now);
            await _db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Error creating order");
        }
    }
}

/// <summary>
/// Коннектор для SimaLand API v3
/// </summary>
public class SimaLandConnector : IOrderConnector
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _cfg;
    private readonly SettingsService _settings;
    private readonly ILogger<SimaLandConnector> _log;
    private readonly Task<int> _paymentTypeId;
    private readonly Task<int> _deliveryTypeId;
    private readonly QPlannerDbContext _db;
    private readonly IEventBus _bus;

    public SimaLandConnector(
        QPlannerDbContext db,
        IHttpClientFactory httpFactory,
        IConfiguration cfg,
        SettingsService settings,
        ILogger<SimaLandConnector> log,
        IEventBus bus)
    {
        _db = db;
        _httpFactory = httpFactory;
        _cfg = cfg;
        _settings = settings;
        _log = log;
        _paymentTypeId = settings.GetAsync<int>("SimaLand:PaymentTypeId");
        _deliveryTypeId =  settings.GetAsync<int>("SimaLand:DeliveryTypeId");
        _bus = bus;
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(List<ItemDto> items)
    {
   var token = _cfg.GetValue<string>("SimaLand:SyncToken");
    var userId = await _settings.GetAsync<int>("SimaLand:UserId");
    var comment = await _settings.GetAsync<string>("SimaLand:DefaultComment");
    var settlementId = await _settings.GetAsync("SimaLand:DefaultSettlementId");
    var pickupId = await _settings.GetAsync<int>("SimaLand:JpPickupId");

    using var client = new HttpClient { BaseAddress = new Uri("https://www.sima-land.ru/api/v3/") };
    client.DefaultRequestHeaders.Add("x-api-key", token);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

    var currentPurchase = await _db.Set<ActiveJpPurchase>()
        .OrderByDescending(x => x.CreatedAt)
        .FirstOrDefaultAsync(x => !x.IsClosed);

    int jpPurchaseId;

    if (currentPurchase != null)
    {
        jpPurchaseId = currentPurchase.JpPurchaseId;
        Console.WriteLine("[JP-PURCHASE] Reusing existing jp_purchase_id=" + jpPurchaseId);
    }
    else
    {
        var endTimeStr = _cfg.GetValue<string>("SimaLand:PurchaseEndTime");
        var endTime = TimeSpan.Parse(endTimeStr);
        var endedAt = DateTime.UtcNow.Date.Add(endTime).ToString("yyyy-MM-dd HH:mm:ss");

        var createPurchaseResp = await client.PostAsJsonAsync("jp-purchase/", new
        {
            user_id = userId,
            jp_status_id = 1,
            ended_at = endedAt
        });
        var createPurchaseContent = await createPurchaseResp.Content.ReadAsStringAsync();
        Console.WriteLine("[JP-PURCHASE] Status=" + createPurchaseResp.StatusCode);
        Console.WriteLine("[JP-PURCHASE] Response=" + createPurchaseContent);

        if (!createPurchaseResp.IsSuccessStatusCode)
        {
            try
            {
                using var tryDoc = JsonDocument.Parse(createPurchaseContent);
                if (tryDoc.RootElement.TryGetProperty("id", out var idProp))
                {
                    jpPurchaseId = idProp.GetInt32();
                    _db.Add(new ActiveJpPurchase
                    {
                        JpPurchaseId = jpPurchaseId,
                        CreatedAt = DateTime.UtcNow,
                        IsClosed = false
                    });
                    await _db.SaveChangesAsync();
                }
                else
                {
                    _log.LogWarning("Failed to create jp-purchase and no id found in response");
                    return new CreateOrderResponse { OrderId = 0 };
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error parsing failed jp-purchase response");
                return new CreateOrderResponse { OrderId = 0 };
            }
        }
        else
        {
            using var purchaseDoc = JsonDocument.Parse(createPurchaseContent);
            jpPurchaseId = purchaseDoc.RootElement.GetProperty("id").GetInt32();
            _db.Add(new ActiveJpPurchase
            {
                JpPurchaseId = jpPurchaseId,
                CreatedAt = DateTime.UtcNow,
                IsClosed = false
            });
            await _db.SaveChangesAsync();
        }
    }

    var sidSet = items.Select(i => i.Sid).ToList();
    var products = await _db.Set<product>()
        .Where(p => sidSet.Contains(p.sid.ToString()))
        .ToDictionaryAsync(p => p.sid.ToString(), p => new {
            MinQty = p.qty_multiplier ?? 1,
            Balance = p.balance ?? 0
        });

    var filteredItems = items
        .Select(i => new {
            Sid = i.Sid,
            Qty = (int)Math.Ceiling((double)i.Qty / products[i.Sid].MinQty) * products[i.Sid].MinQty
        })
        .Where(x => products[x.Sid].Balance >= x.Qty && products[x.Sid].Balance > 0)
        .ToList();

    if (!filteredItems.Any())
    {
        _log.LogWarning("No valid items for jp-request");
        return new CreateOrderResponse { OrderId = 0 };
    }

    var payload = new
    {
        items_data = filteredItems.Select(i => new { item_sid = i.Sid, qty = i.Qty }).ToArray(),
        contact_name = await _settings.GetAsync<string>("SimaLand:ContactName"),
        contact_email = await _settings.GetAsync<string>("SimaLand:ContactEmail"),
        contact_phone = await _settings.GetAsync<string>("SimaLand:ContactPhone"),
        settlement_id = 27503892,
        jp_purchase_id = jpPurchaseId,
        comment = comment
    };

    var response = await client.PostAsJsonAsync("order/checkout-jp-request-by-products/", payload);
    var content = await response.Content.ReadAsStringAsync();
    Console.WriteLine("[JP-REQUEST] Status=" + response.StatusCode);
    Console.WriteLine("[JP-REQUEST] Response=" + content);
    _log.LogInformation("[JP-REQUEST] Status={Status}, Response={Response}", response.StatusCode, content);

    if (!response.IsSuccessStatusCode)
        return new CreateOrderResponse { OrderId = 0 };

    using var orderDoc = JsonDocument.Parse(content);
    var orderId = orderDoc.RootElement.GetProperty("jp_order").GetProperty("order_id").GetInt32();

    _log.LogInformation("Order created successfully, id={OrderId}", orderId);
    await _bus.PublishAsync(new OrderCreatedEvent(orderId));

    return new CreateOrderResponse { OrderId = orderId };


    }
}