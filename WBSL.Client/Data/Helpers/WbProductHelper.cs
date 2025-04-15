using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json;
using Shared;

namespace WBSL.Client.Data.Helpers;

public static class WbProductHelper
{
    public static Dictionary<string, string> BuildDisplayNameMap<T>()
    {
        return typeof(T).GetProperties()
            .Select(p =>
            {
                var display = p.GetCustomAttribute<DisplayAttribute>()?.Name;
                return new { Prop = p.Name, Display = display ?? p.Name };
            })
            .ToDictionary(x => x.Display, x => x.Prop);
    }

    public static string GetCharacteristicValue(WbCharacteristicDto charact)
    {
        if (charact.Value is JsonElement element)
        {
            return element.ValueKind switch
            {
                JsonValueKind.String => element.GetString(),
                JsonValueKind.Number => element.GetDouble().ToString(),
                JsonValueKind.Array => string.Join(", ", element.EnumerateArray().Select(x => x.ToString())),
                _ => charact.Value?.ToString() ?? "N/A"
            };
        }

        return charact.Value?.ToString() ?? "N/A";
    }

    public static object FormatValueForWb(PropertyMapping mapping)
    {
        if (string.IsNullOrWhiteSpace(mapping.SimaLandValue)) return "";

        var normalizedValue = mapping.SimaLandFieldName == "Цвет"
            ? char.ToLower(mapping.SimaLandValue[0]) + mapping.SimaLandValue.Substring(1)
            : mapping.SimaLandValue;

        var splitValues = normalizedValue
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        if (mapping.MaxCount is > 0)
        {
            splitValues = splitValues.Take(mapping.MaxCount.Value).ToList();
        }

        return mapping.CharcType switch
        {
            0 or 1 => splitValues,
            4 => splitValues.Select(val => int.TryParse(val, out var num) ? num : 0).ToList(),
            _ => mapping.SimaLandValue
        };
    }

    public static void SetPropertyValue(object target, string propertyPath, string value)
    {
        var parts = propertyPath.Split('.');

        for (int i = 0; i < parts.Length - 1; i++)
        {
            var prop = target.GetType().GetProperty(parts[i]);
            if (prop == null) return;

            var propValue = prop.GetValue(target);
            if (propValue == null)
            {
                propValue = Activator.CreateInstance(prop.PropertyType);
                prop.SetValue(target, propValue);
            }

            target = propValue;
        }

        var finalProp = target.GetType().GetProperty(parts.Last());
        if (finalProp == null || !finalProp.CanWrite) return;

        var targetType = Nullable.GetUnderlyingType(finalProp.PropertyType) ?? finalProp.PropertyType;

        try
        {
            object? convertedValue;

            if (targetType == typeof(int))
            {
                if (double.TryParse(value.Replace(',', '.'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var doubleVal))
                {
                    convertedValue = (int)Math.Round(doubleVal);
                }
                else
                {
                    convertedValue = Convert.ChangeType(value, targetType);
                }
            }
            else
            {
                convertedValue = Convert.ChangeType(value, targetType);
            }

            finalProp.SetValue(target, convertedValue);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка при конвертации значения '{value}' в {targetType.Name}: {ex.Message}");
        }
    }

    public static bool IsTypeCompatible(Type type, int? charcType)
    {
        if (charcType == null) return true;

        return charcType switch
        {
            0 or 1 => type == typeof(string),
            4 => type == typeof(int) || type == typeof(double) || type == typeof(float) || type == typeof(decimal),
            _ => true
        };
    }

    public static bool IsValueCompatible(string? value, int? charcType)
    {
        if (charcType == null || string.IsNullOrWhiteSpace(value)) return false;

        return charcType switch
        {
            0 or 1 => true,
            4 => double.TryParse(value, out _),
            _ => true
        };
    }

    public static string GetTypeString(int? charcType)
    {
        return charcType switch
        {
            0 or 1 => "Строка",
            4 => "Число",
            _ => "Не определено"
        };
    }
}