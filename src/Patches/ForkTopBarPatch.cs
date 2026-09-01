using ActLikeIt2.Runtime;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.sts2.Core.Nodes.TopBar;

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

internal static class ForkBossVisibility
{
	public static void HideWhileSelecting(NTopBarBossIcon bossIcon)
	{
		if (!ActSelectionSession.HasPendingSelection)
		{
			return;
		}

		bossIcon.Visible = false;
		bossIcon.FocusMode = Control.FocusModeEnum.None;
		bossIcon.MouseFilter = Control.MouseFilterEnum.Ignore;
		NHoverTipSet.Remove(bossIcon);
	}

	public static void RestoreAfterSelection(NTopBarBossIcon bossIcon)
	{
		bossIcon.Visible = true;
		bossIcon.FocusMode = Control.FocusModeEnum.All;
		bossIcon.MouseFilter = Control.MouseFilterEnum.Stop;
		bossIcon.RefreshBossIcon();
	}
}

[HarmonyPatch(typeof(NTopBarBossIcon), "OnRoomEntered")]
internal static class ForkBossRoomEnteredPatch
{
	[HarmonyPostfix]
	private static void Postfix(NTopBarBossIcon __instance) =>
		ForkBossVisibility.HideWhileSelecting(__instance);
}

[HarmonyPatch(typeof(NTopBarBossIcon), nameof(NTopBarBossIcon.RefreshBossIcon))]
internal static class ForkBossRefreshPatch
{
	[HarmonyPostfix]
	private static void Postfix(NTopBarBossIcon __instance) =>
		ForkBossVisibility.HideWhileSelecting(__instance);
}

[HarmonyPatch(typeof(NTopBarBossIcon), "OnFocus")]
internal static class ForkBossHoverPatch
{
	[HarmonyPrefix]
	private static bool Prefix() => !ActSelectionSession.HasPendingSelection;
}
