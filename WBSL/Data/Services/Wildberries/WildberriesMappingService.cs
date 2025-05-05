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
                        if (mapping.WbFieldName == "Title") 
                        {
                            var title = value?.ToString() ?? "";
                            product.Title = title.Length > 60 ? title.Substring(0, 60) : title;
                        }
                        else if (mapping.WbFieldName == "Description")
                        {
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
                        
                        if (rawName.StartsWith("Attr_", StringComparison.OrdinalIgnoreCase) && rawName.Length > 5)
                        {
                            characteristicName = rawName.Substring(5);
                        }
                        else
                        {
                            characteristicName = rawName;
                        }
                        parsedValue = SubstituteCharacteristicValue(parsedValue, characteristicName,
                            request.CharacteristicValueMappings);

                        if (mapping.CharacteristicDataType != null){
                            parsedValue = ConvertValueToExpectedType(
                                parsedValue,
                                mapping.CharacteristicDataType.Value,
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
        if (source.StartsWith("Attr_"))
        {
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

    private object ConvertValueToExpectedType(object value, WbCharacteristicDataType dataType, int? maxCount){
        bool isList = maxCount.HasValue && maxCount.Value > 1;

        switch (dataType){
            case WbCharacteristicDataType.String:
                if (isList){
                    if (value is List<string> strList) return strList;
                    return new List<string>{ value.ToString() ?? "" };
                }
                else{
                    return value.ToString() ?? "";
                }

            case WbCharacteristicDataType.Number:
                if (isList){
                    if (value is List<string> rawList){
                        var numbers = rawList
                            .Select(v => int.TryParse(v, out var d) ? (int?)d : null)
                            .Where(d => d.HasValue)
                            .Select(d => d.Value)
                            .ToList();
                        return numbers;
                    }
                    else if (int.TryParse(value.ToString(), out var singleIntList))
                    {
                        return new List<int> { singleIntList };
                    }
                    else if (decimal.TryParse(value.ToString(), out var dVal))
                    {
                        return new List<int> { (int)Math.Round(dVal) };
                    }
                }
                else{
                    if (int.TryParse(value.ToString(), out var singleInt))
                        return singleInt;
                    else if (decimal.TryParse(value.ToString(), out var dVal))
                        return (int)Math.Round(dVal);
                }

                break;
        }

        return value; // fallback, если не удалось преобразовать
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
            return list.Select(item =>
            {
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

    private bool IsEmpty(object? value)
    {
        if (value == null) return true;

        return value switch
        {
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

    private object ParseCharValue(object? raw)
    {
        switch (raw)
        {
            case null:
                return "";

            case List<string> list:
                var cleaned = list
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToList();

                return cleaned.Count > 1 ? cleaned : cleaned.FirstOrDefault() ?? "";

            case string str:
                var parts = str
                    .Split(';', StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .ToList();

                return parts.Count > 1 ? parts : parts.FirstOrDefault() ?? "";

            default:
                return raw.ToString() ?? "";
        }
    }
}