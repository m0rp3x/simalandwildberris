using Shared;

namespace WBSL.Client.Data.Services;

public class ProductMappingService
{
    public List<PropertyMapping> GenerateMappings(WbProductCardDto wbProduct, SimalandProductDto? simaProduct = null)
    {
        var mappings = new List<PropertyMapping>();
        
        mappings.Add(new PropertyMapping
        {
            PropertyName = "Артикул продавца",
            WbValue = wbProduct.VendorCode,
            WbFieldName = nameof(WbProductCardDto.VendorCode),
            SimaLandFieldName = nameof(SimalandProductDto.sid),
            SimaLandValue = simaProduct?.sid.ToString() ?? "",
            IsSelectable = false
        });

        mappings.Add(new PropertyMapping
        {
            PropertyName = "Бренд",
            WbValue = wbProduct.Brand,
            WbFieldName = nameof(WbProductCardDto.Brand),
            SimaLandFieldName = nameof(SimalandProductDto.trademark_name),
            SimaLandValue = simaProduct?.trademark_name,
            IsSelectable = true
        });

        mappings.Add(new PropertyMapping
        {
            PropertyName = "Название",
            WbValue = wbProduct.Title,
            WbFieldName = nameof(WbProductCardDto.Title),
            SimaLandFieldName = nameof(SimalandProductDto.name),
            SimaLandValue = simaProduct?.name,
            IsSelectable = true
        });

        mappings.Add(new PropertyMapping
        {
            PropertyName = "Описание",
            WbValue = wbProduct.Description,
            WbFieldName = nameof(WbProductCardDto.Description),
            SimaLandFieldName = nameof(SimalandProductDto.description),
            SimaLandValue = simaProduct?.description,
            IsSelectable = true
        });

        // Габариты
        mappings.AddRange(new[]
        {
            new PropertyMapping
            {
                PropertyName = "Ширина",
                WbValue = wbProduct.Dimensions.Width.ToString(),
                WbFieldName = "Dimensions.Width",
                SimaLandFieldName = nameof(SimalandProductDto.width),
                SimaLandValue = simaProduct?.width.ToString(),
                IsSelectable = true
            },
            new PropertyMapping
            {
                PropertyName = "Высота",
                WbValue = wbProduct.Dimensions.Height.ToString(),
                WbFieldName = "Dimensions.Height",
                SimaLandFieldName = nameof(SimalandProductDto.height),
                SimaLandValue = simaProduct?.height.ToString(),
                IsSelectable = true
            },
            new PropertyMapping
            {
                PropertyName = "Длина",
                WbValue = wbProduct.Dimensions.Length.ToString(),
                WbFieldName = "Dimensions.Length",
                SimaLandFieldName = nameof(SimalandProductDto.depth),
                SimaLandValue = simaProduct?.depth.ToString(),
                IsSelectable = true
            },
            new PropertyMapping
            {
                PropertyName = "Вес",
                WbValue = wbProduct.Dimensions.WeightBrutto.ToString(),
                WbFieldName = "Dimensions.WeightBrutto",
                SimaLandFieldName = nameof(SimalandProductDto.weight),
                SimaLandValue = simaProduct?.weight.ToString(),
                IsSelectable = true
            }
        });

        // Характеристики
        if (wbProduct.Characteristics != null)
        {
            foreach (var charact in wbProduct.Characteristics)
            {
                string value = charact.Value switch
                {
                    string s => s,
                    double d => d.ToString(),
                    List<string> list => string.Join(", ", list),
                    _ => charact.Value?.ToString() ?? ""
                };

                var simaAttr = simaProduct?.Attributes?
                    .FirstOrDefault(a => a.attr_name.Equals(charact.Name, StringComparison.OrdinalIgnoreCase));

                mappings.Add(new PropertyMapping
                {
                    PropertyName = charact.Name,
                    WbValue = value,
                    WbFieldName = $"Characteristics:{charact.Id}",
                    SimaLandFieldName = simaAttr?.attr_name,
                    SimaLandValue = simaAttr?.value_text,
                    IsSelectable = true,
                    IsFromAttribute = true
                });
            }
        }

        return mappings;
    }
}
