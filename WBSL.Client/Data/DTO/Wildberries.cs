namespace WBSL.Client.Data.DTO;

public class WbAdditionalCharacteristicDto
{
    public int CharcID { get; set; }
    public string SubjectName { get; set; }
    public int SubjectID { get; set; }
    public string Name { get; set; }
    public bool Required { get; set; }
    public string UnitName { get; set; }
    public int MaxCount { get; set; }
    public bool Popular { get; set; }
    public int CharcType { get; set; }
}

public class WbProductFullInfoDto
{
    public WbProductDto Product { get; set; }
    public List<WbAdditionalCharacteristicDto>? AdditionalCharacteristics { get; set; }
}

public class WbProductDto
{
    public long NmID { get; set; }
    public long ImtID { get; set; }
    public string NmUUID { get; set; }
    public int SubjectID { get; set; }
    public string SubjectName { get; set; }
    public string VendorCode { get; set; }
    public string Brand { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public bool NeedKiz { get; set; }
    public List<WbPhotoDto> Photos { get; set; }
    public WbDimensionsDto Dimensions { get; set; }
    public List<WbCharacteristicDto> Characteristics { get; set; }
    public List<WbAdditionalCharacteristicDto> AdditionalCharacteristics { get; set; } = new();
    public List<WbSizeDto> Sizes { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class WbPhotoDto
{
    public string Big { get; set; }
    public string C246x328 { get; set; }
    public string C516x688 { get; set; }
    public string Hq { get; set; }
    public string Square { get; set; }
    public string Tm { get; set; }
}

public class WbDimensionsDto
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int Length { get; set; }
    public double WeightBrutto { get; set; }
    public bool IsValid { get; set; }
}

public class WbCharacteristicDto
{
    public int Id { get; set; }
    public string Name { get; set; }
    public object Value { get; set; } // Может быть string, double или массивом
}

public class WbSizeDto
{
    public long ChrtID { get; set; }
    public string TechSize { get; set; }
    public string WbSize { get; set; }
    public List<string> Skus { get; set; }
}