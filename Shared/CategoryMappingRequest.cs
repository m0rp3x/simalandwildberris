namespace Shared;

public class CategoryMappingRequest
{
    public int WildberriesCategoryId { get; set; }
    public string SimalandCategoryName { get; set; } = "";
    public List<FieldMapping> Mappings { get; set; } = new();
    public List<CharacteristicValueMapping> CharacteristicValueMappings { get; set; } = new();
}