using Shared;
using WBSL.Models;

namespace WBSL.Data.Services.Wildberries;

public class WildberriesMappingService
{
    public List<WbCreateVariantInternalDto> BuildProductsFromMapping(
        CategoryMappingRequest request,
        List<product> simaProducts
    )
    {
        var result = new List<WbCreateVariantInternalDto>();
        
        var categoryId = request.WildberriesCategoryId;
        var mappings = request.Mappings;
        
        foreach (var sima in simaProducts)
        {
            var product = new WbCreateVariantInternalDto
            {
                SubjectID = categoryId,
                Dimensions = new WbDimensionsDtoToApi(),
                Characteristics = new List<WbCharacteristicDto>(),
                Sizes = new List<WbsizeDto>(),
                VendorCode = sima.sid.ToString()
            };

            foreach (var mapping in mappings)
            {
                var value = ExtractValue(sima, mapping.SourceProperty);

                switch (mapping.Type)
                {
                    case FieldMappingType.Text:
                        if (mapping.WbFieldName == "Title") product.Title = value?.ToString() ?? "";
                        else if (mapping.WbFieldName == "Description") product.Description = value?.ToString() ?? "";
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
                                product.Dimensions.WeightBrutto = TryParseDecimal(value);
                                break;
                        }
                        break;

                    case FieldMappingType.Characteristic:
                        var charId = ExtractCharId(mapping.WbFieldName);
                        var parsedValue = ParseCharValue(value);
                        product.Characteristics.Add(new WbCharacteristicDto
                        {
                            Id = charId,
                            Name = mapping.DisplayName,
                            Value = parsedValue
                        });
                        break;
                }
            }

            result.Add(product);
        }

        return result;
    }
    
    private object? ExtractValue(product sima, string source)
    {
        if (source.StartsWith("Attr_"))
        {
            var attrName = source.Replace("Attr_", "");
            return sima.product_attributes.FirstOrDefault(a => a.attr_name == attrName)?.value_text;
        }

        var prop = typeof(product).GetProperty(source);
        return prop?.GetValue(sima);
    }

    private int TryParseInt(object? val)
    {
        if (val == null) return 0;

        try
        {
            return Convert.ToInt32(val);
        }
        catch
        {
            return int.TryParse(val.ToString(), out var i) ? i : 0;
        }
    }
    
    private decimal TryParseDecimal(object? val)
    {
        if (val == null) return 0;

        try
        {
            return Convert.ToDecimal(val);
        }
        catch
        {
            return decimal.TryParse(val.ToString(), out var i) ? i : 0;
        }
    }

    private int ExtractCharId(string wbFieldName)
    {
        return wbFieldName.StartsWith("Char_") && int.TryParse(wbFieldName.Replace("Char_", ""), out var id)
            ? id : 0;
    }

    private object ParseCharValue(object? raw)
    {
        var parts = raw?.ToString()?.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .ToList() ?? new();

        return parts.Count > 1 ? parts : parts.FirstOrDefault() ?? "";
    }

}