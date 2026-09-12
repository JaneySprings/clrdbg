using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNet.Debugging.Common.Extensions;
using DotNet.Debugging.Common.Logging;

namespace DotNet.Debugging.Common;

public static class Localizer {
    private const string DefaultLocale = "en-us";
    private static CurrentClassLogger logger = new CurrentClassLogger(nameof(Localizer));

    public static void Init() {
        SafeExtensions.Invoke(() => {
            var culture = new CultureInfo(DefaultLocale);
            var nlsString = Environment.GetEnvironmentVariable("VSCODE_NLS_CONFIG");
            if (!string.IsNullOrEmpty(nlsString)) {
                var nlsConfig = JsonSerializer.Deserialize<NlsConfig>(nlsString);
                if (!string.IsNullOrEmpty(nlsConfig?.Locale))
                    culture = new CultureInfo(nlsConfig.Locale);
            }

            logger.Debug($"NLS:[{nlsString}] -> {culture}");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        });
    }

    class NlsConfig {
        [JsonPropertyName("locale")] public string? Locale { get; set; }
        [JsonPropertyName("osLocale")] public string? OsLocale { get; set; }
    }
}