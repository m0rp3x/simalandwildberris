using System.Text.Json;

namespace WBSL.Client.Pages
{
    public static class Extensions
    {
        public static JsonElement? GetValueOrDefault(this Dictionary<string, JsonElement> dict, string key)
        {
            if (dict.TryGetValue(key, out var value))
                return value;
            return null;
        }
    }
}