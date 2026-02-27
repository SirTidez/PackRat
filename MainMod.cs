using MelonLoader;
using PackRat.Config;
using PackRat.Helpers;
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
    public const string Version = "1.0.1";
}

public class PackRat : MelonMod
{
    public override void OnInitializeMelon()
    {
        Configuration.Instance.Load();
        Configuration.Instance.Save(); // Forces config file creation with defaults
        ModLogger.Info("PackRat initialized.");
    }

    public override void OnSceneWasLoaded(int buildIndex, string sceneName)
    {
        Configuration.Instance.Reset();
        if (sceneName != "Main")
            return;

        ConfigSyncManager.StartSync();
        BackpackShopIntegration.RunWhenReady();
    }
}
