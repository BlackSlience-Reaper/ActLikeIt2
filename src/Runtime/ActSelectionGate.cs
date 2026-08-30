namespace ActLikeIt2.Runtime;

/// <summary>
/// One-shot gate so EnterAct runs the vanilla flow immediately after the player chose an act
/// in the fork event, instead of showing the fork again.
/// </summary>
internal static class ActSelectionGate
{
	public static bool SkipNextFork { get; set; }
}
