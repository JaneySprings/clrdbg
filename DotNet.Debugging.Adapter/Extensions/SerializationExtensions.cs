using System.Text.Json;
using System.Text.Json.Serialization;
using DotNet.Debugging.Common.Extensions;
using Newtonsoft.Json.Linq;

namespace DotNet.Debugging.Adapter.Extensions {
    public static class SerializationExtensions {
        public static JsonSerializerOptions Options { get; } = new JsonSerializerOptions {
            Converters = { new JsonStringEnumConverter() },
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };

        public static JToken? TryGetValue(this Dictionary<string, JToken> dictionary, string key) {
            if (dictionary.TryGetValue(key, out var value))
                return value;
            return null;
        }
        public static T? ToClass<T>(this JToken? jtoken) where T : class {
            if (jtoken == null || jtoken.Type == JTokenType.Null)
                return null;

            string json = jtoken.ToString(Newtonsoft.Json.Formatting.None);
            return SafeExtensions.Invoke(() => JsonSerializer.Deserialize<T>(json, Options));
        }
        public static T ToValue<T>(this JToken? jtoken, T defaultValue = default) where T : struct {
            if (jtoken == null || jtoken.Type == JTokenType.Null)
                return defaultValue;

            string json = jtoken.ToString(Newtonsoft.Json.Formatting.None);
            return SafeExtensions.Invoke(defaultValue, () => JsonSerializer.Deserialize<T>(json, Options));
        }
    }
}