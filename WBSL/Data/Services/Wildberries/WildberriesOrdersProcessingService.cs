using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Shared.Enums;
using WBSL.Data.Extensions;
using WBSL.Data.HttpClientFactoryExt;
using WBSL.Data.Models;
using WBSL.Data.Models.DTO;

namespace WBSL.Data.Services.Wildberries;

public class WildberriesOrdersProcessingService : WildberriesBaseService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlatformHttpClientFactory _httpFactory;
    private readonly ILogger<WildberriesOrdersProcessingService> _logger;

    public WildberriesOrdersProcessingService(
        IServiceScopeFactory scopeFactory,
        PlatformHttpClientFactory httpFactory,
        ILogger<WildberriesOrdersProcessingService> logger) : base(httpFactory){
        _scopeFactory = scopeFactory;
        _httpFactory = httpFactory;
        _logger = logger;
    }
    
    /// <summary>
    /// Основная точка входа для Hangfire.
    /// Гарантирует, что повторный запуск не будет происходить в течение 1 часа.
    /// </summary>
    [DisableConcurrentExecution(timeoutInSeconds: 60 * 60)]
    public async Task FetchAndSaveOrdersAsync()
    {
        List<OrderDto> orders;
        try
        {
            orders = await FetchOrdersAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при получении заказов из Wildberries");
            throw;
        }

        try
        {
            await SaveOrdersAsync(orders);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при сохранении заказов в базу");
            throw;
        }
    }
    
    /// <summary>
    /// Запрашивает у Wildberries список заказов и возвращает десериализованный DTO.
    /// </summary>
    private async Task<List<OrderDto>> FetchOrdersAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();

        // Получаем список Wildberries-аккаунтов
        var accountIds = await db.external_accounts
            .Where(x => x.platform == ExternalAccountType.Wildberries.ToString())
            .Select(x => x.id)
            .ToListAsync();

        if (accountIds.Count == 0)
            return new List<OrderDto>();
        
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var client = await _httpFactory.GetValidClientAsync(
            ExternalAccountType.WildBerriesMarketPlace,
            accountIds,
            "/ping",
            cts.Token);

        if (client == null)
            throw new InvalidOperationException("Не удалось получить валидный HttpClient для Wildberries");


        var response = await client.GetAsync($"/api/v3/orders/new", cts.Token);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);
        var wrapper = await JsonSerializer.DeserializeAsync<OrdersResponseDto>(
            stream,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            cts.Token);

        return wrapper?.Orders ?? new List<OrderDto>();
    }
    
    /// <summary>
    /// Сохраняет список заказов в базу, преобразуя DTO в сущности.
    /// </summary>
    private async Task SaveOrdersAsync(IEnumerable<OrderDto> orders)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<QPlannerDbContext>();

        foreach (var dto in orders)
        {
            // Здесь вы можете делать Upsert: либо Add, либо Update, если уже есть
            var existing = await db.Orders.FindAsync(dto.Id);
            if (existing != null)
            {
                // Обновляем поля
                existing.Price           = dto.Price;
                existing.ConvertedPrice  = dto.ConvertedPrice;
                existing.CurrencyCode    = dto.CurrencyCode;
                existing.ConvertedCurrencyCode = dto.ConvertedCurrencyCode;
                existing.IsZeroOrder     = dto.IsZeroOrder;
                // ... и другие поля по необходимости
                db.Orders.Update(existing);
            }
            else
            {
                // Создаём новую сущность
                var entity = new OrderEntity
                {
                    Id                      = dto.Id,
                    OrderUid                = dto.OrderUid,
                    Article                 = dto.Article,
                    CreatedAt               = dto.CreatedAt,
                    WarehouseId             = dto.WarehouseId,
                    NmId                    = dto.NmId,
                    ChrtId                  = dto.ChrtId,
                    Price                   = dto.Price,
                    ConvertedPrice          = dto.ConvertedPrice,
                    CurrencyCode            = dto.CurrencyCode,
                    ConvertedCurrencyCode   = dto.ConvertedCurrencyCode,
                    CargoType               = dto.CargoType,
                    IsZeroOrder             = dto.IsZeroOrder,
                    Offices                 = dto.Offices,
                    Skus                    = dto.Skus,
                    IsB2B                   = dto.Options.IsB2B
                };
                db.Orders.Add(entity);
            }
        }

        await db.SaveChangesAsync();
    }
}