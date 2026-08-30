using ActLikeIt2.Runtime;
using HarmonyLib;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace ActLikeIt2.Patches;

[HarmonyPatch(typeof(ActModel), nameof(ActModel.CreateMap))]
internal static class ActModelCreateMapPatch
{
	[HarmonyPrefix]
	private static bool Prefix(ActModel __instance, RunState runState, bool replaceTreasureWithElites, ref ActMap __result)
	{
		if (!ActRegistry.TryGet(__instance.Id.Entry, out ActRegistration? registration)
			|| registration?.CustomCreateMap == null)
		{
			return true;
		}

		ActMap? customMap = registration.CustomCreateMap(runState, replaceTreasureWithElites);
		if (customMap == null)
		{
			return true;
		}

		__result = customMap;
		return false;
	}
}

// NOTE: no patch targets ActModel.GetMapPointTypes: it is abstract in this game build,
// so Harmony cannot prepare it ("Abstract methods cannot be prepared"). CustomMapPointTypes
// is therefore not applied on the public-beta branch.

[HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateRooms))]
internal static class ActModelGenerateRoomsPatch
{
	[HarmonyPostfix]
	private static void Postfix(ActModel __instance)
	{
		RunState? runState = RunManager.Instance.State;
		if (runState == null
			|| !__instance.IsMutable
			|| !ActRegistry.TryGet(__instance.Id.Entry, out ActRegistration? registration)
			|| registration == null)
		{
			return;
		}

		ActBossOrderUtil.ApplyForcedBossOrder(__instance, runState, registration);
	}
}
