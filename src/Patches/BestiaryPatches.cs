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
[HarmonyPatch(typeof(NBestiary), "AddEvents")]
internal static class BestiaryAddRegisteredActsPatch
{
	[HarmonyPrefix]
	private static void Prefix(NBestiary __instance)
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
