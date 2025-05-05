using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using WBSL.Models;

namespace WBSL.Data.Services;

public class ExcelUpdateService
{
    private readonly QPlannerDbContext _context;

    public ExcelUpdateService(QPlannerDbContext context)
    {
        _context = context;
    }

    public async Task<List<long>> UpdateProductNamesAsync(List<ProductNameUpdateDto> updates)
    {
        var sids = updates.Select(u => u.Sid).ToList();

        var products = await _context.Set<product>()
            .Where(p => sids.Contains(p.sid))
            .ToListAsync();

        var updated = new List<long>();

        foreach (var product in products)
        {
            var update = updates.FirstOrDefault(u => u.Sid == product.sid);
            if (update == null) continue;

            bool changed = false;

            if (product.name != update.Name)
            {
                product.name = update.Name;
                changed = true;
            }

            if (product.qty_multiplier != update.QtyMultiplier)
            {
                product.qty_multiplier = update.QtyMultiplier;
                changed = true;
            }

            if (changed)
                updated.Add(product.sid);
        }

        if (updated.Any())
            await _context.SaveChangesAsync();

        return updated;
    }



    
    
    public async Task<byte[]> ExportAllProductsToExcelAsync()
    {
        var products = await _context.Set<product>().ToListAsync();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Products");

        // Заголовки
        worksheet.Cell(1, 1).Value = "Артикул";
        worksheet.Cell(1, 2).Value = "Наименование";
        worksheet.Cell(1, 3).Value = "Наименование";

        int row = 2;
        foreach (var p in products)
        {
            worksheet.Cell(row, 1).Value = p.sid;
            worksheet.Cell(row, 2).Value = p.name;
            worksheet.Cell(row, 3).Value = p.qty_multiplier;
            
            row++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
