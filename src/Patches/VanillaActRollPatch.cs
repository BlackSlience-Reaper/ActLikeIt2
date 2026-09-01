using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Unlocks;

namespace ActLikeIt2.Patches;

/// <summary>
/// Keeps every custom act in ActLikeIt2's candidate registry while preventing it from
/// entering a new RunState before the fork has resolved. The chosen act is released by
/// RunStateActUtil.ReplaceActAt after the player votes.
/// </summary>
[HarmonyPatch(typeof(ActModel), nameof(ActModel.GetRandomList))]
internal static class VanillaActRollPatch
{
	[HarmonyPrefix]
	[HarmonyPriority(Priority.First)]
	private static bool Prefix(
		Rng rng,
		UnlockState unlockState,
		bool isMultiplayer,
		ref IEnumerable<ActModel> __result,
		out IReadOnlyList<ActModel> __state)
	{
		Compat.ActTogglerCompat.DisableRunPreselectionPatch();
		List<ActModel> result = BuildVanillaList(rng, unlockState, isMultiplayer);
		__state = result;
		__result = result;
		ActLikeIt2Mod.Log.Info(
			$"[Fork] Custom act preselection quarantined; vanilla run templates=[{string.Join(",", result.Select(static act => act.Id.Entry))}].");
		return false;
	}

	[HarmonyFinalizer]
	[HarmonyAfter("ActToggler")]
	[HarmonyPriority(Priority.Last)]
	private static void Finalizer(
		ref IEnumerable<ActModel> __result,
		IReadOnlyList<ActModel>? __state)
	{
		if (__state != null)
		{
			__result = __state;
		}
	}

	private static List<ActModel> BuildVanillaList(
		Rng rng,
		UnlockState unlockState,
		bool isMultiplayer)
	{
		List<ActModel> vanillaActs = ModelDb.Acts.Where(IsVanilla).ToList();
		var result = new List<ActModel>();
		foreach (IGrouping<int, ActModel> slot in vanillaActs
			.GroupBy(static act => act.Index)
			.OrderBy(static group => group.Key))
		{
			ActModel? discoveryAct = null;
			var unlocked = new List<ActModel>();
			foreach (ActModel act in slot)
			{
				if (!act.IsUnlocked(unlockState))
				{
					continue;
				}

				if (!act.IsDefault
					&& !isMultiplayer
					&& !SaveManager.Instance.Progress.DiscoveredActs.Contains(act.Id)
					&& TestMode.IsOff)
				{
					discoveryAct = act;
					break;
				}

				unlocked.Add(act);
			}

			result.Add(discoveryAct
				?? rng.NextItem(unlocked)
				?? throw new InvalidOperationException($"No unlocked vanilla act for slot {slot.Key + 1}."));
		}

		return result;
	}

	internal static ActModel? RollForSlot(
		int actIndex,
		Rng rng,
		UnlockState unlockState,
		bool isMultiplayer) =>
		BuildVanillaList(rng, unlockState, isMultiplayer)
			.FirstOrDefault(act => act.Index == actIndex);

	private static bool IsVanilla(ActModel act) =>
		act is Overgrowth or Underdocks or Hive or Glory;
}
