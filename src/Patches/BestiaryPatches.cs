using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.Bestiary;

namespace ActLikeIt2.Patches;

/// <summary>
/// Adds every ActLikeIt2 registration that is absent from ModelDb.Acts to the bestiary.
/// BaseLib acts already injected into ModelDb.Acts are skipped because the game adds them itself.
/// NBestiary.AddAct retains the game's discovered-act and discovered-monster visibility rules.
/// </summary>
#if STS2_0_111_0
[HarmonyPatch(typeof(NBestiary), "AddEvents")]
#else
[HarmonyPatch(typeof(NBestiary), "CreateEntries")]
#endif
internal static class BestiaryAddRegisteredActsPatch
{
	// 0.107.1 没有 AddEvents；必须等 CreateEntries 初始化发现集后再追加章节。
#if STS2_0_111_0
	[HarmonyPrefix]
#else
	[HarmonyPostfix]
#endif
	private static void AddRegisteredActs(NBestiary __instance)
	{
		HashSet<string> modelDbActIds = ModelDb.Acts
			.Select(static act => act.Id.Entry)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		foreach (ActRegistration registration in ActRegistry.GetAllRegistrations())
		{
			if (modelDbActIds.Add(registration.IdEntry))
			{
				__instance.AddAct(registration.CanonicalAct);
			}
		}
	}
}
