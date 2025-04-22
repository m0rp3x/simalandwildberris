using System.Text.Json;
using Microsoft.JSInterop;
using Shared;
using Shared.FieldInfos;

namespace WBSL.Client.Data.Helpers;
/// <summary>
/// Вспомогательные методы для маппинга полей между продуктами SimaLand и Wildberries.
/// </summary>
public static class FieldMappingHelper
{
    public static List<FieldMapping> Create(){
        
        var Mappings = new List<FieldMapping>
        {
            new() { WbFieldName = "VendorCode", DisplayName = "Артикуль", Type = FieldMappingType.Text },
            new() { WbFieldName = "Title", DisplayName = "Название", Type = FieldMappingType.Text },
            new() { WbFieldName = "Description", DisplayName = "Описание", Type = FieldMappingType.Text },
            new() { WbFieldName = "Brand", DisplayName = "Бренд", Type = FieldMappingType.Text },
            new() { WbFieldName = "Length", DisplayName = "Длина, см", Type = FieldMappingType.Dimension },
            new() { WbFieldName = "Width", DisplayName = "Ширина, см", Type = FieldMappingType.Dimension },
            new() { WbFieldName = "Height", DisplayName = "Высота, см", Type = FieldMappingType.Dimension },
            new() { WbFieldName = "Weight", DisplayName = "Вес, кг", Type = FieldMappingType.Dimension },
        };
        return Mappings;
    }
    /// <summary>
    /// Назначает или обновляет маппинг поля WB с соответствующим полем или атрибутом из SimaLand.
    /// При наличии fieldInfo — сразу применяет значение.
    /// </summary>
    /// <param name="fieldName">Имя поля Wildberries (например, "Title" или "Char_1234").</param>
    /// <param name="simaFieldName">Имя поля из SimaLand, либо Attr_ID (например, "name" или "Attr_456").</param>
    /// <param name="Mappings">Коллекция текущих маппингов, куда будет добавлен или обновлён элемент.</param>
    /// <param name="simalandProduct">Объект продукта SimaLand, из которого берётся значение.</param>
    /// <param name="characteristicInfos">Список всех характеристик WB для правильного разбора значений.</param>
    /// <param name="fieldInfo">Поле WB, которому нужно установить значение (если передано).</param>
    // public static void SetMapping(string fieldName, string simaFieldName, List<FieldMapping> Mappings, SimalandProductDto simalandProduct ,List<WbAdditionalCharacteristicDto> characteristicInfos, WbFieldInfo? fieldInfo = null){
    //     if (simaFieldName == "Attr_0" || simaFieldName == "0" || string.IsNullOrWhiteSpace(simaFieldName))
    //     {
    //         Mappings.RemoveAll(m => m.FieldName == fieldName);
    //         return;
    //     }
    //     var mapping = Mappings.FirstOrDefault(m => m.FieldName == fieldName);
    //     if (mapping != null)
    //         mapping.SourceProperty = simaFieldName;
    //     else
    //         Mappings.Add(new FieldMapping{ FieldName = fieldName, SourceProperty = simaFieldName });
    //
    //     if (fieldInfo != null){
    //         object? val = null;
    //
    //         if (simaFieldName.StartsWith("Attr_")){
    //             if (int.TryParse(simaFieldName.Replace("Attr_", ""), out var attrId)){
    //                 var attr = simalandProduct.Attributes.FirstOrDefault(a => a.id == attrId);
    //                 if (attr != null){
    //                     val = ParseAttribute(attr.value_text, fieldInfo, characteristicInfos);
    //                 }
    //             }
    //         }
    //         else{
    //             var simaProp = typeof(SimalandProductDto).GetProperty(simaFieldName);
    //             if (simaProp != null){
    //                 val = simaProp.GetValue(simalandProduct);
    //             }
    //         }
    //
    //         if (val != null){
    //             fieldInfo.Setter(val);
    //         }
    //     }
    // }
    
    /// <summary>
    /// Применяет все маппинги к полям Wildberries на основе значений из SimaLand.
    /// </summary>
    /// <param name="mappings">Список сохранённых маппингов (FieldName → SourceProperty).</param>
    /// <param name="wbFields">Список полей WB (в том числе характеристик), в которые будет установлено значение.</param>
    /// <param name="sima">Объект продукта SimaLand, содержащий поля и атрибуты.</param>
    /// <param name="charcInfo">Список характеристик WB для корректного разбора типов и ограничений.</param>
    // public static void ApplyMappings(List<FieldMapping> mappings, List<WbFieldInfo> wbFields, SimalandProductDto sima,
    //     List<WbAdditionalCharacteristicDto> charcInfo){
    //     foreach (var map in mappings){
    //         var field = wbFields.FirstOrDefault(f => f.FieldName == map.FieldName);
    //         if (field == null) continue;
    //
    //         object? value = null;
    //
    //         if (map.SourceProperty.StartsWith("Attr_") &&
    //             int.TryParse(map.SourceProperty.Replace("Attr_", ""), out var attrId)){
    //             var attr = sima.Attributes.FirstOrDefault(a => a.id == attrId);
    //             if (attr != null){
    //                 value = ParseAttribute(attr.value_text, field, charcInfo);
    //             }
    //         }
    //         else{
    //             var prop = typeof(SimalandProductDto).GetProperty(map.SourceProperty);
    //             if (prop != null){
    //                 value = prop.GetValue(sima);
    //             }
    //         }
    //
    //         if (value != null)
    //             field.Setter(value);
    //     }
    // }

    /// <summary>
    /// Добавляет в список полей характеристики Wildberries как WbFieldInfo.
    /// Удаляет предыдущие поля с IsCharacteristic = true перед добавлением.
    /// </summary>
    /// <param name="wbFields">Целевой список полей, в который будут добавлены характеристики.</param>
    /// <param name="characteristics">Значения характеристик WB (id, name, value).</param>
    /// <param name="characteristicInfos">Метаданные характеристик (MaxCount, CharcType и др).</param>
    public static void MergeCharacteristicMappings(
        List<FieldMapping> mappings,
        List<WbAdditionalCharacteristicDto> currentCharacteristics)
    {
        var currentFieldNames = currentCharacteristics
            .Select(c => $"Char_{c.CharcID}")
            .ToHashSet();

        // Удалить все "Char_*", которых нет в текущей категории
        mappings.RemoveAll(m =>
            m.Type == FieldMappingType.Characteristic &&
            !currentFieldNames.Contains(m.WbFieldName));

        // Добавить недостающие
        var overrideMaxCount = new Dictionary<int, int>
        {
            { 90630, 1 },
            { 90673, 1 }
        };
        foreach (var charInfo in currentCharacteristics)
        {
            var fieldName = $"Char_{charInfo.CharcID}";
            var maxCount = overrideMaxCount.TryGetValue(charInfo.CharcID, out var overriddenMax)
                ? overriddenMax
                : charInfo.MaxCount;
            
            var existing = mappings.FirstOrDefault(m => m.WbFieldName == fieldName);

            var dataType = InferCharacteristicDataType(charInfo.CharcType, charInfo.MaxCount);
            
            if (existing != null)
            {
                existing.CharacteristicDataType = dataType;
                existing.MaxCount = maxCount;
            }
            else
            {
                mappings.Add(new FieldMapping
                {
                    WbFieldName = fieldName,
                    DisplayName = charInfo.Name,
                    Type = FieldMappingType.Characteristic,
                    SourceProperty = "",
                    CharacteristicDataType = dataType,
                    MaxCount = maxCount
                });
            }
        }
    }
    
    public static void SanitizeMappings(
        List<FieldMapping> mappings,
        List<SimalandAttributeDto> currentSimalandAttrs)
    {
        var validAttrNames = new HashSet<string>(
            currentSimalandAttrs.Select(a => a.Name)
        );
        foreach (var m in mappings)
        {
            if (m.Type == FieldMappingType.Characteristic &&
                m.SourceProperty.StartsWith("Attr_"))
            {
                var attrName = m.SourceProperty.Substring("Attr_".Length);

                if (!validAttrNames.Contains(attrName))
                {
                    m.SourceProperty = ""; // ✂️ затираем, если такого атрибута уже нет
                }
            }
        }
    }

    
    public static async Task SaveOrUpdateMappingsAsync(IJSRuntime js, List<FieldMapping> currentCategoryMappings)
    {
        var allJson = await js.InvokeAsync<string>("localStorage.getItem", "fieldMappings");

        var allMappings = !string.IsNullOrWhiteSpace(allJson)
            ? JsonSerializer.Deserialize<List<FieldMapping>>(allJson) ?? new()
            : new List<FieldMapping>();

        foreach (var current in currentCategoryMappings)
        {
            var existing = allMappings.FirstOrDefault(m => m.WbFieldName == current.WbFieldName);
            if (existing != null)
                existing.SourceProperty = current.SourceProperty;
            else
                allMappings.Add(current);
        }

        var updatedJson = JsonSerializer.Serialize(allMappings);
        await js.InvokeVoidAsync("localStorage.setItem", "fieldMappings", updatedJson);
    }
    
    public static async Task<List<FieldMapping>> LoadFromLocalStorageAsync(IJSRuntime js)
    {
        var json = await js.InvokeAsync<string>("localStorage.getItem", "fieldMappings");

        var defaultMappings = Create();

        if (string.IsNullOrWhiteSpace(json))
            return defaultMappings;

        var loaded = JsonSerializer.Deserialize<List<FieldMapping>>(json) ?? new();

        // Сопоставляем значения, но оставляем DisplayName и Type из дефолтных
        foreach (var def in defaultMappings)
        {
            var saved = loaded.FirstOrDefault(x => x.WbFieldName == def.WbFieldName);
            if (saved != null)
            {
                def.SourceProperty = saved.SourceProperty;
            }
        }
        var additional = loaded
            .Where(l => defaultMappings.All(d => d.WbFieldName != l.WbFieldName))
            .ToList();
        
        defaultMappings.AddRange(additional);
        
        return defaultMappings;
    }
    
    private static WbCharacteristicDataType? InferCharacteristicDataType(int charcType, int maxCount)
    {
        // Строка
        if (charcType == 0 || charcType == 1)
            return WbCharacteristicDataType.String;

        // Число
        if (charcType == 4)
            return WbCharacteristicDataType.Number;

        return null; // если вдруг не определили
    }

}