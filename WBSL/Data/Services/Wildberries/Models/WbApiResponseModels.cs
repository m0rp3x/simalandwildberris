namespace WBSL.Data.Services.Wildberries.Models;

public class WbApiResponse
{
    public List<WbProductCard> Cards{ get; set; }
    public WbCursor Cursor{ get; set; }
}

public class WbProductCard
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
    public List<WbPhoto> Photos{ get; set; }
    public WbDimensions Dimensions{ get; set; }
    public List<WbCharacteristic> Characteristics{ get; set; }
    public List<Wbsize> Sizes{ get; set; }
    public DateTime CreatedAt{ get; set; }
    public DateTime UpdatedAt{ get; set; }
}

public class WbPhoto
{
    public string Big{ get; set; }
    public string C246x328{ get; set; }
    public string C516x688{ get; set; }
    public string Hq{ get; set; }
    public string Square{ get; set; }
    public string Tm{ get; set; }
}

public class WbDimensions
{
    public int Width{ get; set; }
    public int Height{ get; set; }
    public int Length{ get; set; }
    public double WeightBrutto{ get; set; }
    public bool IsValid{ get; set; }
}

public class WbCharacteristic
{
    public int Id{ get; set; }
    public string Name{ get; set; }
    public object Value{ get; set; } // Может быть string, double или List<string>
}

public class Wbsize
{
    public long ChrtID{ get; set; }
    public string TechSize{ get; set; }
    public string WbSize{ get; set; }
    public List<string> Skus{ get; set; }
}

public class WbCursor
{
    public DateTime UpdatedAt{ get; set; }
    public long NmID{ get; set; }
    public int Total{ get; set; }
}