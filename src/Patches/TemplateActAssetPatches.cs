using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;

namespace ActLikeIt2.Patches;

/// <summary>
/// ActModel derives these resource paths from the custom model id through non-virtual getters.
/// Template acts redirect those getters to their template so they can reuse shipped assets.
/// </summary>
[HarmonyPatch]
internal static class TemplateActAssetPathPatch
{
	private static IEnumerable<MethodBase> TargetMethods()
	{
		yield return AccessTools.PropertyGetter(typeof(ActModel), nameof(ActModel.RestSiteBackgroundPath));
		yield return AccessTools.PropertyGetter(typeof(ActModel), nameof(ActModel.MapTopBgPath));
		yield return AccessTools.PropertyGetter(typeof(ActModel), nameof(ActModel.MapMidBgPath));
		yield return AccessTools.PropertyGetter(typeof(ActModel), nameof(ActModel.MapBotBgPath));
		yield return AccessTools.PropertyGetter(typeof(ActModel), nameof(ActModel.BackgroundScenePath));
	}

	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool Prefix(
		ActModel __instance,
		MethodBase __originalMethod,
		ref string __result)
	{
		if (__instance is not TemplateActModel templateAct)
		{
			return true;
		}

		ActModel template = templateAct.TemplateAct;
		__result = __originalMethod.Name switch
		{
			"get_RestSiteBackgroundPath" => template.RestSiteBackgroundPath,
			"get_MapTopBgPath" => template.MapTopBgPath,
			"get_MapMidBgPath" => template.MapMidBgPath,
			"get_MapBotBgPath" => template.MapBotBgPath,
			"get_BackgroundScenePath" => template.BackgroundScenePath,
			_ => throw new InvalidOperationException(
				"Unsupported template Act asset getter: " + __originalMethod.Name)
		};
		return false;
	}
}

[HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateBackgroundAssets))]
internal static class TemplateActBackgroundAssetsPatch
{
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool Prefix(
		ActModel __instance,
		Rng rng,
		ref BackgroundAssets __result)
	{
		if (__instance is not TemplateActModel templateAct)
		{
			return true;
		}

		__result = templateAct.TemplateAct.GenerateBackgroundAssets(rng);
		return false;
	}
}
