using System;
using System.Collections.Generic;

namespace WBSL.Models;

public partial class product
{
    public long sid { get; set; }

    public string name { get; set; } = null!;

    public string? description { get; set; }

    public decimal? width { get; set; }

    public decimal? height { get; set; }

    public decimal? depth { get; set; }

    public decimal? weight { get; set; }

    public decimal? box_depth { get; set; }

    public decimal? box_height { get; set; }

    public decimal? box_width { get; set; }

    public string? base_photo_url { get; set; }

    public int? category_id { get; set; }

    public string? balance { get; set; }

    public int? qty_multiplier { get; set; }

    public decimal? wholesale_price { get; set; }

    public decimal? price { get; set; }

    public string? category_name { get; set; }

    public List<string>? photo_urls { get; set; }

    public string? barcodes { get; set; }

    public int? vat { get; set; }
}
