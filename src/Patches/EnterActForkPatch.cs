using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ActLikeIt2.Events;
using ActLikeIt2.Runtime;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace ActLikeIt2.Patches;

/// <summary>
/// When multiple acts are available, or an Act 4+ candidate can be declined for direct
/// victory, replaces the whole EnterAct flow:
/// show the fork event, wait for the player's choice (the event swaps the chosen act into the
/// run and regenerates its rooms), then run the vanilla EnterAct so map generation and room
/// entry all happen against the chosen act.
/// Intercepting SetActInternal alone is not enough: EnterAct continues with map marker setup
/// after the intercepted await, so skipping SetActInternal leaves the map screen unbuilt and
/// InitMarker throws for the starting coord.
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterAct))]
internal static class EnterActForkPatch
{
	[HarmonyPrefix]
	private static bool Prefix(int currentActIndex, bool doTransition, ref Task __result)
	{
		RunState? state = RunManager.Instance.State;
		if (state == null)
		{
			return true;
		}

		if (ActSelectionGate.SkipNextFork)
		{
			ActSelectionGate.SkipNextFork = false;
			ActLikeIt2Mod.Log.Info($"[Fork] SkipNextFork consumed; running vanilla EnterAct({currentActIndex}, transition={doTransition}).");
			return true;
		}

		IReadOnlyList<ActModel> candidates = ActRegistry.GetSelectableActs(currentActIndex, state);
		if (!ActSelectionLogic.ShouldOpenFork(
				currentActIndex,
				state.Acts.Count,
				candidates.Count))
		{
			ActLikeIt2Mod.Log.Info(
				$"[Fork] EnterAct({currentActIndex}, transition={doTransition}) kept vanilla flow; "
				+ $"candidateCount={candidates.Count}, candidates=[{string.Join(",", candidates.Select(static act => act.Id.Entry))}].");
			return true;
		}

		ActLikeIt2Mod.Log.Info(
			$"[Fork] EnterAct({currentActIndex}, transition={doTransition}) intercepted; "
			+ $"candidates=[{string.Join(",", candidates.Select(static act => act.Id.Entry))}].");
		__result = RunForkThenEnterAct(currentActIndex, doTransition);
		return false;
	}

	private static async Task RunForkThenEnterAct(int actIndex, bool doTransition)
	{
		try
		{
			RunState state = RunManager.Instance.State
				?? throw new InvalidOperationException("Run state disappeared before fork selection.");
			ActSelectionSession.Begin(actIndex, state);
			EventModel fork = ModelDb.Event<ForkInTheRoadEvent>();
			// Mirror the vanilla EnterAct fade handling. The character-select embark leaves
			// NTransition's overlay opaque with MouseFilter=Stop (NTransition.FadeOut), and the
			// vanilla EnterAct clears it via FadeIn(doTransition). Without the FadeIn here the
			// fork renders underneath a black, input-blocking overlay.
			await RunManager.Instance.FadeOut();
			ClearScreensBeforeFork();
			await RunManager.Instance.EnterRoom(new EventRoom(fork));
			await RunManager.Instance.FadeIn(showTransition: false);
			ActLikeIt2Mod.Log.Info($"[Fork] Fork room entered; waiting for choice.");

			// Resolved by ForkInTheRoadEvent.ChooseAct via ActSelectionSession.CompleteChoice().
			ActSelectionOutcome outcome = await ActSelectionSession.WaitForChoiceAsync();
			if (outcome == ActSelectionOutcome.EnterArchitect)
			{
				ActLikeIt2Mod.Log.Info("[Fork] Direct victory received; entering the Architect room through the native victory transition.");
				await EnterArchitectVictory();
				ActLikeIt2Mod.Log.Info("[Fork] Architect room entered.");
				return;
			}

			ActLikeIt2Mod.Log.Info($"[Fork] Act choice received; running vanilla EnterAct({actIndex}, transition={doTransition}).");

			await RunManager.Instance.EnterAct(actIndex, doTransition);
			NRun.Instance?.GlobalUi.TopBar.BossIcon.RefreshBossIcon();
			ActLikeIt2Mod.Log.Info($"[Fork] Vanilla EnterAct({actIndex}) completed.");
		}
		catch (Exception ex)
		{
			ActLikeIt2Mod.Log.Error($"[Fork] Fork flow failed: {ex}");
			throw;
		}
	}

	private static async Task EnterArchitectVictory()
	{
		RunManager runManager = RunManager.Instance;
		using (new NetLoadingHandle(runManager.NetService))
		{
			await runManager.FadeOut();
			ClearScreensBeforeFork();
			await runManager.EnterRoom(new EventRoom(ModelDb.Event<TheArchitect>()));
			await runManager.FadeIn();
		}
	}

	private static void ClearScreensBeforeFork()
	{
		// Mirror RunManager.ClearScreens before replacing the boss-to-act transition with
		// an event room. In particular, the terminal rewards screen otherwise remains in
		// NOverlayStack and its full-screen Control intercepts the fork option mouse input.
		NOverlayStack.Instance?.Clear();
		NCapstoneContainer.Instance?.Close();
		NMapScreen.Instance?.Close(animateOut: false);
	}
}

/// <summary>
/// The vanilla final act routes directly to the Architect. Registered slots after the
/// current run length continue through EnterAct, whose fork appends the selected act.
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterNextAct))]
internal static class EnterNextRegisteredActPatch
{
	[HarmonyPrefix]
	private static bool Prefix(ref Task __result)
	{
		RunState? state = RunManager.Instance.State;
		if (state == null)
		{
			return true;
		}

		int nextActIndex = state.CurrentActIndex + 1;
		if (nextActIndex < state.Acts.Count)
		{
			return true;
		}

		IReadOnlyList<ActModel> candidates =
			ActRegistry.GetSelectableActs(nextActIndex, state);
		if (candidates.Count == 0)
		{
			return true;
		}

		ActLikeIt2Mod.Log.Info(
			$"[Fork] Extending run into registered slot {nextActIndex + 1}; "
			+ $"candidates=[{string.Join(",", candidates.Select(static act => act.Id.Entry))}].");
		__result = RunManager.Instance.EnterAct(nextActIndex);
		return false;
	}
}
