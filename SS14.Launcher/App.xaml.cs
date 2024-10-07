using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using JetBrains.Annotations;
using Serilog;
using SS14.Launcher.Localization;
using SS14.Launcher.Utility;

namespace SS14.Launcher;

public class App : Application
{
    private static readonly Dictionary<string, AssetDef> AssetDefs = new()
    {
        ["WindowIcon"] = new AssetDef("icon.ico", AssetType.WindowIcon),
        ["LogoLong"] = new AssetDef("logo-long.png", AssetType.Bitmap),
    };

    private readonly Dictionary<string, object> _baseAssets = new();

    public App()
    {
    }

    public override void Initialize()
    {
        // Loc load must execute after avalonia asset loader is available, but should happen before the xaml is loaded
        LoadLocalization();

        AvaloniaXamlLoader.Load(this);

        LoadBaseAssets();
        IconsLoader.Load(this);
    }

    private void LoadLocalization()
    {
        var localizationManager = Splat.Locator.Current.GetRequiredService<LocalizationManager>();
        localizationManager.LoadInferred();
        //localizationManager.OnTranslationChanged += HandleTranslationChanged;
    }

    private void LoadBaseAssets()
    {
        var loader = AvaloniaLocator.Current.GetService<IAssetLoader>()!;

        foreach (var (name, (path, type)) in AssetDefs)
        {
            using var dataStream = loader.Open(new Uri($"avares://SSMV.Launcher/Assets/{path}"));

            var asset = LoadAsset(type, dataStream);

            _baseAssets.Add(name, asset);
            Resources.Add(name, asset);
        }
    }

    private static object LoadAsset(AssetType type, Stream data)
    {
        return type switch
        {
            AssetType.Bitmap => new Bitmap(data),
            AssetType.WindowIcon => new WindowIcon(data),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private sealed record AssetDef(string DefaultPath, AssetType Type);

    private enum AssetType
    {
        Bitmap,
        WindowIcon
    }

    // private void HandleTranslationChanged()
    // {
    //     //AvaloniaXamlLoader.Load(this);
    // }
}
