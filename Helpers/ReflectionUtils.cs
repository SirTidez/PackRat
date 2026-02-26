using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace PackRat.Helpers;

/// <summary>
/// Provides generic reflection-based methods for easier cross-platform development.
/// </summary>
internal static class ReflectionUtils
{
    /// <summary>
    /// Identifies all classes derived from another class.
    /// </summary>
    /// <typeparam name="TBaseClass">The base class derived from.</typeparam>
    /// <returns>A list of all types derived from the base class.</returns>
    internal static List<Type> GetDerivedClasses<TBaseClass>()
    {
        List<Type> derivedClasses = new List<Type>();
        Assembly[] applicableAssemblies = AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => !ShouldSkipAssembly(assembly))
            .ToArray();
        foreach (Assembly assembly in applicableAssemblies)
            foreach (Type type in SafeGetTypes(assembly))
            {
                try
                {
                    if (type == null)
                        continue;
                    if (typeof(TBaseClass).IsAssignableFrom(type)
                        && type != typeof(TBaseClass)
                        && !type.IsAbstract)
                    {
                        derivedClasses.Add(type);
                    }
                }
                catch (TypeLoadException)
                {
                    continue;
                }
                catch (Exception)
                {
                    continue;
                }
            }
        return derivedClasses;
    }

    /// <summary>
    /// Gets all types by their name.
    /// </summary>
    /// <param name="typeName">The name of the type.</param>
    /// <returns>The actual type identified by the name.</returns>
    internal static Type GetTypeByName(string typeName)
    {
        try
        {
            var direct = Type.GetType(typeName, throwOnError: false, ignoreCase: false);
            if (direct != null)
                return direct;
        }
        catch { }

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
        foreach (Assembly assembly in assemblies.Where(a => !ShouldSkipAssembly(a)))
        {
            foreach (Type type in SafeGetTypes(assembly))
            {
                if (type == null)
                    continue;

                if (type.Name == typeName || type.FullName == typeName)
                    return type;
            }
        }

        foreach (Assembly assembly in assemblies)
        {
            foreach (Type type in SafeGetTypes(assembly))
            {
                if (type == null)
                    continue;

                if (type.Name == typeName || type.FullName == typeName || (type.FullName != null && type.FullName.EndsWith("." + typeName)))
                    return type;
            }
        }

        return null;
    }

    /// <summary>
    /// Determines whether to skip an assembly during reflection searches.
    /// </summary>
    /// <param name="assembly">The assembly to check.</param>
    /// <returns>Whether to skip the assembly or not.</returns>
    internal static bool ShouldSkipAssembly(Assembly assembly)
    {
        string fullName = assembly.FullName;
        if (string.IsNullOrEmpty(fullName))
            return false;

        return fullName.StartsWith("System")
               || fullName.StartsWith("Unity")
               || fullName.StartsWith("Il2Cpp")
               || fullName.StartsWith("mscorlib")
               || fullName.StartsWith("Mono.")
               || fullName.StartsWith("netstandard")
               || fullName.StartsWith("com.rlabrecque")
               || fullName.StartsWith("__Generated");
    }

    /// <summary>
    /// Safely gets types from an assembly, even if some types fail to load.
    /// </summary>
    /// <param name="asm">The assembly to get types from.</param>
    /// <returns>The types that were successfully loaded from the assembly.</returns>
    internal static IEnumerable<Type> SafeGetTypes(Assembly asm)
    {
        try
        {
            return asm.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.Where(t => t != null).Cast<Type>();
        }
        catch
        {
            return Array.Empty<Type>();
        }
    }

    /// <summary>
    /// Recursively gets fields from a class down to the object type.
    /// </summary>
    /// <param name="type">The type you want to recursively search.</param>
    /// <param name="bindingFlags">The binding flags to apply during the search.</param>
    /// <returns>All fields from the type hierarchy.</returns>
    internal static FieldInfo[] GetAllFields(Type type, BindingFlags bindingFlags)
    {
        List<FieldInfo> fieldInfos = new List<FieldInfo>();
        while (type != null && type != typeof(object))
        {
            fieldInfos.AddRange(type.GetFields(bindingFlags | BindingFlags.DeclaredOnly));
            type = type.BaseType;
        }
        return fieldInfos.ToArray();
    }

    /// <summary>
    /// Recursively searches for a method by name from a class down to the object type.
    /// </summary>
    /// <param name="type">The type you want to recursively search.</param>
    /// <param name="methodName">The name of the method you're searching for.</param>
    /// <param name="bindingFlags">The binding flags to apply during the search.</param>
    /// <returns>The method info if found, otherwise null.</returns>
    internal static MethodInfo GetMethod(Type type, string methodName, BindingFlags bindingFlags)
    {
        while (type != null && type != typeof(object))
        {
            MethodInfo method = type.GetMethod(methodName, bindingFlags);
            if (method != null)
                return method;

            type = type.BaseType;
        }

        return null;
    }

    /// <summary>
    /// Attempts to set a field or property on an object using reflection.
    /// Tries field first, then property. Handles both public and non-public members.
    /// </summary>
    /// <param name="target">The target object to set the member on.</param>
    /// <param name="memberName">The name of the field or property.</param>
    /// <param name="value">The value to set.</param>
    /// <returns><c>true</c> if the member was successfully set; otherwise, <c>false</c>.</returns>
    internal static bool TrySetFieldOrProperty(object target, string memberName, object value)
    {
        if (target == null) return false;
        var type = target.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var fi = type.GetField(memberName, flags);
        if (fi != null)
        {
            try
            {
                if (value == null || fi.FieldType.IsInstanceOfType(value))
                {
                    fi.SetValue(target, value);
                    return true;
                }
            }
            catch { }
        }

        var pi = type.GetProperty(memberName, flags);
        if (pi != null && pi.CanWrite)
        {
            try
            {
                if (value == null || pi.PropertyType.IsInstanceOfType(value))
                {
                    pi.SetValue(target, value);
                    return true;
                }
            }
            catch { }
        }

        return false;
    }

    /// <summary>
    /// Attempts to get a field or property value from an object using reflection.
    /// Tries field first, then property. Handles both public and non-public members.
    /// </summary>
    /// <param name="target">The target object to get the member from.</param>
    /// <param name="memberName">The name of the field or property.</param>
    /// <returns>The value of the member, or <c>null</c> if not found or inaccessible.</returns>
    internal static object TryGetFieldOrProperty(object target, string memberName)
    {
        if (target == null) return null;
        var type = target.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var fi = type.GetField(memberName, flags);
        if (fi != null)
        {
            try
            {
                return fi.GetValue(target);
            }
            catch { }
        }

        var pi = type.GetProperty(memberName, flags);
        if (pi != null && pi.CanRead)
        {
            try
            {
                return pi.GetValue(target);
            }
            catch { }
        }

        return null;
    }

    /// <summary>
    /// Gets the count of a list-like object (List, IList, or IL2CPP list).
    /// </summary>
    internal static int TryGetListCount(object list)
    {
        if (list == null) return 0;
        var type = list.GetType();
        var countProp = type.GetProperty("Count", BindingFlags.Public | BindingFlags.Instance);
        if (countProp != null && countProp.PropertyType == typeof(int))
        {
            try { return (int)countProp.GetValue(list); }
            catch { }
        }
        if (list is System.Collections.ICollection col)
        {
            try { return col.Count; }
            catch { }
        }
        return 0;
    }

    /// <summary>
    /// Gets the element at index from a list-like object.
    /// </summary>
    internal static object TryGetListItem(object list, int index)
    {
        if (list == null) return null;
        if (list is System.Collections.IList ilist)
        {
            try { return ilist[index]; }
            catch { return null; }
        }
        var type = list.GetType();
        var indexer = type.GetMethod("get_Item", new[] { typeof(int) })
            ?? type.GetMethod("Get", new[] { typeof(int) });
        if (indexer != null)
        {
            try { return indexer.Invoke(list, new object[] { index }); }
            catch { }
        }
        return null;
    }

    /// <summary>
    /// Gets all field/property values on the target that look like lists (have Count and indexer).
    /// Used to scan every slot list on PlayerInventory (hotbarSlots, inventorySlots, etc.).
    /// </summary>
    internal static System.Collections.Generic.List<object> TryGetAllListLikeMembers(object target)
    {
        var result = new System.Collections.Generic.List<object>();
        if (target == null) return result;
        var type = target.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        foreach (var member in type.GetFields(flags))
        {
            try
            {
                var val = member.GetValue(target);
                if (val != null && TryGetListCount(val) >= 0)
                    result.Add(val);
            }
            catch { }
        }
        foreach (var member in type.GetProperties(flags))
        {
            if (!member.CanRead) continue;
            try
            {
                var val = member.GetValue(target);
                if (val != null && TryGetListCount(val) >= 0)
                    result.Add(val);
            }
            catch { }
        }
        return result;
    }

    /// <summary>
    /// Invokes a parameterless callback on the target (e.g. slot's onItemDataChanged) to refresh UI.
    /// </summary>
    internal static void TryInvokeParameterlessCallback(object target, params string[] possibleNames)
    {
        if (target == null) return;
        foreach (var name in possibleNames)
        {
            var val = TryGetFieldOrProperty(target, name);
            if (val == null) continue;
            var del = val as Delegate;
            if (del != null)
            {
                try { del.DynamicInvoke(null); return; }
                catch { }
            }
            var method = val.GetType().GetMethod("Invoke", BindingFlags.Public | BindingFlags.Instance);
            if (method != null && method.GetParameters().Length == 0)
            {
                try { method.Invoke(val, null); return; }
                catch { }
            }
        }
    }
}
