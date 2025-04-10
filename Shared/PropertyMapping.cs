namespace Shared;

public class PropertyMapping
{
    public string PropertyName { get; set; }
    public string WbValue { get; set; }
    public string WbFieldName { get; set; }
    public string? SimaLandFieldName { get; set; } // имя поля SimaLand
    public string? SimaLandValue { get; set; }     // значение поля SimaLand
    public bool IsSelectable { get; set; }
    public bool IsFromAttribute { get; set; } 
}