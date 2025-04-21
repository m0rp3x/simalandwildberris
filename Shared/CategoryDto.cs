namespace Shared;

public class CategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string NameAlias { get; set; } = "";
    public int ItemsCount { get; set; }
    public int ItemsParentCount { get; set; }
    public List<CategoryDto> SubCategories { get; set; } = new();
    public List<CategoryDto> ActiveSubCategories { get; set; } = new();
    
}
