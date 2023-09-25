using System;
using System.Globalization;
using Avalonia;
using Avalonia.Platform;
using NGettext;
using Serilog;

namespace SS14.Launcher.Localization;

/// <summary>
/// Manages localization for the launcher, providing functionality for setting current language and looking up
/// translated strings from GetText catalogs.
/// </summary>
public class LocalizationManager
{
    private Catalog? activeCatalog;

    public LocalizationManager()
    {
    }

    public void LoadDefault()
    {
        LoadTestCulture();
    }

    private void LoadTestCulture()
    {
        var sergalTextCultureInfo = new CultureInfo("sergal");
        LoadCulture(sergalTextCultureInfo);
    }

    public void LoadCulture(CultureInfo culture)
    {
        var assets = AvaloniaLocator.Current.GetService<IAssetLoader>();

        if (assets == null)
        {
            Log.Warning("Unable to find asset loader, no localization will be done.");
            return;
        }

        // This pathing logic mirrors ngettext FindTranslationFile
        var possibleUris = new[] {
            new Uri($"avares://SSMV.Launcher/Assets/locale/" + culture.Name.Replace('-', '_') + "/LC_MESSAGES/Launcher.mo"),
            new Uri($"avares://SSMV.Launcher/Assets/locale/" + culture.Name + "/LC_MESSAGES/Launcher.mo"),
            new Uri($"avares://SSMV.Launcher/Assets/locale/" + culture.TwoLetterISOLanguageName + "/LC_MESSAGES/Launcher.mo")
        };

        foreach (var possibleFileUri in possibleUris)
        {
            if (assets.Exists(possibleFileUri))
            {
                var stream = assets.Open(possibleFileUri);
                activeCatalog = new Catalog(stream, culture);

                if (activeCatalog != null)
                {
                    Log.Information("Loaded translation catalog for " + culture.Name);
                    return;
                }
                else
                    Log.Warning("Problem loading translation catalog at " + possibleFileUri.ToString());
            }
        }

        Log.Warning("Could not find localization .po for culture: " + culture.Name);
    }

    public string GetString(string sourceString)
    {
        if (activeCatalog == null)
            return sourceString;

        return activeCatalog.GetString(sourceString);
    }

    public string GetParticularString(string context, string sourceString)
    {
        if (activeCatalog == null)
            return sourceString;

        return activeCatalog.GetParticularString(context, sourceString);
    }

    /// <summary>
    /// This custom function allows to attempt looking up a context-specific string, and if it fails, to fallback to
    /// a non-context generic string.
    /// </summary>
    /// <param name="context"></param>
    /// <param name="sourceString"></param>
    /// <returns></returns>
    public string GetParticularStringWithFallback(string context, string sourceString)
    {
        if (activeCatalog == null)
            return sourceString;

        if (context != null)
        {
            // Try to get string with context, and if not defined, fallback to no context version.
            // NGetText's implementation would fall through back to default non-translated language, so we do some
            // manual peeking here.

            if (activeCatalog.IsTranslationExist(context + Catalog.CONTEXT_GLUE + sourceString))
                return activeCatalog.GetParticularString(context, sourceString);
        }

        return activeCatalog.GetString(sourceString);
    }
}
