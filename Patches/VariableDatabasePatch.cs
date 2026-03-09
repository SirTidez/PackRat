using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PackRat.Networking;

#if MONO
using ScheduleOne.Variables;
#else
using Il2CppScheduleOne.Variables;
#endif

namespace PackRat.Patches;

/// <summary>
/// Intercepts PackRat sync messages carried through VariableDatabase send/receive RPC logic.
/// Uses reflection-based method targeting so hash-suffixed method names remain compatible across game updates.
/// </summary>
[HarmonyPatch]
public static class VariableDatabasePatch
{
    public static IEnumerable<MethodBase> TargetMethods()
    {
        var methods = AccessTools.GetDeclaredMethods(typeof(VariableDatabase));
        for (var i = 0; i < methods.Count; i++)
        {
            var method = methods[i];
            if (method == null)
                continue;

            var isReceive = method.Name.StartsWith("RpcLogic___ReceiveValue", StringComparison.Ordinal);
            var isSend = method.Name.StartsWith("RpcLogic___SendValue", StringComparison.Ordinal);
            if (!isReceive && !isSend)
                continue;

            var parameters = method.GetParameters();
            if (parameters.Length < 3)
                continue;

            if (parameters[1].ParameterType != typeof(string) || parameters[2].ParameterType != typeof(string))
                continue;

            yield return method;
        }
    }

    [HarmonyPrefix]
    public static bool RpcLogicValue(MethodBase __originalMethod, object[] __args)
    {
        if (__args == null || __args.Length < 3)
            return true;

        var variableName = __args[1] as string;
        var value = __args[2] as string;
        var handled = BackpackStateSyncManager.HandleVariableSyncMessage(variableName, value);
        if (!handled)
            return true;

        // Keep send-side logic intact so host request messages still broadcast to clients.
        var methodName = __originalMethod?.Name;
        return methodName == null || !methodName.StartsWith("RpcLogic___ReceiveValue", StringComparison.Ordinal);
    }
}
