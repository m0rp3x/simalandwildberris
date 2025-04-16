namespace Shared.FieldInfos;

public class WbFieldInfo
{
    public string FieldName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string? GroupName { get; set; } = null; 
    public Func<object> Getter { get; set; } = () => "";
    public Action<object> Setter { get; set; } = _ => { };
    public bool IsCharacteristic { get; set; } = false;
}