using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Runs;

namespace ActLikeIt2.Runtime;

/// <summary>
/// Tracks a pending act selection and signals when the player resolved the fork event.
/// The EnterAct fork patch awaits <see cref="WaitForChoiceAsync"/> after the fork event
/// room is entered, then runs the vanilla EnterAct against the chosen act.
/// </summary>
internal static class ActSelectionSession
{
	internal static readonly object ActMutationGate = new();

	private static int? _pendingActIndex;
	private static bool _choiceClaimed;
	private static bool _isMultiplayer;
	private static SynchronizationContext? _choiceContext;
	private static bool _canChooseVictory;
	private static TaskCompletionSource<ActSelectionOutcome> _choice = new();
	private static IReadOnlyList<ActSelectionGroup> _displayGroups = Array.Empty<ActSelectionGroup>();

	public static bool HasPendingSelection => _pendingActIndex.HasValue;

	public static int PendingActIndex => _pendingActIndex ?? throw new InvalidOperationException("No pending act selection.");

	public static IReadOnlyList<ActSelectionGroup> DisplayGroups => _displayGroups;

	public static bool CanChooseVictory => _canChooseVictory;

	public static void Begin(int actIndex, IRunState runState)
	{
		lock (ActMutationGate)
		{
			_pendingActIndex = actIndex;
			_choiceClaimed = false;
			_isMultiplayer = runState.Players.Count > 1;
			_choiceContext = SynchronizationContext.Current;
			_displayGroups = ActRegistry
				.GetSelectableActGroups(actIndex, runState)
				.Select(group => group.PreRoll(runState.Rng.UpFront))
				.ToList();
			_canChooseVictory = actIndex >= 3 && _displayGroups.Count > 0;
			_choice = new TaskCompletionSource<ActSelectionOutcome>();
		}
	}

	/// <summary>
	/// Claims the shared choice once per peer. EventSynchronizer invokes a shared option on
	/// every player's event instance, but all of those instances mutate the same RunState.
	/// </summary>
	public static bool TryClaimChoice(out ActSelectionClaim claim)
	{
		lock (ActMutationGate)
		{
			if (!_pendingActIndex.HasValue || _choiceClaimed)
			{
				claim = default;
				return false;
			}

			_choiceClaimed = true;
			claim = new ActSelectionClaim(_pendingActIndex.Value, _displayGroups, _isMultiplayer);
			return true;
		}
	}

	/// <summary>Task that completes when the player picks an act in the fork event.</summary>
	public static Task<ActSelectionOutcome> WaitForChoiceAsync() => _choice.Task;

	/// <summary>
	/// Called by the fork event after the chosen act has been swapped into the run and its
	/// rooms regenerated. Releases the EnterAct fork patch to run the vanilla EnterAct flow.
	/// The completion is posted to the Godot synchronization context instead of being set
	/// inline: the option callback (EventOption.Chosen) is still on the stack when this runs,
	/// and EnterAct immediately calls ExitCurrentRooms -> EventRoom.Exit ->
	/// AwaitPendingOptionTasks, which waits for that very Chosen() task. Completing inline
	/// deadlocks with a permanent black screen.
	/// </summary>
	public static void CompleteChoice()
	{
		FinishChoice(ActSelectionOutcome.EnterAct, null);
	}

	public static void CompleteVictoryChoice()
	{
		FinishChoice(ActSelectionOutcome.EnterArchitect, null);
	}

	public static void FailChoice(Exception exception)
	{
		FinishChoice(default, exception);
	}

	private static void FinishChoice(ActSelectionOutcome outcome, Exception? exception)
	{
		SynchronizationContext? context;
		TaskCompletionSource<ActSelectionOutcome> choice;
		lock (ActMutationGate)
		{
			_pendingActIndex = null;
			_choiceClaimed = false;
			_isMultiplayer = false;
			_canChooseVictory = false;
			_displayGroups = Array.Empty<ActSelectionGroup>();
			context = _choiceContext;
			_choiceContext = null;
			choice = _choice;
		}

		void Resolve()
		{
			if (exception == null)
			{
				choice.TrySetResult(outcome);
			}
			else
			{
				choice.TrySetException(exception);
			}
		}

		if (context != null)
		{
			ActLikeIt2Mod.Log.Info("[Fork] CompleteChoice: posting completion to synchronization context.");
			context.Post(_ => Resolve(), null);
		}
		else
		{
			ActLikeIt2Mod.Log.Warn("[Fork] CompleteChoice: no synchronization context captured; completing inline.");
			Resolve();
		}
	}
}

internal readonly record struct ActSelectionClaim(
	int ActIndex,
	IReadOnlyList<ActSelectionGroup> DisplayGroups,
	bool IsMultiplayer);

internal enum ActSelectionOutcome
{
	EnterAct,
	EnterArchitect
}
