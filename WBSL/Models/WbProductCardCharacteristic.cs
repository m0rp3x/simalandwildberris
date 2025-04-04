using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class WbProductCardCharacteristic
{
    public long ProductNmID { get; set; }

    public int CharacteristicId { get; set; }

    public string? Value { get; set; }

    public virtual WbCharacteristic Characteristic { get; set; } = null!;

    public virtual WbProductCard ProductNm { get; set; } = null!;
}
