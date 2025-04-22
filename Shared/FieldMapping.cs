namespace Shared;

public class FieldMapping
{
    public string WbFieldName { get; set; } = "";          // "Title", "Height", "Char_123"
    public string DisplayName { get; set; } = "";          // UI name
    public string SourceProperty { get; set; } = "";       // "name", "Attr_456"
    public FieldMappingType Type { get; set; }             // enum: Text / Dimension / Characteristic
    
    public WbCharacteristicDataType? CharacteristicDataType { get; set; } // null если не характериситка
    public int? MaxCount { get; set; } // если нужно знать, список или нет
}

public enum FieldMappingType
{
    Text,
    Dimension,
    Characteristic
}

public enum WbCharacteristicDataType
{
    String = 0,
    Number = 4,
}