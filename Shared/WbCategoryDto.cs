namespace Shared;

public class WbCategoryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
}

public class WbCategoryDtoExt : WbCategoryDto
{
    public int ParentId { get; set; }
    
}
