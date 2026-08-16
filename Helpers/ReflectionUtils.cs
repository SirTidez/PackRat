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
    private static readonly object ReflectionCacheLock = new object();
    private static readonly Dictionary<(Type Type, string Name), MemberInfo> ReadableMemberCache =
        new Dictionary<(Type Type, string Name), MemberInfo>();
    private static readonly Dictionary<Type, MemberInfo[]> ListLikeMemberCache =
        new Dictionary<Type, MemberInfo[]>();

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
    /// Returns true when <paramref name="value"/> is a string and <paramref name="memberType"/> is
    /// System.String or Il2CppSystem.String, so that we allow assignment in IL2CPP where the
    /// member type may be Il2CppSystem.String.
    /// </summary>
    private static bool IsStringAssignableTo(Type memberType, object value)
    {
        if (memberType == null || value == null)
            return false;
        if (!(value is string))
            return false;
        if (memberType == typeof(string))
            return true;
        return memberType.FullName == "Il2CppSystem.String";
    }

    /// <summary>
    /// Attempts to set a field or property on an object using reflection.
    /// Tries field first, then property. Handles both public and non-public members.
    /// In IL2CPP, allows C# string assignment to Il2CppSystem.String members and converts when needed.
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
                if (value == null || fi.FieldType.IsInstanceOfType(value) || IsStringAssignableTo(fi.FieldType, value))
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
                if (value == null || pi.PropertyType.IsInstanceOfType(value) || IsStringAssignableTo(pi.PropertyType, value))
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
    /// Attempts to set an enum field or property by enum member name.
    /// </summary>
    internal static bool TrySetEnumFieldOrProperty(object target, string memberName, string enumValueName)
    {
        if (target == null || string.IsNullOrWhiteSpace(memberName) || string.IsNullOrWhiteSpace(enumValueName))
            return false;

        var type = target.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        var fi = type.GetField(memberName, flags);
        if (fi != null && fi.FieldType.IsEnum)
        {
            try
            {
                var parsed = Enum.Parse(fi.FieldType, enumValueName, ignoreCase: true);
                fi.SetValue(target, parsed);
                return true;
            }
            catch { }
        }

        var pi = type.GetProperty(memberName, flags);
        if (pi != null && pi.CanWrite && pi.PropertyType.IsEnum)
        {
            try
            {
                var parsed = Enum.Parse(pi.PropertyType, enumValueName, ignoreCase: true);
                pi.SetValue(target, parsed);
                return true;
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
        var member = GetCachedReadableMember(type, memberName);
        if (member is FieldInfo field)
        {
            try
            {
                return field.GetValue(target);
            }
            catch { }
        }
        else if (member is PropertyInfo property)
        {
            try
            {
                return property.GetValue(target);
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
        var members = GetCachedListLikeMembers(target.GetType());
        for (var i = 0; i < members.Length; i++)
        {
            try
            {
                var member = members[i];
                var val = member is FieldInfo field ? field.GetValue(target) : ((PropertyInfo)member).GetValue(target);
                if (val != null && !ContainsReference(result, val))
                    result.Add(val);
            }
            catch { }
        }

        return result;
    }

    private static bool ContainsReference(List<object> values, object candidate)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (ReferenceEquals(values[i], candidate))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Resolves frequently used readable members without invoking their getters. Call during scene
    /// initialization to avoid cold reflection metadata work on the first gameplay input.
    /// </summary>
    internal static void PrewarmReadableMembers(Type type, params string[] memberNames)
    {
        if (type == null || memberNames == null)
            return;
        for (var i = 0; i < memberNames.Length; i++)
            GetCachedReadableMember(type, memberNames[i]);
    }

    /// <summary>
    /// Resolves the list-valued inventory members without evaluating unrelated property getters.
    /// </summary>
    internal static void PrewarmListLikeMembers(Type type)
    {
        if (type != null)
            GetCachedListLikeMembers(type);
    }

    private static MemberInfo GetCachedReadableMember(Type type, string memberName)
    {
        if (type == null || string.IsNullOrEmpty(memberName))
            return null;

        var key = (type, memberName);
        lock (ReflectionCacheLock)
        {
            if (ReadableMemberCache.TryGetValue(key, out var cached))
                return cached;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var member = (MemberInfo)type.GetField(memberName, flags);
            if (member == null)
            {
                var property = type.GetProperty(memberName, flags);
                if (property?.CanRead == true && property.GetIndexParameters().Length == 0)
                    member = property;
            }

            ReadableMemberCache[key] = member;
            return member;
        }
    }

    private static MemberInfo[] GetCachedListLikeMembers(Type type)
    {
        lock (ReflectionCacheLock)
        {
            if (ListLikeMemberCache.TryGetValue(type, out var cached))
                return cached;

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
            var members = new List<MemberInfo>();
            var fields = type.GetFields(flags);
            for (var i = 0; i < fields.Length; i++)
            {
                if (IsListLikeType(fields[i].FieldType))
                    members.Add(fields[i]);
            }

            var properties = type.GetProperties(flags);
            for (var i = 0; i < properties.Length; i++)
            {
                var property = properties[i];
                if (property.CanRead && property.GetIndexParameters().Length == 0 && IsListLikeType(property.PropertyType))
                    members.Add(property);
            }

            cached = members.ToArray();
            ListLikeMemberCache[type] = cached;
            return cached;
        }
    }

    private static bool IsListLikeType(Type type)
    {
        if (type == null || type == typeof(string))
            return false;
        if (typeof(System.Collections.IList).IsAssignableFrom(type) ||
            typeof(System.Collections.ICollection).IsAssignableFrom(type))
        {
            return true;
        }

        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        var count = type.GetProperty("Count", flags);
        if (count == null || count.PropertyType != typeof(int))
            return false;
        return type.GetMethod("get_Item", new[] { typeof(int) }) != null ||
            type.GetMethod("Get", new[] { typeof(int) }) != null;
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
