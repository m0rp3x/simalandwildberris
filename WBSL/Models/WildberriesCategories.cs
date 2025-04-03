namespace WBSL.Models;

public class WildberriesParrentCategories
{
    public int Id { get; set; }
    public string Name { get; set; }
    
    public ICollection<WildberriesCategories> CategoriesCollection{ get; set; }
}

public class WildberriesCategories
{
    public int Id{ get; set; }
    public int ParentId{ get; set; }
    public string Name{ get; set; }
    public string ParentName{ get; set; }
}