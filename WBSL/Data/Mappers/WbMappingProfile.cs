using Shared;
using WBSL.Models;

namespace WBSL.Data.Mappers;

public static class WbProductCardMapper
{
    // Метод для маппинга WbApiResponse на список WbProductCard
    public static List<WbProductCard> MapFromApiResponse(WbApiResponse response)
    {
        return response.Cards.Select(card => new WbProductCard
        {
            NmID = card.NmID,
            ImtID = card.ImtID,
            NmUUID = card.NmUUID,
            SubjectID = card.SubjectID,
            SubjectName = card.SubjectName,
            VendorCode = card.VendorCode,
            Brand = card.Brand,
            Title = card.Title,
            Description = card.Description,
            NeedKiz = card.NeedKiz,
            CreatedAt = NormalizeDateTime(card.CreatedAt),
            UpdatedAt = NormalizeDateTime(card.UpdatedAt),
            WbPhotos = card.Photos?.Select(p => new WbPhoto
            {
                Big = p.Big,
                C246x328 = p.C246x328,
                C516x688 = p.C516x688,
                Hq = p.Hq,
                Square = p.Square,
                Tm = p.Tm
            }).ToList() ?? new List<WbPhoto>(),
            SizeChrts = card.Sizes?.Select(x => new WbSize
            {
                ChrtID = x.ChrtID,
                TechSize = x.TechSize,
                WbSize1 = x.WbSize,
                Value = string.Join(", ", x.Skus),
            }).ToList() ?? new List<WbSize>(),
            WbProductCardCharacteristics = card.Characteristics?.Select(ch => new WbProductCardCharacteristic
            {
                ProductNmID = card.NmID,
                CharacteristicId = ch.Id,
                Characteristic = new WbCharacteristic
                {
                    Name = ch.Name,
                    Id = ch.Id
                },
                Value = ch.Value.ToString(),
            }).ToList() ?? new List<WbProductCardCharacteristic>(),
            Dimensions = new List<WbDimension>
            {
                new WbDimension
                {
                    Width = card.Dimensions.Width,
                    Height = card.Dimensions.Height,
                    Length = card.Dimensions.Length,
                    WeightBrutto = card.Dimensions.WeightBrutto,
                    IsValid = card.Dimensions.IsValid
                }
            }
        }).ToList();
    }

    public static WbProductCardDto MapToDto(WbProductCard card)
    {
        return new WbProductCardDto
        {
            NmID = card.NmID,
            ImtID = card.ImtID ?? 0,
            NmUUID = card.NmUUID ?? string.Empty,
            SubjectID = card.SubjectID ?? 0,
            SubjectName = card.SubjectName ?? string.Empty,
            VendorCode = card.VendorCode ?? string.Empty,
            Brand = card.Brand ?? string.Empty,
            Title = card.Title ?? string.Empty,
            Description = card.Description ?? string.Empty,
            NeedKiz = card.NeedKiz ?? false,
            Photos = card.WbPhotos?.Select(p => new WbPhotoDto
            {
                Big = p.Big ?? string.Empty,
                C246x328 = p.C246x328 ?? string.Empty,
                C516x688 = p.C516x688 ?? string.Empty,
                Hq = p.Hq ?? string.Empty,
                Square = p.Square ?? string.Empty,
                Tm = p.Tm ?? string.Empty
            }).ToList(),
            Dimensions = card.Dimensions?.Select(d => new WbDimensionsDto
            {
                Width = d.Width ?? 0,
                Height = d.Height ?? 0,
                Length = d.Length ?? 0,
                WeightBrutto = d.WeightBrutto ?? 0,
                IsValid = d.IsValid ?? false
            }).FirstOrDefault(), // Только один объект WbDimensionsDto
            Characteristics = card.WbProductCardCharacteristics?.Select(ch => new WbCharacteristicDto
            {
                Id = ch.Characteristic.Id,
                Name = ch.Characteristic.Name ?? string.Empty,
                Value = ch.Value ?? string.Empty
            }).ToList(),
            Sizes = card.SizeChrts?.Select(s => new WbsizeDto
            {
                ChrtID = s.ChrtID,
                TechSize = s.TechSize ?? string.Empty,
                WbSize = s.WbSize1 ?? string.Empty,
                Skus = !string.IsNullOrEmpty(s.Value) ? s.Value.Split(',').ToList() : new List<string>()
            }).ToList(),
            CreatedAt = card.CreatedAt.Value,
            UpdatedAt = card.UpdatedAt.Value
        };
    }
    
    public static WbProductCard MapFromDto(WbProductCardDto dto)
    {
        return new WbProductCard
        {
            NmID = dto.NmID,
            ImtID = dto.ImtID,
            NmUUID = dto.NmUUID,
            SubjectID = dto.SubjectID,
            SubjectName = dto.SubjectName,
            VendorCode = dto.VendorCode,
            Brand = dto.Brand,
            Title = dto.Title,
            Description = dto.Description,
            NeedKiz = dto.NeedKiz,
            CreatedAt = dto.CreatedAt,
            UpdatedAt = dto.UpdatedAt,
            WbPhotos = dto.Photos.Select(p => new WbPhoto
            {
                Big = p.Big,
                C246x328 = p.C246x328,
                C516x688 = p.C516x688,
                Hq = p.Hq,
                Square = p.Square,
                Tm = p.Tm
            }).ToList(),
            SizeChrts = dto.Sizes.Select(s => new WbSize
            {
                ChrtID = s.ChrtID,
                TechSize = s.TechSize,
                WbSize1 = s.WbSize,
                Value = string.Join(", ", s.Skus)
            }).ToList(),
            WbProductCardCharacteristics = dto.Characteristics.Select(ch => new WbProductCardCharacteristic
            {
                ProductNmID = dto.NmID,
                CharacteristicId = ch.Id,
                Characteristic = new WbCharacteristic
                {
                    Name = ch.Name,
                    Id = ch.Id
                },
                Value = ch.Value.ToString()
            }).ToList(),
            Dimensions = new List<WbDimension>
            {
                new WbDimension
                {
                    Width = dto.Dimensions.Width,
                    Height = dto.Dimensions.Height,
                    Length = dto.Dimensions.Length,
                    WeightBrutto = dto.Dimensions.WeightBrutto,
                    IsValid = dto.Dimensions.IsValid
                }
            }
        };
    }
    // Нормализация времени
    private static DateTime NormalizeDateTime(DateTime date)
    {
        return date == default ? DateTime.MinValue : date;
    }
}