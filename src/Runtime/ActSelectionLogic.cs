using MegaCrit.Sts2.Core.Runs;

namespace ActLikeIt2.Runtime;

internal static class ActSelectionLogic
{
	private const int FirstVictoryChoiceActIndex = 3;

	public static bool NeedsFork(int actIndex, IRunState? runState)
	{
		if (runState == null)
		{
			return false;
		}

		int candidateCount = ActRegistry.GetSelectableActs(actIndex, runState).Count;
		return ShouldOpenFork(actIndex, runState.Acts.Count, candidateCount);
	}

	public static bool ShouldOpenFork(
		int actIndex,
		int actCount,
		int candidateCount) =>
		candidateCount > 1
		|| (candidateCount > 0
			&& (actIndex >= actCount
				|| actIndex >= FirstVictoryChoiceActIndex));
}
