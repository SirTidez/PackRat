using System;
using System.Collections.Generic;
using System.Reflection;

#if !MONO
using Il2CppNewtonsoft.Json;
using STJ = System.Text.Json;
#else
using Newtonsoft.Json;
#endif

namespace PackRat.Helpers;

/// <summary>
/// Helper class for safely creating JSON serialization in a cross-platform manner.
/// In IL2CPP, uses System.Text.Json. In Mono, uses Newtonsoft.Json.
/// </summary>
public static class JsonHelper
{
#if MONO
    private static object _cachedSettings = null;
    private static object _cachedSettingsFormatted = null;
#else
    private static JsonSerializerSettings _cachedSettings = null;
    private static JsonSerializerSettings _cachedSettingsFormatted = null;
#endif
    private static bool _initializationAttempted = false;

    /// <summary>
    /// Gets default JSON serializer settings.
    /// Returns null in Mono if settings can't be created (will use JsonConvert defaults).
    /// </summary>
#if MONO
    public static object GetDefaultSettings()
#else
    public static JsonSerializerSettings GetDefaultSettings()
#endif
    {
        if (_cachedSettings != null)
        {
#if MONO
            return null;
#else
            return (JsonSerializerSettings)_cachedSettings;
#endif
        }

        if (!_initializationAttempted)
        {
            _initializationAttempted = true;

#if MONO
            _cachedSettings = null;
#else
            try
            {
                _cachedSettings = new JsonSerializerSettings
                {
                    MissingMemberHandling = MissingMemberHandling.Ignore,
                    NullValueHandling = NullValueHandling.Ignore
                };
            }
            catch (Exception ex)
            {
                MelonLoader.MelonLogger.Warning($"[PackRat] Error creating JsonSerializerSettings: {ex.Message}");
                _cachedSettings = null;
            }
#endif
        }

#if MONO
        return null;
#else
        return (JsonSerializerSettings)_cachedSettings;
#endif
    }

    /// <summary>
    /// Safely serializes an object to JSON string.
    /// </summary>
    public static string SerializeObject<T>(T value)
    {
        try
        {
#if !MONO
            return STJ.JsonSerializer.Serialize(value);
#else
            return JsonConvert.SerializeObject(value);
#endif
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Error($"[PackRat] Error serializing object to JSON: {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Safely deserializes JSON string to object.
    /// </summary>
    public static T DeserializeObject<T>(string json)
    {
        try
        {
#if !MONO
            return STJ.JsonSerializer.Deserialize<T>(json);
#else
            return JsonConvert.DeserializeObject<T>(json);
#endif
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Error($"[PackRat] Error deserializing JSON to {typeof(T).Name}: {ex.Message}");
            throw;
        }
    }

#if !MONO
    /// <summary>
    /// Fallback for PopulateObject under IL2CPP using System.Text.Json.
    /// Deserializes JSON and copies matching properties to the target object.
    /// </summary>
    public static void PopulateObject(string json, object target)
    {
        if (target == null) return;
        var targetType = target.GetType();
        var deserialized = STJ.JsonSerializer.Deserialize(json, targetType);
        if (deserialized == null) return;

        foreach (var prop in targetType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!prop.CanRead || !prop.CanWrite) continue;
            try
            {
                var val = prop.GetValue(deserialized);
                if (val != null)
                    prop.SetValue(target, val);
            }
            catch { }
        }

        foreach (var field in targetType.GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            try
            {
                var val = field.GetValue(deserialized);
                if (val != null)
                    field.SetValue(target, val);
            }
            catch { }
        }
    }
#else
    /// <summary>
    /// Populates an object from JSON string using Newtonsoft.Json.
    /// </summary>
    public static void PopulateObject(string json, object target)
    {
        try
        {
            JsonConvert.PopulateObject(json, target);
        }
        catch (Exception ex)
        {
            MelonLoader.MelonLogger.Error($"[PackRat] Error populating object from JSON: {ex.Message}");
            throw;
        }
    }
#endif
}
