namespace Shared;



// Shared/PromptRequest.cs
public class PromptRequest
{
    public string Prompt { get; set; } = string.Empty;
}

public class UpdatedDescriptionDto
{
    public long Sid { get; set; }
    public string OldDescription { get; set; } = string.Empty;
    public string NewDescription { get; set; } = string.Empty;
}

// Shared/ShortenRequest.cs
public class ShortenRequest
{
    public string Prompt { get; set; } = "";
    /// <summary>
    /// "name"  — сократить поле name  
    /// "description" — сократить поле description
    /// </summary>
    public string Field { get; set; } = "name";
}

// Shared/ShortenedDto.cs
public class ShortenedDto
{
    public long Sid { get; set; }
    public string Field { get; set; } = "";            // "name" или "description"
    public string OldValue { get; set; } = "";          // старое name/description
    public string NewValue { get; set; } = "";          // новое укороченное
}




