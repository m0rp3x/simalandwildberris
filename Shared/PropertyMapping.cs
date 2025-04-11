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
    
    public bool IsRequired { get; set; } // Нужно ли обязательно сопоставить
    public string? UnitName { get; set; } // Ед. измерения
    public int? CharcType { get; set; } // Тип характеристики
    public int? MaxCount { get; set; } // Макс. кол-во значений
    public int? CharcID { get; set; } // ID характеристики
}

public class PropertyMappingTemplate
{
    public string WbFieldName { get; set; } = "";
    public string? SimaLandFieldName { get; set; }
}