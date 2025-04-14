using Shared;
using Shared.Enums;

namespace WBSL.Client.Data.Services;

public class ProductMappingService
{
    private PropertyMappingTemplate? FindTemplate(string wbFieldName, List<PropertyMappingTemplate> savedTemplates){
        return savedTemplates?.FirstOrDefault(t => t.WbFieldName == wbFieldName);
    }

    private string? GetSimaLandValue(SimalandProductDto sima, string? fieldName){
        if (string.IsNullOrWhiteSpace(fieldName)) return null;

        var prop = sima.GetType().GetProperty(fieldName);
        if (prop != null)
            return prop.GetValue(sima)?.ToString();

        var attr = sima.Attributes?.FirstOrDefault(a => a.attr_name == fieldName);
        return attr?.value_text;
    }

    public List<PropertyMapping> GenerateMappings(WbProductCardDto wbProduct, SimalandProductDto? simaProduct = null,
        List<WbAdditionalCharacteristicDto>? additionalCharacteristics = null,
        List<PropertyMappingTemplate> savedTemplates = null){
        var mappings = new List<PropertyMapping>();

        // Базовые маппинги
        mappings.AddRange(GenerateBaseMappings(wbProduct, simaProduct));

        // Габариты
        mappings.AddRange(GenerateDimensionsMappings(wbProduct, simaProduct));

        // Характеристики
        mappings.AddRange(GenerateCharacteristicsMappings(wbProduct, simaProduct, additionalCharacteristics));

        // Применяем шаблоны (если они есть)
        foreach (var mapping in mappings){
            var template = FindTemplate(mapping.WbFieldName, savedTemplates);
            if (template != null){
                mapping.SimaLandFieldName = template.SimaLandFieldName;
                mapping.SimaLandValue = simaProduct != null
                    ? GetSimaLandValue(simaProduct, template.SimaLandFieldName)
                    : null;
            }
        }

        return mappings;
    }

    private List<PropertyMapping> GenerateBaseMappings(WbProductCardDto wbProduct, SimalandProductDto? simaProduct){
        var baseMappings = new List<PropertyMapping>{
            new PropertyMapping{
                PropertyName = "Артикул продавца",
                WbValue = wbProduct.VendorCode,
                WbFieldName = nameof(WbProductCardDto.VendorCode),
                SimaLandFieldName = nameof(SimalandProductDto.sid),
                SimaLandValue = simaProduct?.sid.ToString() ?? "",
                IsSelectable = false
            },
            new PropertyMapping{
                PropertyName = "Бренд",
                WbValue = wbProduct.Brand,
                WbFieldName = nameof(WbProductCardDto.Brand),
                SimaLandFieldName = nameof(SimalandProductDto.trademark_name),
                SimaLandValue = simaProduct?.trademark_name,
                IsSelectable = true
            },
            new PropertyMapping{
                PropertyName = "Название",
                WbValue = wbProduct.Title,
                WbFieldName = nameof(WbProductCardDto.Title),
                SimaLandFieldName = nameof(SimalandProductDto.name),
                SimaLandValue = simaProduct?.name,
                IsSelectable = true
            },
            new PropertyMapping{
                PropertyName = "Описание",
                WbValue = wbProduct.Description,
                WbFieldName = nameof(WbProductCardDto.Description),
                SimaLandFieldName = nameof(SimalandProductDto.description),
                SimaLandValue = simaProduct?.description,
                IsSelectable = true
            }
        };
        baseMappings.Add(new PropertyMapping
        {
            PropertyName = "Категория",
            WbValue = $"{wbProduct.SubjectName} ({wbProduct.SubjectID})",
            SimaLandFieldName = null,
            SimaLandValue = null,
            MappingType = MappingWbType.Category,
            IsSelectable = false,
            SubjectId = wbProduct.SubjectID
        });
        return baseMappings;
    }

    private List<PropertyMapping> GenerateDimensionsMappings(WbProductCardDto wbProduct,
        SimalandProductDto? simaProduct){
        var dimensionMappings = new List<PropertyMapping>{
            new PropertyMapping{
                PropertyName = "Ширина товара",
                WbValue = wbProduct.Dimensions.Width.ToString(),
                WbFieldName = "Dimensions.Width",
                SimaLandFieldName = nameof(SimalandProductDto.width),
                SimaLandValue = simaProduct?.width.ToString(),
                IsSelectable = true
            },
            new PropertyMapping{
                PropertyName = "Высота товара",
                WbValue = wbProduct.Dimensions.Height.ToString(),
                WbFieldName = "Dimensions.Height",
                SimaLandFieldName = nameof(SimalandProductDto.height),
                SimaLandValue = simaProduct?.height.ToString(),
                IsSelectable = true
            },
            new PropertyMapping{
                PropertyName = "Длина товара",
                WbValue = wbProduct.Dimensions.Length.ToString(),
                WbFieldName = "Dimensions.Length",
                SimaLandFieldName = nameof(SimalandProductDto.depth),
                SimaLandValue = simaProduct?.depth.ToString(),
                IsSelectable = true
            },
            new PropertyMapping{
                PropertyName = "Вес товара",
                WbValue = wbProduct.Dimensions.WeightBrutto.ToString(),
                WbFieldName = "Dimensions.WeightBrutto",
                SimaLandFieldName = nameof(SimalandProductDto.weight),
                SimaLandValue = simaProduct?.weight.ToString(),
                IsSelectable = true
            }
        };

        return dimensionMappings;
    }

    private List<PropertyMapping> GenerateCharacteristicsMappings(WbProductCardDto wbProduct,
        SimalandProductDto? simaProduct, List<WbAdditionalCharacteristicDto>? additionalCharacteristics){
        var charMap = new Dictionary<int, PropertyMapping>();
        var characteristicMappings = new List<PropertyMapping>();

        // Обработка дополнительных характеристик
        if (additionalCharacteristics != null){
            foreach (var add in additionalCharacteristics){
                var mapping = new PropertyMapping{
                    PropertyName = add.Name,
                    WbFieldName = $"Characteristics:{add.CharcID}",
                    WbValue = "", // заполним позже
                    IsSelectable = true,
                    IsFromAttribute = false,
                    IsRequired = add.Required,
                    UnitName = add.UnitName,
                    CharcType = add.CharcType,
                    MaxCount = add.MaxCount,
                    CharcID = add.CharcID
                };

                charMap[add.CharcID] = mapping;
                characteristicMappings.Add(mapping);
            }
        }

        // Обработка характеристик из wbProduct
        if (wbProduct.Characteristics != null){
            foreach (var charact in wbProduct.Characteristics){
                string value = charact.Value switch{
                    string s => s,
                    double d => d.ToString(),
                    List<string> list => string.Join(", ", list),
                    _ => charact.Value?.ToString() ?? ""
                };

                // если есть — обновим существующий
                if (charMap.TryGetValue(charact.Id, out var existing)){
                    existing.WbValue = value;
                    existing.IsFromAttribute = true; // если хотим отметить, что пришло из WB
                }
                else{
                    // если нет — создадим новую маппу
                    var simaAttr = simaProduct?.Attributes?
                        .FirstOrDefault(a => a.attr_name.Equals(charact.Name, StringComparison.OrdinalIgnoreCase));

                    characteristicMappings.Add(new PropertyMapping{
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
        }

        return characteristicMappings;
    }
}