using Shared;
using Shared.FieldInfos;

namespace WBSL.Client.Data.Helpers;

public static class WbFieldInfoHelper
{
    public static List<WbFieldInfo> Create(
        Func<object> getTitle, Action<object> setTitle,
        Func<object> getDescription, Action<object> setDescription,
        Func<object> getBrand, Action<object> setBrand,
        WbDimensionsDto dimensions)
    {
        return new List<WbFieldInfo>
        {
            new() {
                FieldName = "Title",
                DisplayName = "Наименование товара",
                Getter = getTitle,
                Setter = setTitle
            },
            new() {
                FieldName = "Description",
                DisplayName = "Описание",
                Getter = getDescription,
                Setter = setDescription
            },
            new() {
                FieldName = "Brand",
                DisplayName = "Бренд",
                Getter = getBrand,
                Setter = setBrand
            },
            new() {
                FieldName = "Length",
                DisplayName = "Длина",
                GroupName = "Габариты (в см / кг)",
                Getter = () => dimensions.Length,
                Setter = val => dimensions.Length = Convert.ToInt32(val)
            },
            new() {
                FieldName = "Width",
                DisplayName = "Ширина",
                GroupName = "Габариты (в см / кг)",
                Getter = () => dimensions.Width,
                Setter = val => dimensions.Width = Convert.ToInt32(val)
            },
            new() {
                FieldName = "Height",
                DisplayName = "Высота",
                GroupName = "Габариты (в см / кг)",
                Getter = () => dimensions.Height,
                Setter = val => dimensions.Height = Convert.ToInt32(val)
            },
            new() {
                FieldName = "WeightBrutto",
                DisplayName = "Вес брутто",
                GroupName = "Габариты (в см / кг)",
                Getter = () => dimensions.WeightBrutto,
                Setter = val => dimensions.WeightBrutto = (double?)Convert.ToDecimal(val)
            }
        };
    }
}