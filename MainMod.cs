using MelonLoader;
using PackRat.Config;
using PackRat.Helpers;
using PackRat.Profiling;
using PackRat.Shops;

[assembly: MelonInfo(
    typeof(PackRat.PackRat),
    PackRat.BuildInfo.Name,
    PackRat.BuildInfo.Version,
    PackRat.BuildInfo.Author
)]
[assembly: MelonColor(1, 255, 165, 0)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace PackRat;

public static class BuildInfo
{
    public const string Name = "PackRat";
    public const string Description = "Portable backpack storage for Schedule One";
    public const string Author = "SirTidez";
    public const string Version = "2.0.1";
}

public class PackRat : MelonMod
{
    public override void OnInitializeMelon()
    {
        Configuration.Instance.Load();
        UiProfiler.ApplyEnabledState(Configuration.Instance.EnableDeveloperProfiler);
        UiProfiler.Event("lifecycle", "initialize");
        ModLogger.Info("PackRat initialized.");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        using var profile = UiProfiler.Measure("lifecycle", "scene_loaded",
            $"index={buildIndex};scene={sceneName}");
        UiProfiler.Event("lifecycle", "scene_change", $"index={buildIndex};scene={sceneName}");
        Configuration.Instance.Reset();
        UiProfiler.ApplyEnabledState(Configuration.Instance.EnableDeveloperProfiler);
        if (sceneName != "Main")
        {
            CameraLockedStateHelper.ResetSceneCache();
            return;
        }

        CameraLockedStateHelper.PrepareForMainSceneLoad();
        ConfigSyncManager.StartSync();
        BackpackShopIntegration.RunWhenReady();
    }

    public override void OnUpdate()
    {
        if (UiProfiler.IsEnabled)
            UiProfiler.FlushIfDue();
    }

    public override void OnDeinitializeMelon()
    {
        UiProfiler.Event("lifecycle", "deinitialize");
        UiProfiler.Shutdown();
    }

    public override void OnApplicationQuit()
    {
        UiProfiler.Event("lifecycle", "application_quit");
        UiProfiler.Shutdown();
    }
}
