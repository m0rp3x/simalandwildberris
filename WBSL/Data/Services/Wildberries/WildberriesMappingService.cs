using System.Collections;
using System.Globalization;
using Shared;
using WBSL.Models;

namespace WBSL.Data.Services.Wildberries;

public class WildberriesMappingService
{
    public List<WbCreateVariantInternalDto> BuildProductsFromMapping(
        CategoryMappingRequest request,
        List<product> simaProducts
    ){
        var result = new List<WbCreateVariantInternalDto>();

        var categoryId = request.WildberriesCategoryId;
        var mappings = request.Mappings;

        foreach (var sima in simaProducts){
            var product = new WbCreateVariantInternalDto{
                SubjectID = categoryId,
                Dimensions = new WbDimensionsDtoToApi(),
                Characteristics = new List<WbCharacteristicDto>(),
                Sizes = new List<WbsizeDto>(),
                VendorCode = sima.sid.ToString()
            };

            foreach (var mapping in mappings){
                var value = ExtractValue(sima, mapping.SourceProperty);

                switch (mapping.Type){
                    case FieldMappingType.Text:
                        if (mapping.WbFieldName == "Title"){
                            var title = value?.ToString() ?? "";
                            product.Title = title.Length > 60 ? title.Substring(0, 60) : title;
                        }
                        else if (mapping.WbFieldName == "Description"){
                            var desc = value?.ToString() ?? "";
                            desc = desc.Replace("&nbsp;", " ").Replace("\u00A0", " ");
                            product.Description = desc;
                        }
                        else if (mapping.WbFieldName == "Brand") product.Brand = value?.ToString() ?? "";

                        break;

                    case FieldMappingType.Dimension:
                        switch (mapping.WbFieldName){
                            case "Length":
                                product.Dimensions.Length = TryParseInt(value);
                                break;
                            case "Width":
                                product.Dimensions.Width = TryParseInt(value);
                                break;
                            case "Height":
                                product.Dimensions.Height = TryParseInt(value);
                                break;
                            case "Weight":
                                var grams = TryParseDecimal(value);
                                product.Dimensions.WeightBrutto = grams / 1000m;
                                break;
                        }

                        break;

                    case FieldMappingType.Characteristic:
                        var charId = ExtractCharId(mapping.WbFieldName);
                        var parsedValue = ParseCharValue(value);
                        string rawName = mapping.SourceProperty ?? "";
                        string characteristicName;

                        if (rawName.StartsWith("Attr_", StringComparison.OrdinalIgnoreCase) && rawName.Length > 5){
                            characteristicName = rawName.Substring(5);
                        }
                        else{
                            characteristicName = rawName;
                        }

                        parsedValue = SubstituteCharacteristicValue(parsedValue, characteristicName,
                            request.CharacteristicValueMappings);

                        if (mapping.CharacteristicDataType != null){
                            parsedValue = ConvertValueToExpectedType(
                                parsedValue,
                                mapping.CharacteristicDataType.Value,
                                charId,
                                mapping.MaxCount
                            );
                        }

                        if (!IsEmpty(parsedValue)){
                            product.Characteristics.Add(new WbCharacteristicDto{
                                Id = charId,
                                Name = mapping.DisplayName,
                                Value = parsedValue
                            });
                        }

                        break;
                }
            }

            result.Add(product);
        }

        return result;
    }

    private object? ExtractValue(product sima, string source){
        if (source.StartsWith("Attr_")){
            var attrName = source.Replace("Attr_", "");

            var values = sima.product_attributes
                .Where(a => a.attr_name == attrName)
                .Select(a => a.value_text?.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Distinct()
                .Take(3)
                .ToList();

            if (values.Count == 0)
                return null;

            if (values.Count == 1)
                return values[0]!; // вернём строку, не список

            return values; // вернём список строк
        }

        var prop = typeof(product).GetProperty(source);
        return prop?.GetValue(sima);
    }

    private object ConvertValueToExpectedType(
        object value,
        WbCharacteristicDataType dataType,
        int charId,
        int? maxCount)
    {
        bool isList = maxCount.HasValue && maxCount.Value > 1;

        switch (dataType)
        {
            case WbCharacteristicDataType.String:
                return ConvertToStrings(value, isList);

            case WbCharacteristicDataType.Number:
                return ConvertToNumbers(value, isList, charId);

            default:
                return value;
        }
    }

    // -------------------- Вспомогательные методы --------------------

    private static object ConvertToStrings(object value, bool isList){
        if (isList){
            // Если передали IEnumerable (но не string) — приводим каждый элемент
            if (value is IEnumerable raw && !(value is string)){
                return raw
                    .Cast<object>()
                    .Select(o => o?.ToString() ?? "")
                    .ToList();
            }

            // Иначе — одиночный элемент упаковываем в список
            return new List<string>{ value?.ToString() ?? "" };
        }
        else{
            // Для одиночного берём первый не-пустой из коллекции (если это IEnumerable)
            if (value is IEnumerable raw && !(value is string)){
                return raw
                           .Cast<object>()
                           .Select(o => o?.ToString() ?? "")
                           .FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                       ?? "";
            }

            // Иначе — просто строка
            return value?.ToString() ?? "";
        }
    }

    private static object ConvertToNumbers(object value, bool isList, int charId){
        bool isSpecialDecimal = charId == 90673 || charId == 90675;

        if (isList){
            // Получаем последовательность объектов (или одиночный в виде одного элемента)
            var seq = (value is IEnumerable raw && !(value is string))
                ? raw.Cast<object>()
                : new[]{ value };

            if (isSpecialDecimal){
                // [ decimal ], округлённые до 1 знака
                return seq
                    .Select(ToDecimalRound1)
                    .ToList();
            }

            return seq
                .Select(ToInt)
                .ToList();
        }

        return isSpecialDecimal
            ? (object)ToDecimalRound1(value)
            : ToInt(value);
    }

    private static decimal ToDecimalRound1(object v){
        decimal d = v switch{
            decimal x => x,
            double x => (decimal)x,
            int x => x,
            long x => x,
            string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var z)
                => z,
            _ => 0m
        };
        return Math.Round(d, 1);
    }

    private static int ToInt(object v){
        // Парсим в decimal и округляем до целого
        decimal d = v switch{
            decimal x => x,
            double x => (decimal)x,
            int x => x,
            long x => x,
            string s when decimal.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var z)
                => z,
            _ => 0m
        };
        return (int)Math.Round(d);
    }

    private object SubstituteCharacteristicValue(object value, string characteristicName,
        List<CharacteristicValueMapping> substitutions){
        if (value is string sValue){
            var mapping = substitutions
                .FirstOrDefault(x =>
                    x.CharacteristicName.Equals(characteristicName, StringComparison.OrdinalIgnoreCase) &&
                    x.SimalandValue.Equals(sValue, StringComparison.OrdinalIgnoreCase));

            // Берём WildberriesValue из mapping, если оно не пустое; иначе – исходное
            var mapped = mapping?.WildberriesValue;
            return mapping != null ? mapping.WildberriesValue ?? "" : sValue;
        }

        if (value is List<string> list){
            return list.Select(item => {
                var mapping = substitutions
                    .FirstOrDefault(x =>
                        x.CharacteristicName.Equals(characteristicName, StringComparison.OrdinalIgnoreCase) &&
                        x.SimalandValue.Equals(item, StringComparison.OrdinalIgnoreCase));

                return mapping != null ? mapping.WildberriesValue : item;
            }).ToList();
        }

        return value;
    }

    private int TryParseInt(object? val){
        if (val == null) return 0;

        try{
            return Convert.ToInt32(val);
        }
        catch{
            return int.TryParse(val.ToString(), out var i) ? i : 0;
        }
    }

    private bool IsEmpty(object? value){
        if (value == null) return true;

        return value switch{
            string s => string.IsNullOrWhiteSpace(s),

            IEnumerable<string> list => !list.Any(x => !string.IsNullOrWhiteSpace(x)),

            _ => false
        };
    }

    private decimal TryParseDecimal(object? val){
        if (val == null) return 0;

        try{
            return Convert.ToDecimal(val);
        }
        catch{
            return decimal.TryParse(val.ToString(), out var i) ? i : 0;
        }
    }

    private int ExtractCharId(string wbFieldName){
        return wbFieldName.StartsWith("Char_") && int.TryParse(wbFieldName.Replace("Char_", ""), out var id)
            ? id
            : 0;
    }

    private object ParseCharValue(object? raw){
        switch (raw){
            case null:
                return "";

            case List<string> list:
                var cleaned = list
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToList();

                return cleaned.Count > 1 ? cleaned : cleaned.FirstOrDefault() ?? "";

            case string str:
                var parts = str
                    .Split(new[]{ ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Take(3)
                    .ToList();

                return parts.Count > 1 ? parts : parts.FirstOrDefault() ?? "";

            default:
                return raw.ToString() ?? "";
        }
    }
}