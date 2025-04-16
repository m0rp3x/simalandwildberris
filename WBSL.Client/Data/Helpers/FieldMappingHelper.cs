using Shared;
using Shared.FieldInfos;

namespace WBSL.Client.Data.Helpers;
/// <summary>
/// Вспомогательные методы для маппинга полей между продуктами SimaLand и Wildberries.
/// </summary>
public static class FieldMappingHelper
{
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
    public static void SetMapping(string fieldName, string simaFieldName, List<FieldMapping> Mappings, SimalandProductDto simalandProduct ,List<WbAdditionalCharacteristicDto> characteristicInfos, WbFieldInfo? fieldInfo = null){
        if (simaFieldName == "Attr_0" || simaFieldName == "0" || string.IsNullOrWhiteSpace(simaFieldName))
        {
            Mappings.RemoveAll(m => m.FieldName == fieldName);
            return;
        }
        var mapping = Mappings.FirstOrDefault(m => m.FieldName == fieldName);
        if (mapping != null)
            mapping.SourceProperty = simaFieldName;
        else
            Mappings.Add(new FieldMapping{ FieldName = fieldName, SourceProperty = simaFieldName });

        if (fieldInfo != null){
            object? val = null;

            if (simaFieldName.StartsWith("Attr_")){
                if (int.TryParse(simaFieldName.Replace("Attr_", ""), out var attrId)){
                    var attr = simalandProduct.Attributes.FirstOrDefault(a => a.id == attrId);
                    if (attr != null){
                        val = ParseAttribute(attr.value_text, fieldInfo, characteristicInfos);
                    }
                }
            }
            else{
                var simaProp = typeof(SimalandProductDto).GetProperty(simaFieldName);
                if (simaProp != null){
                    val = simaProp.GetValue(simalandProduct);
                }
            }

            if (val != null){
                fieldInfo.Setter(val);
            }
        }
    }
    
    /// <summary>
    /// Применяет все маппинги к полям Wildberries на основе значений из SimaLand.
    /// </summary>
    /// <param name="mappings">Список сохранённых маппингов (FieldName → SourceProperty).</param>
    /// <param name="wbFields">Список полей WB (в том числе характеристик), в которые будет установлено значение.</param>
    /// <param name="sima">Объект продукта SimaLand, содержащий поля и атрибуты.</param>
    /// <param name="charcInfo">Список характеристик WB для корректного разбора типов и ограничений.</param>
    public static void ApplyMappings(List<FieldMapping> mappings, List<WbFieldInfo> wbFields, SimalandProductDto sima,
        List<WbAdditionalCharacteristicDto> charcInfo){
        foreach (var map in mappings){
            var field = wbFields.FirstOrDefault(f => f.FieldName == map.FieldName);
            if (field == null) continue;

            object? value = null;

            if (map.SourceProperty.StartsWith("Attr_") &&
                int.TryParse(map.SourceProperty.Replace("Attr_", ""), out var attrId)){
                var attr = sima.Attributes.FirstOrDefault(a => a.id == attrId);
                if (attr != null){
                    value = ParseAttribute(attr.value_text, field, charcInfo);
                }
            }
            else{
                var prop = typeof(SimalandProductDto).GetProperty(map.SourceProperty);
                if (prop != null){
                    value = prop.GetValue(sima);
                }
            }

            if (value != null)
                field.Setter(value);
        }
    }

    /// <summary>
    /// Добавляет в список полей характеристики Wildberries как WbFieldInfo.
    /// Удаляет предыдущие поля с IsCharacteristic = true перед добавлением.
    /// </summary>
    /// <param name="wbFields">Целевой список полей, в который будут добавлены характеристики.</param>
    /// <param name="characteristics">Значения характеристик WB (id, name, value).</param>
    /// <param name="characteristicInfos">Метаданные характеристик (MaxCount, CharcType и др).</param>
    public static void AppendCharacteristicFields(
        List<WbFieldInfo> wbFields,
        List<WbCharacteristicDto> characteristics,
        List<WbAdditionalCharacteristicDto> characteristicInfos){
        wbFields.RemoveAll(f => f.IsCharacteristic);

        var fields = characteristics.Select(charac => {
            var charcInfo = characteristicInfos.FirstOrDefault(x => x.CharcID == charac.Id);
            var displayName = charac.Name;
            if (charcInfo?.Required == true)
                displayName += " *";

            return new WbFieldInfo{
                FieldName = GetCharacteristicFieldName(charac.Id),
                DisplayName = displayName,
                GroupName = "Характеристики",
                Getter = () => charac.Value,
                Setter = val => charac.Value = ConvertCharacValue(val, charac, characteristicInfos),
                IsCharacteristic = true
            };
        }).ToList();

        wbFields.AddRange(fields);
    }
    
    /// <summary>
    /// Форматирует значение поля в строку для отображения.
    /// </summary>
    /// <param name="value">Значение любого типа (строка, число, список).</param>
    /// <returns>Отформатированная строка.</returns>
    public static string FormatFieldValue(object? value){
        if (value == null) return "";

        return value switch{
            List<string> strList => string.Join(", ", strList),
            List<int> intList => string.Join(", ", intList),
            _ => value.ToString() ?? ""
        };
    }
    
    /// <summary>
    /// Парсит строковое значение из атрибута SimaLand в нужный формат (список или значение).
    /// </summary>
    /// <param name="rawValue">Сырое строковое значение из SimaLand (value_text).</param>
    /// <param name="field">Поле WB, для которого применяется значение.</param>
    /// <param name="charcInfo">Метаданные характеристик для определения типа и ограничения.</param>
    /// <returns>Результат в виде int, string, List&lt;int&gt;, List&lt;string&gt; и т.п.</returns>
    public static object ParseAttribute(string? rawValue, WbFieldInfo field, List<WbAdditionalCharacteristicDto> charcInfo){
        if (rawValue == null) return "";

        var parts = rawValue.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .ToList();

        var charId = ExtractCharId(field.FieldName);
        var charInfo = charcInfo.FirstOrDefault(x => x.CharcID == charId);
        bool isList = charInfo?.MaxCount != 1;

        if (charInfo?.CharcType == 4){
            var nums = parts.Select(x => int.TryParse(x, out var i) ? i : 0).ToList();
            return isList ? LimitList(nums, charInfo.MaxCount) : nums.FirstOrDefault();
        }

        return isList ? LimitList(parts, charInfo?.MaxCount ?? 0) : parts.FirstOrDefault() ?? "";
    }
    
    /// <summary>
    /// Проверяет, можно ли редактировать поле вручную.
    /// </summary>
    /// <param name="field">Поле WB.</param>
    /// <returns>true, если поле read-only (например, список характеристик), иначе false.</returns>
    public static bool IsFieldReadOnly(WbFieldInfo field){
        var value = field.Getter();

        switch (field.IsCharacteristic){
            case true when value is List<string>:
            case true when value is List<int>:
                return true;
            default:
                return false;
        }
    }
    
    private static string GetCharacteristicFieldName(int charId) => $"Char_{charId}";
    
    private static object ConvertCharacValue(object? val, WbCharacteristicDto charac, List<WbAdditionalCharacteristicDto> characteristicInfos){
        var info = characteristicInfos.FirstOrDefault(x => x.CharcID == charac.Id);
        if (info == null || val == null)
            return "";

        bool isList = info.MaxCount != 1;

        if (info.CharcType == 4) // число
        {
            if (isList)
                return val is List<int> ints ? LimitList(ints, info.MaxCount) : new List<int>();
            return int.TryParse(val?.ToString(), out var n) ? n : 0;
        }

        // строки
        if (isList)
            return val is List<string> strings ? LimitList(strings, info.MaxCount) : new List<string>();

        return val?.ToString() ?? "";
    }
    private static int ExtractCharId(string fieldName){
        if (fieldName.StartsWith("Char_") && int.TryParse(fieldName.Replace("Char_", ""), out var id))
            return id;
        return 0;
    }
    
    private static List<T> LimitList<T>(List<T> list, int max){
        return max > 0 ? list.Take(max).ToList() : list;
    }

}