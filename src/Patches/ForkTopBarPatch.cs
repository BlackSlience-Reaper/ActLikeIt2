using ActLikeIt2.Runtime;
using HarmonyLib;

namespace ActLikeIt2.Patches;

[HarmonyPatch(
	typeof(MegaCrit.Sts2.Core.Nodes.TopBar.NTopBarMapButton),
	"OnRelease")]
internal static class ForkTopBarMapPatch
{
	[HarmonyPrefix]
	private static bool Prefix()
	{
		return !ActSelectionSession.HasPendingSelection;
	}
}
