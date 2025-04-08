using WBSL.Models;

namespace WBSL.Data.Services.Wildberries.Models;

//TODO: В БД ДОБАВЬ
public class CategoryValueRelation
{
    public int Id{ get; set; }
    
    public int ParentId{ get; set; }
    public WbCharacteristic Parent{ get; set; }
    
    public string WbValue{ get; set; }
    public string SimalandValue{ get; set; }
}