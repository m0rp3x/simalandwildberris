﻿
namespace Shared;

public class WbApiResponse
{
    public List<WbProductCardDto> Cards{ get; set; }
    public WbCursorDto Cursor{ get; set; }
}
public class WbProductFullInfoDto
{
    public WbProductCardDto Product { get; set; }
    public List<WbAdditionalCharacteristicDto>? AdditionalCharacteristics { get; set; }
    public WbProductFullInfoDto() {} 
    public WbProductFullInfoDto(WbProductCardDto product, List<WbAdditionalCharacteristicDto>? characteristics){
        Product = product;
        AdditionalCharacteristics = characteristics;
    }
    public WbProductFullInfoDto(WbProductCardDto product){
        Product = product;
        AdditionalCharacteristics = null;
    }
}
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
public class WbProductCardDto
 {
     public long NmID{ get; set; }
     public long ImtID{ get; set; }
     public string NmUUID{ get; set; }
     public int SubjectID{ get; set; }
     public string SubjectName{ get; set; }
     public string VendorCode{ get; set; }
     public string Brand{ get; set; }
     public string Title{ get; set; }
     public string Description{ get; set; }
     public bool NeedKiz{ get; set; }
     public List<WbPhotoDto>? Photos{ get; set; }
     public WbDimensionsDto? Dimensions{ get; set; }
     public List<WbCharacteristicDto>? Characteristics{ get; set; }
     public List<WbsizeDto>? Sizes{ get; set; }
     public DateTime CreatedAt{ get; set; }
     public DateTime UpdatedAt{ get; set; }
 }
public class WbCreateVariantDto
{
    public string VendorCode { get; set; }
    public string Title { get; set; }
    public string Description { get; set; }
    public string Brand { get; set; }
    public WbDimensionsDtoToApi Dimensions { get; set; }
    public List<WbCharacteristicDto> Characteristics { get; set; } = new();
    public List<WbsizeDto> Sizes { get; set; } = new();
    
    public WbCreateVariantDto() {}

    public WbCreateVariantDto(WbProductCardDto dto){
        VendorCode = dto.VendorCode;
        Title = dto.Title;
        Description = dto.Description;
        Brand = dto.Brand;
        Dimensions = dto.Dimensions != null
            ? new WbDimensionsDtoToApi {
                Length = FixDimension(dto.Dimensions.Length),
                Width = FixDimension(dto.Dimensions.Width),
                Height = FixDimension(dto.Dimensions.Height),
                WeightBrutto = Math.Round((decimal)dto.Dimensions.WeightBrutto, 3),
            }
            : new WbDimensionsDtoToApi();
        Characteristics = dto.Characteristics ?? new List<WbCharacteristicDto>();
        Sizes = dto.Sizes ?? new List<WbsizeDto>();
        
    }
    public WbCreateVariantDto(WbCreateVariantInternalDto dto){
        VendorCode = dto.VendorCode;
        Title = dto.Title;
        Description = dto.Description;
        Brand = dto.Brand;
        Dimensions = dto.Dimensions != null
            ? new WbDimensionsDtoToApi {
                Length = FixDimension(dto.Dimensions.Length),
                Width = FixDimension(dto.Dimensions.Width),
                Height = FixDimension(dto.Dimensions.Height),
                WeightBrutto = Math.Round(dto.Dimensions.WeightBrutto, 3),
            }
            : new WbDimensionsDtoToApi();
        Characteristics = dto.Characteristics ?? new List<WbCharacteristicDto>();
        Sizes = dto.Sizes ?? new List<WbsizeDto>();
    }
    private int FixDimension(int? value)
    {
        return value.HasValue && value.Value > 0 ? value.Value : 1;
    }
}

public class WbCreateVariantInternalDto : WbCreateVariantDto
{
    public int SubjectID { get; set; }
    public WbCreateVariantInternalDto() : base(){}
    
    public WbCreateVariantInternalDto(
        string vendorCode,
        string title,
        string description,
        string brand,
        WbDimensionsDto dimensions,
        List<WbCharacteristicDto> characteristics,
        List<WbsizeDto> sizes,
        int subjectId)
    {
        VendorCode = vendorCode;
        Title = title;
        Description = description;
        Brand = brand;
        SubjectID = subjectId;
        Sizes = sizes;
        Dimensions = dimensions != null
            ? new WbDimensionsDtoToApi
            {
                Length = FixDimension(dimensions.Length),
                Width = FixDimension(dimensions.Width),
                Height = FixDimension(dimensions.Height),
                WeightBrutto = Math.Round((decimal)dimensions.WeightBrutto, 3)
            }
            : new WbDimensionsDtoToApi();
        
        Characteristics = (characteristics ?? new())
            .Where(c => IsValidCharValue(c.Value))
            .ToList();
    }
    private bool IsValidCharValue(object? val)
    {
        if (val == null) return false;

        return val switch
        {
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i != 0,
            decimal d => d != 0,
            List<string> sl => sl.Any(x => !string.IsNullOrWhiteSpace(x)),
            List<int> il => il.Any(x => x != 0),
            _ => true // всё остальное считаем валидным
        };
    }
    private int FixDimension(int? value)
    {
        return value.HasValue && value.Value > 0 ? value.Value : 1;
    }
}

public class WbPhotoDto
{
    public string Big{ get; set; }
    public string C246x328{ get; set; }
    public string C516x688{ get; set; }
    public string Hq{ get; set; }
    public string Square{ get; set; }
    public string Tm{ get; set; }
}

public class WbDimensionsDtoToApi
{
    public int Width{ get; set; }
    public int Height{ get; set; }
    public int Length{ get; set; }
    public decimal WeightBrutto{ get; set; }
}

public class WbDimensionsDto
{
    public int Width{ get; set; }
    public int Height{ get; set; }
    public int Length{ get; set; }
    public double? WeightBrutto{ get; set; }
    public bool IsValid{ get; set; }
}

public class WbCharacteristicDto
{
    public int Id{ get; set; }
    public string Name{ get; set; }
    public object Value{ get; set; } // Может быть string, double или List<string>
}

public class WbsizeDto
{
    public long ChrtID{ get; set; }
    public string TechSize{ get; set; }
    public string WbSize{ get; set; }
    public List<string> Skus{ get; set; }
}

public class WbCursorDto
{
    public DateTime UpdatedAt{ get; set; }
    public long NmID{ get; set; }
    public int Total{ get; set; }
}