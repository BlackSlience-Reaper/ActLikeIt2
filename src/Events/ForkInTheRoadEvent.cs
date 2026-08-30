using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ActLikeIt2.Runtime;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace ActLikeIt2.Events;

/// <summary>
/// Shared fork-in-the-road event shown before each act when multiple acts are available.
/// </summary>
public sealed class ForkInTheRoadEvent : EventModel
{
	public override bool IsShared => true;

	public override EventLayoutType LayoutType => EventLayoutType.Default;

	public override IEnumerable<LocString> GameInfoOptions
	{
		get
		{
			yield return new LocString(LocTable, Id.Entry + ".pages.INITIAL.options.RANDOM.title");
			yield return new LocString(LocTable, Id.Entry + ".pages.INITIAL.options.VICTORY.title");
		}
	}

	protected override IReadOnlyList<EventOption> GenerateInitialOptions()
	{
		int actIndex = ActSelectionSession.PendingActIndex;
		IReadOnlyList<ActSelectionGroup> groups = ActSelectionSession.DisplayGroups;
		List<EventOption> options = new();

		foreach (ActSelectionGroup group in groups)
		{
			string optionKey = $"{Id.Entry}.pages.INITIAL.options.{StringHelper.Slugify(group.Id)}";
			ActSelectionGroup captured = group;
			options.Add(new EventOption(
				this,
				() => ChooseGroup(captured),
				group.Title,
				group.Description,
				optionKey,
				System.Array.Empty<IHoverTip>()));
		}

		if (groups.Count > 1)
		{
			options.Add(new EventOption(
				this,
				ChooseRandomAct,
				$"{Id.Entry}.pages.INITIAL.options.RANDOM"));
		}

		if (ActSelectionSession.CanChooseVictory)
		{
			options.Add(new EventOption(
				this,
				ChooseVictory,
				$"{Id.Entry}.pages.INITIAL.options.VICTORY"));
		}

		return options;
	}

	private Task ChooseGroup(ActSelectionGroup group)
	{
		return ChooseAct(group.Acts[0]);
	}

	private Task ChooseRandomAct()
	{
		if (!ActSelectionSession.TryClaimChoice(out ActSelectionClaim claim))
		{
			return FinishRepeatedSharedCallback();
		}

		IReadOnlyList<ActModel> candidates = claim.DisplayGroups
			.SelectMany(static group => group.Acts)
			.ToList();
		Rng rng = Owner!.RunState.Rng.UpFront;
		List<ActModel> list = candidates.ToList();
		ActModel chosen = (!claim.IsMultiplayer
				? Compat.ActTogglerCompat.PickWeightedAct(claim.ActIndex + 1, list, rng)
				: null)
			?? rng.NextItem(list)
			?? list[0];
		return ApplyClaimedAct(claim.ActIndex, chosen);
	}

	private Task ChooseAct(ActModel canonicalAct)
	{
		if (!ActSelectionSession.TryClaimChoice(out ActSelectionClaim claim))
		{
			return FinishRepeatedSharedCallback();
		}

		return ApplyClaimedAct(claim.ActIndex, canonicalAct);
	}

	private Task ChooseVictory()
	{
		if (!ActSelectionSession.TryClaimChoice(out ActSelectionClaim claim))
		{
			return FinishRepeatedSharedCallback();
		}

		SetEventFinished(InitialDescription);
		ActLikeIt2Mod.Log.Info($"[Fork] Chose direct victory instead of registered slot {claim.ActIndex + 1}; entering the Architect room.");
		ActSelectionSession.CompleteVictoryChoice();
		return Task.CompletedTask;
	}

	private Task ApplyClaimedAct(int actIndex, ActModel canonicalAct)
	{
		RunState runState = (RunState)Owner!.RunState;
		try
		{
			ActModel mutableAct = canonicalAct.ToMutable();

			lock (ActSelectionSession.ActMutationGate)
			{
				RunStateActUtil.ReplaceActAt(runState, actIndex, mutableAct);
				RunStateActUtil.RegenerateActRooms(runState, actIndex);
			}

			ActLikeIt2Mod.Log.Info($"[Fork] Chose act '{canonicalAct.Id.Entry}' for slot {actIndex + 1}; act swapped and rooms regenerated.");

			SetEventFinished(InitialDescription);
			ActSelectionGate.SkipNextFork = true;
			ActSelectionSession.CompleteChoice();
			return Task.CompletedTask;
		}
		catch (Exception ex)
		{
			ActLikeIt2Mod.Log.Error($"[Fork] Applying act choice '{canonicalAct.Id.Entry}' failed: {ex}");
			ActSelectionSession.FailChoice(ex);
			throw;
		}
	}

	private Task FinishRepeatedSharedCallback()
	{
		SetEventFinished(InitialDescription);
		ActLikeIt2Mod.Log.Info($"[Fork] Finished repeated shared callback for player {Owner?.NetId}.");
		return Task.CompletedTask;
	}
}
