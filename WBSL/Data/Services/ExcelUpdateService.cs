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

    public async Task<int> UpdateProductNamesAsync(List<ProductNameUpdateDto> updates)
    {
        var sids = updates.Select(u => u.Sid).ToList();
        var products = _context.Set<product>().Where(p => sids.Contains(p.sid)).ToList();

        int updatedCount = 0;

        foreach (var product in products)
        {
            var update = updates.FirstOrDefault(u => u.Sid == product.sid);
            if (update != null && product.name != update.Name)
            {
                product.name = update.Name;
                updatedCount++;
            }
        }

        if (updatedCount > 0)
            await _context.SaveChangesAsync();

        return updatedCount;
    }
    
    
    public async Task<byte[]> ExportAllProductsToExcelAsync()
    {
        var products = await _context.Set<product>().ToListAsync();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Products");

        // Заголовки
        worksheet.Cell(1, 1).Value = "Артикул";
        worksheet.Cell(1, 2).Value = "Наименование";

        int row = 2;
        foreach (var p in products)
        {
            worksheet.Cell(row, 1).Value = p.sid;
            worksheet.Cell(row, 2).Value = p.name;
            row++;
        }

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}
