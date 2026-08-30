using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace ActLikeIt2.Runtime;

internal static class RunStateActUtil
{
	private const int DoubleBossActIndex = 2;

	private static readonly PropertyInfo ActsProperty =
		typeof(RunState).GetProperty(nameof(RunState.Acts), BindingFlags.Public | BindingFlags.Instance)
		?? throw new InvalidOperationException("RunState.Acts property not found.");
	private static readonly FieldInfo SharedAncientSubsetField =
		typeof(ActModel).GetField("_sharedAncientSubset", BindingFlags.NonPublic | BindingFlags.Instance)
		?? throw new InvalidOperationException("ActModel._sharedAncientSubset field not found.");

	public static void ReplaceActAt(RunState runState, int actIndex, ActModel mutableAct)
	{
		mutableAct.AssertMutable();
		List<ActModel> acts = runState.Acts.ToList();
		if (actIndex < 0 || actIndex > acts.Count)
		{
			throw new ArgumentOutOfRangeException(nameof(actIndex));
		}

		if (actIndex == acts.Count)
		{
			acts.Add(mutableAct);
		}
		else
		{
			if (SharedAncientSubsetField.GetValue(acts[actIndex])
				is List<AncientEventModel> sharedAncients)
			{
				mutableAct.SetSharedAncientSubset(sharedAncients);
			}
			acts[actIndex] = mutableAct;
		}
		ActsProperty.SetValue(runState, acts);
	}

	public static void RegenerateActRooms(RunState runState, int actIndex)
	{
		ActModel act = runState.Acts[actIndex];
		act.GenerateRooms(runState.Rng.UpFront, runState.UnlockState, runState.Players.Count > 1);
		if (RunManager.Instance.ShouldApplyTutorialModifications())
		{
			act.ApplyDiscoveryOrderModifications(runState.UnlockState);
		}

		// ActLikeIt2 keeps A10 double bosses on the fixed third-act slot even when
		// another mod appends later acts. Apply the rule before this act's map is created.
		ApplyThirdActDoubleBossRule(runState, actIndex, act);
	}

	private static void ApplyThirdActDoubleBossRule(
		RunState runState,
		int actIndex,
		ActModel act)
	{
		if (actIndex != DoubleBossActIndex
			|| runState.AscensionLevel < (int)AscensionLevel.DoubleBoss)
		{
			return;
		}

		EncounterModel? secondBoss = runState.Rng.UpFront.NextItem(
			act.AllBossEncounters.Where(encounter => encounter.Id != act.BossEncounter.Id));
		act.SetSecondBossEncounter(secondBoss);
	}
}
