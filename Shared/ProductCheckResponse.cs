namespace Shared;

public class ProductCheckResponse
{
    public bool IsNullFromWb { get; set; }
    public SimalandProductDto? SimalandProduct { get; set; }
    
    public WbCategoryDto? BaseCategory { get; set; }
    public WbCategoryDto? ChildCategory { get; set; }
}
