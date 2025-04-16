using System.Net.Http.Json;
using System.Text.Json;
using Shared;
using Microsoft.JSInterop;
using MudBlazor;

namespace WBSL.Client.Data.Services;

public class WbProductService
{
    private readonly HttpClient _client;
    private readonly ISnackbar _snackbar;
    private readonly ProductMappingService _mappingService;
    private readonly IJSRuntime _js;

    public WbProductService(HttpClient client, ISnackbar snackbar, ProductMappingService mappingService, IJSRuntime js){
        _client = client;
        _snackbar = snackbar;
        _mappingService = mappingService;
        _js = js;
    }

    public async Task<List<ExternalAccount>> LoadAccountsAsync(){
        return await _client.GetFromJsonAsync<List<ExternalAccount>>("api/accounts") ?? new();
    }

    public async Task<WbItemApiResponse?> SearchProductAsync(string vendorCode, ExternalAccount wbAccount,
        ExternalAccount simaAccount){
        if (string.IsNullOrWhiteSpace(vendorCode)){
            _snackbar.Add("Введите артикул товара", Severity.Warning);
            return null;
        }

        try{
            _snackbar.Add("Ищем товар...", Severity.Info);
            var response =
                await _client.GetFromJsonAsync<WbItemApiResponse>(
                    $"api/Wildberries/wbItem/{vendorCode}/{simaAccount.Id}/{wbAccount.Id}");

            if (response is null || response.wbProduct is null){
                throw new Exception("Товар не найден");
            }

            _snackbar.Add("Товар найден!", Severity.Success);
            return response;
        }
        catch (Exception ex){
            _snackbar.Add($"Ошибка: {ex.Message}", Severity.Error);
            return null;
        }
    }
    
    
    public async Task<ProductCheckResponse?> CheckSimalandAndWbProductAsync(string vendorCode, ExternalAccount wbAccount,
        ExternalAccount simaAccount){
        if (string.IsNullOrWhiteSpace(vendorCode)){
            _snackbar.Add("Введите артикул товара", Severity.Warning);
            return null;
        }

        try{
            _snackbar.Add("Ищем товар...", Severity.Info);
            var response =
                await _client.GetFromJsonAsync<ProductCheckResponse>(
                    $"api/Wildberries/checkWbAndSimaland/{vendorCode}/{simaAccount.Id}/{wbAccount.Id}");

            if (response is null){
                throw new Exception("Товар не найден");
            }

            if (!response.IsNullFromWb){
                _snackbar.Add("Товар найден в Wildberries", Severity.Warning);
            }

            _snackbar.Add("Товар найден!", Severity.Success);
            return response;
        }
        catch (Exception ex){
            _snackbar.Add($"Ошибка: {ex.Message}", Severity.Error);
            return null;
        }
    }

    public async Task<bool> CreateItemsAsync(List<WbProductCardDto> products, int wbAccountId){
        var response = await _client.PostAsJsonAsync($"api/Wildberries/createWbItem/{wbAccountId}", products);
        var result = await response.Content.ReadFromJsonAsync<WbApiResult>();

        if (result is null){
            _snackbar.Add("Не удалось получить ответ от сервера", Severity.Error);
            return false;
        }

        if (result.Error){
            _snackbar.Add(result.ErrorText ?? "Произошла ошибка", Severity.Error);

            if (result.AdditionalErrors is JsonElement json && json.ValueKind == JsonValueKind.Object){
                foreach (var prop in json.EnumerateObject()){
                    _snackbar.Add($"{prop.Name}: {prop.Value}", Severity.Warning);
                }
            }

            return false;
        }

        _snackbar.Add("Товары успешно обновлены", Severity.Success);
        return true;
    }
    
    public async Task<bool> CreateItemsAsync(List<WbCreateVariantInternalDto> products, int wbAccountId){
        var response = await _client.PostAsJsonAsync($"api/Wildberries/createWbItem/{wbAccountId}", products);
        var result = await response.Content.ReadFromJsonAsync<WbApiResult>();

        if (result is null){
            _snackbar.Add("Не удалось получить ответ от сервера", Severity.Error);
            return false;
        }

        if (result.Error){
            _snackbar.Add(result.ErrorText ?? "Произошла ошибка", Severity.Error);

            if (result.AdditionalErrors is JsonElement json && json.ValueKind == JsonValueKind.Object){
                foreach (var prop in json.EnumerateObject()){
                    _snackbar.Add($"{prop.Name}: {prop.Value}", Severity.Warning);
                }
            }

            return false;
        }

        _snackbar.Add("Товары успешно обновлены", Severity.Success);
        return true;
    }

    public async Task<bool> UpdateItemsAsync(List<WbProductCardDto> products, Guid wbAccountId){
        var response = await _client.PostAsJsonAsync($"api/Wildberries/updateWbItem/{wbAccountId}", products);
        var result = await response.Content.ReadFromJsonAsync<WbApiResult>();

        if (result is null){
            _snackbar.Add("Не удалось получить ответ от сервера", Severity.Error);
            return false;
        }

        if (result.Error){
            _snackbar.Add(result.ErrorText ?? "Произошла ошибка", Severity.Error);

            if (result.AdditionalErrors is JsonElement json && json.ValueKind == JsonValueKind.Object){
                foreach (var prop in json.EnumerateObject()){
                    _snackbar.Add($"{prop.Name}: {prop.Value}", Severity.Warning);
                }
            }

            return false;
        }

        _snackbar.Add("Товары успешно обновлены", Severity.Success);
        return true;
    }

    public bool ValidateMappings(List<PropertyMapping>? mappings, out List<string> errors){
        errors = mappings
            .Where(m => m.IsRequired && (string.IsNullOrWhiteSpace(m.SimaLandValue) || m.SimaLandValue.Trim() == "0"))
            .Select(m => m.PropertyName)
            .Distinct()
            .Select(name => $"Не заполнено обязательное поле: {name}")
            .ToList();

        return !errors.Any();
    }

    public async Task SaveMappingTemplateAsync(List<PropertyMappingTemplate> template){
        var json = JsonSerializer.Serialize(template);
        await _js.InvokeVoidAsync("mappingStorage.saveMapping", "wb_mapping_template", json);
        _snackbar.Add("Шаблон маппинга сохранён в localStorage ✅", Severity.Success);
    }

    public async Task<List<PropertyMappingTemplate>> LoadMappingTemplateAsync(){
        var json = await _js.InvokeAsync<string>("mappingStorage.loadMapping", "wb_mapping_template");
        if (string.IsNullOrWhiteSpace(json))
            return new();

        try{
            return JsonSerializer.Deserialize<List<PropertyMappingTemplate>>(json) ?? new();
        }
        catch{
            _snackbar.Add("⚠️ Ошибка при чтении шаблона маппинга", Severity.Warning);
            return new();
        }
    }
}