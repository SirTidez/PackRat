using System.Reflection;
using UnityEngine;

#if !MONO
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
#endif

namespace PackRat.Helpers;

/// <summary>
/// Identifies a pure-uGUI layout contract exported by the PackRat UI authoring project.
/// </summary>
public enum EditorUiPane
{
    Standalone,
    Embedded,
    Handover,
    Settings,
    DedicatedCanvas
}

/// <summary>
/// Loads and owns PackRat's approved editor-authored UI AssetBundle. The bundle
/// contains only built-in Unity components; PackRat continues to own data and event binding.
/// </summary>
public static class EditorUiAssetBundle
{
    private const string ResourceName = "PackRat.assets.packrat_ui_windows.bundle";

    private static readonly Dictionary<EditorUiPane, GameObject> _prefabs = new();
    private static AssetBundle _bundle;
    private static bool _loadAttempted;
    private static bool _missingResourceLogged;

    /// <summary>
    /// Gets whether a compatible editor-authored bundle was loaded from the mod assembly.
    /// </summary>
    public static bool IsLoaded => _bundle != null;

    /// <summary>
    /// Loads the embedded bundle once so opening a backpack never performs asset decoding.
    /// A missing resource is expected until the authoring export has run and keeps the C# UI intact.
    /// </summary>
    public static bool Prewarm()
    {
        if (_bundle != null)
            return true;
        if (_loadAttempted)
            return false;

        _loadAttempted = true;
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
            if (stream == null)
            {
                if (!_missingResourceLogged)
                {
                    _missingResourceLogged = true;
                    ModLogger.Debug("[EditorUI] AssetBundle is not embedded; retaining the C# UI fallback.");
                }
                return false;
            }

            using var memory = new MemoryStream();
            stream.CopyTo(memory);
            var data = memory.ToArray();
#if MONO
            _bundle = AssetBundle.LoadFromMemory(data);
#else
            var il2CppData = new Il2CppStructArray<byte>(data.Length);
            for (var index = 0; index < data.Length; index++)
                il2CppData[index] = data[index];
            _bundle = AssetBundle.LoadFromMemory(il2CppData);
#endif
            if (_bundle == null)
            {
                ModLogger.Error("[EditorUI] Unity rejected the embedded PackRat UI AssetBundle.");
                return false;
            }

            ModLogger.Info("[EditorUI] Prewarmed the editor-authored UI AssetBundle.");
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error("EditorUiAssetBundle.Prewarm", ex);
            return false;
        }
    }

    /// <summary>
    /// Loads and caches one prefab contract without instantiating it.
    /// </summary>
    public static bool TryGetPrefab(EditorUiPane pane, out GameObject prefab)
    {
        prefab = null;
        if (_prefabs.TryGetValue(pane, out var cached) && cached != null)
        {
            prefab = cached;
            return true;
        }

        if (!Prewarm())
            return false;

        try
        {
            var path = GetAssetPath(pane);
#if MONO
            prefab = _bundle.LoadAsset<GameObject>(path);
#else
            prefab = _bundle.LoadAsset(path, Il2CppType.Of<GameObject>())?.TryCast<GameObject>();
#endif
            if (prefab == null)
            {
                ModLogger.Error($"[EditorUI] Prefab '{path}' was not found in the UI AssetBundle.");
                return false;
            }

            _prefabs[pane] = prefab;
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"EditorUiAssetBundle.TryGetPrefab({pane})", ex);
            return false;
        }
    }

    /// <summary>
    /// Instantiates an editor-authored pane beneath its runtime owner while preserving prefab-local
    /// anchors, offsets, and scale. Event and data binding remains the caller's responsibility.
    /// </summary>
    public static bool TryInstantiate(EditorUiPane pane, Transform parent, out GameObject instance)
    {
        instance = null;
        if (parent == null || !TryGetPrefab(pane, out var prefab))
            return false;

        try
        {
            instance = UnityEngine.Object.Instantiate(prefab, parent, false);
            instance.name = prefab.name;
            return true;
        }
        catch (Exception ex)
        {
            ModLogger.Error($"EditorUiAssetBundle.TryInstantiate({pane})", ex);
            return false;
        }
    }

    /// <summary>
    /// Releases cached prefab references and the bundle during mod shutdown.
    /// </summary>
    public static void Unload()
    {
        _prefabs.Clear();
        if (_bundle != null)
        {
            try
            {
                _bundle.Unload(unloadAllLoadedObjects: true);
            }
            catch (Exception ex)
            {
                ModLogger.Error("EditorUiAssetBundle.Unload", ex);
            }
        }

        _bundle = null;
        _loadAttempted = false;
    }

    private static string GetAssetPath(EditorUiPane pane)
    {
        return pane switch
        {
            EditorUiPane.Embedded => "assets/packratui/prefabs/packratembeddedpane.prefab",
            EditorUiPane.Handover => "assets/packratui/prefabs/packrathandoverpane.prefab",
            EditorUiPane.Settings => "assets/packratui/prefabs/packratsettingsoverlay.prefab",
            EditorUiPane.DedicatedCanvas => "assets/packratui/prefabs/packratdedicatedcanvas.prefab",
            _ => "assets/packratui/prefabs/packratstandalonepane.prefab"
        };
    }
}
