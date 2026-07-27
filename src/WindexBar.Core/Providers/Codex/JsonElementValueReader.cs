using System.Globalization;
using System.Text.Json;

namespace WindexBar.Core.Providers.Codex;

internal static class JsonElementValueReader
{
    public static bool TryReadDouble(JsonElement value, out double result)
    {
        if (value.ValueKind is JsonValueKind.Number)
        {
            return value.TryGetDouble(out result);
        }

        if (value.ValueKind is JsonValueKind.String)
        {
            return double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
        }

        result = 0;
        return false;
    }

    public static bool TryReadInt64(JsonElement value, out long result)
    {
        if (value.ValueKind is JsonValueKind.Number)
        {
            return value.TryGetInt64(out result);
        }

        if (value.ValueKind is JsonValueKind.String)
        {
            return long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out result);
        }

        result = 0;
        return false;
    }
}
