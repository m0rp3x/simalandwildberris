namespace Shared;

public class FieldMapping
{
    public string WbFieldName { get; set; } = "";          // "Title", "Height", "Char_123"
    public string DisplayName { get; set; } = "";          // UI name
    public string SourceProperty { get; set; } = "";       // "name", "Attr_456"
    public FieldMappingType Type { get; set; }             // enum: Text / Dimension / Characteristic
}

public enum FieldMappingType
{
    Text,
    Dimension,
    Characteristic
}