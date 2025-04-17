using Shared;
using WBSL.Models;

namespace WBSL.Data.Mappers;

public static class WbProductCardMapToDomain
{
    public static WbProductCard MapToDomain(WbProductCardDto card)
    {
        return new WbProductCard
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
                Value = x.Skus != null ? string.Join(", ", x.Skus) : string.Empty
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
                Value = ch.Value?.ToString() ?? string.Empty
            }).ToList() ?? new List<WbProductCardCharacteristic>(),
            Dimensions = card.Dimensions != null
                ? new List<WbDimension>
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
                : new List<WbDimension>()
        };
    }

    private static DateTime NormalizeDateTime(DateTime dateTime){
        if (dateTime.Kind == DateTimeKind.Unspecified)
            return DateTime.SpecifyKind(dateTime, DateTimeKind.Local);

        return dateTime.Kind == DateTimeKind.Utc
            ? dateTime.ToLocalTime()
            : dateTime;
    }
}