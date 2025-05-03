using System.Text.Json;
using Microsoft.JSInterop;
using Shared;

namespace WBSL.Client.Data.Helpers;

public static class CharacteristicMappingHelper
{
    private const string StorageKey = "characteristicValueMappings";

    public static async Task<List<CharacteristicValueMapping>> LoadFromLocalStorageAsync(IJSRuntime js)
    {
        var json = await js.InvokeAsync<string>("localStorage.getItem", StorageKey);

        return string.IsNullOrWhiteSpace(json)
            ? new List<CharacteristicValueMapping>()
            : JsonSerializer.Deserialize<List<CharacteristicValueMapping>>(json) ?? new();
    }

    public static async Task SaveToLocalStorageAsync(IJSRuntime js, List<CharacteristicValueMapping> mappings)
    {
        var json = JsonSerializer.Serialize(mappings);
        await js.InvokeVoidAsync("localStorage.setItem", StorageKey, json);
    }

    public static async Task UpsertMappingAsync(
        IJSRuntime js,
        CharacteristicValueMapping newMapping)
    {
        var existing = await LoadFromLocalStorageAsync(js);

        var match = existing.FirstOrDefault(x =>
            x.CharacteristicName == newMapping.CharacteristicName &&
            x.SimalandValue == newMapping.SimalandValue);

        if (match != null)
        {
            match.WildberriesValue = newMapping.WildberriesValue;
        }
        else
        {
            existing.Add(newMapping);
        }

        await SaveToLocalStorageAsync(js, existing);
    }

    public static void MergeWithSavedMappings(
        List<CharacteristicValueMapping> generated,
        List<CharacteristicValueMapping> saved)
    {
        foreach (var gen in generated)
        {
            var match = saved.FirstOrDefault(x =>
                x.CharacteristicName == gen.CharacteristicName &&
                x.SimalandValue == gen.SimalandValue);

            if (match != null)
            {
                gen.WildberriesValue = match.WildberriesValue;
            }
        }
    }
}