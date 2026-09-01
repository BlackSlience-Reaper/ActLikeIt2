using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using ActLikeIt2.Events;
using MegaCrit.Sts2.addons.mega_text;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;

namespace ActLikeIt2.Patches; // TODO：刷新选项第一次加载位置偏下，刷新后才正常

[HarmonyPatch(typeof(NEventLayout), nameof(NEventLayout.AddOptions))]
internal static class ForkEventLayoutPatch
{
	private const int MaxVisibleOptions = 4;
	private const float OptionHeight = 100f;
	private const string ScrollName = "ActLikeIt2ForkOptionsScroll";
	private const string NativeScrollbarName = "ActLikeIt2ForkNativeScrollbar";
	private const string NativeScrollbarScenePath = "res://scenes/ui/scrollbar.tscn";
	private const string RefreshButtonName = "ActLikeIt2ForkRefreshButton";
	private const int AlignmentFrameCount = 30;
	private const float NativeScrollbarEdgeSize = 48f;
	private const float NativeScrollbarGap = 12f;
	private const int RefreshPatchMargin = 48;
	private const float RefreshButtonLeft = 540f;
	private static readonly Vector2 RefreshButtonSize = new(264f, OptionHeight);

	private static readonly AccessTools.FieldRef<NEventLayout, EventModel> EventRef =
		AccessTools.FieldRefAccess<NEventLayout, EventModel>("_event");
	private static readonly AccessTools.FieldRef<NEventLayout, VBoxContainer> OptionsRef =
		AccessTools.FieldRefAccess<NEventLayout, VBoxContainer>("_optionsContainer");

	[HarmonyPostfix]
	private static void Postfix(NEventLayout __instance)
	{
		if (EventRef(__instance) is not ForkInTheRoadEvent)
		{
			return;
		}

		VBoxContainer options = OptionsRef(__instance);
		NEventOptionButton? refreshButton = options
			.GetChildren()
			.OfType<NEventOptionButton>()
			.FirstOrDefault(IsRefreshButton);
		if (refreshButton != null)
		{
			options.RemoveChild(refreshButton);
			__instance.AddChild(refreshButton);
		}

		int optionCount = options.GetChildCount();
		ScrollContainer? forkScroll = null;
		if (options.GetParent() is ScrollContainer existingScroll)
		{
			Configure(existingScroll, options, optionCount);
			forkScroll = existingScroll;
		}
		else if (optionCount > MaxVisibleOptions && options.GetParent() is Container parent)
		{
			int childIndex = options.GetIndex();
			parent.RemoveChild(options);

			var scroll = new ScrollContainer
			{
				Name = ScrollName,
				ClipContents = true,
				FollowFocus = true,
				FocusMode = Control.FocusModeEnum.None,
				MouseFilter = Control.MouseFilterEnum.Pass,
				HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
				VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
				SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
			};
			parent.AddChild(scroll);
			parent.MoveChild(scroll, childIndex);
			scroll.AddChild(options);
			Configure(scroll, options, optionCount);
			forkScroll = scroll;
		}

		if (forkScroll != null)
		{
			ConfigureNativeScrollbar(__instance, forkScroll, optionCount);
		}

		if (refreshButton != null)
		{
			ConfigureRefreshButton(__instance, refreshButton, options);
		}
	}

	private static void Configure(
		ScrollContainer scroll,
		VBoxContainer options,
		int optionCount)
	{
		float visibleHeight = OptionHeight * Math.Min(MaxVisibleOptions, Math.Max(1, optionCount));
		scroll.CustomMinimumSize = new Vector2(800f, visibleHeight);
		scroll.VerticalScrollMode = optionCount > MaxVisibleOptions
			? ScrollContainer.ScrollMode.Auto
			: ScrollContainer.ScrollMode.Disabled;
		options.CustomMinimumSize = new Vector2(800f, OptionHeight * Math.Max(1, optionCount));
		options.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
	}

	private static void ConfigureNativeScrollbar(
		NEventLayout layout,
		ScrollContainer scroll,
		int optionCount)
	{
		VScrollBar builtInScrollbar = scroll.GetVScrollBar();
		builtInScrollbar.Modulate = Colors.Transparent;
		builtInScrollbar.MouseFilter = Control.MouseFilterEnum.Ignore;

		NScrollbar? nativeScrollbar = layout.GetNodeOrNull<NScrollbar>(NativeScrollbarName);
		if (nativeScrollbar == null)
		{
			nativeScrollbar = PreloadManager.Cache
				.GetScene(NativeScrollbarScenePath)
				.Instantiate<NScrollbar>(PackedScene.GenEditState.Disabled);
			nativeScrollbar.Name = NativeScrollbarName;
			layout.AddChild(nativeScrollbar);

			NScrollbar capturedNative = nativeScrollbar;
			nativeScrollbar.Connect(
				Godot.Range.SignalName.ValueChanged,
				Callable.From<double>(value => SyncBuiltInScrollbar(builtInScrollbar, value)));
			builtInScrollbar.Connect(
				Godot.Range.SignalName.ValueChanged,
				Callable.From<double>(_ => SyncNativeScrollbar(builtInScrollbar, capturedNative)));
		}

		UpdateNativeScrollbarLayout(scroll, nativeScrollbar, optionCount);
		SyncNativeScrollbar(builtInScrollbar, nativeScrollbar);
		TaskHelper.RunSafely(UpdateNativeScrollbarDuringInitialLayout(
			scroll,
			builtInScrollbar,
			nativeScrollbar,
			optionCount));
	}

	private static async Task UpdateNativeScrollbarDuringInitialLayout(
		ScrollContainer scroll,
		VScrollBar builtInScrollbar,
		NScrollbar nativeScrollbar,
		int optionCount)
	{
		for (int i = 0; i < AlignmentFrameCount; i++)
		{
			await nativeScrollbar.AwaitProcessFrame();
			UpdateNativeScrollbarLayout(scroll, nativeScrollbar, optionCount);
			SyncNativeScrollbar(builtInScrollbar, nativeScrollbar);
		}
	}

	private static void UpdateNativeScrollbarLayout(
		ScrollContainer scroll,
		NScrollbar nativeScrollbar,
		int optionCount)
	{
		if (!GodotObject.IsInstanceValid(scroll)
			|| !GodotObject.IsInstanceValid(nativeScrollbar)
			|| !scroll.IsInsideTree()
			|| !nativeScrollbar.IsInsideTree())
		{
			return;
		}

		bool visible = optionCount > MaxVisibleOptions;
		nativeScrollbar.Visible = visible;
		nativeScrollbar.MouseFilter = visible
			? Control.MouseFilterEnum.Stop
			: Control.MouseFilterEnum.Ignore;
		if (!visible)
		{
			return;
		}

		float trackHeight = Math.Max(1f, scroll.Size.Y - NativeScrollbarEdgeSize * 2f);
		nativeScrollbar.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		nativeScrollbar.CustomMinimumSize = Vector2.Zero;
		nativeScrollbar.Size = new Vector2(NativeScrollbarEdgeSize, trackHeight);
		nativeScrollbar.GlobalPosition = new Vector2(
			scroll.GlobalPosition.X + scroll.Size.X + NativeScrollbarGap,
			scroll.GlobalPosition.Y + NativeScrollbarEdgeSize);
	}

	private static void SyncBuiltInScrollbar(
		VScrollBar builtInScrollbar,
		double nativeValue)
	{
		double scrollRange = Math.Max(0.0, builtInScrollbar.MaxValue - builtInScrollbar.Page);
		builtInScrollbar.Value = scrollRange * nativeValue * 0.01;
	}

	private static void SyncNativeScrollbar(
		VScrollBar builtInScrollbar,
		NScrollbar nativeScrollbar)
	{
		double scrollRange = Math.Max(0.0, builtInScrollbar.MaxValue - builtInScrollbar.Page);
		double percentage = scrollRange <= 0.0
			? 0.0
			: builtInScrollbar.Value / scrollRange * 100.0;
		nativeScrollbar.SetValueWithoutAnimation(Math.Clamp(percentage, 0.0, 100.0));
	}

	private static bool IsRefreshButton(NEventOptionButton button) =>
		button.Option.TextKey.EndsWith(
			ForkInTheRoadEvent.RefreshOptionSuffix,
			StringComparison.Ordinal);

	private static void ConfigureRefreshButton(
		NEventLayout layout,
		NEventOptionButton refreshButton,
		VBoxContainer options)
	{
		refreshButton.Name = RefreshButtonName;
		foreach (NinePatchRect panel in refreshButton
			.GetChildren()
			.OfType<NinePatchRect>())
		{
			panel.PatchMarginLeft = Math.Min(panel.PatchMarginLeft, RefreshPatchMargin);
			panel.PatchMarginRight = Math.Min(panel.PatchMarginRight, RefreshPatchMargin);
		}

		refreshButton.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		refreshButton.AnchorLeft = 0f;
		refreshButton.AnchorTop = 0f;
		refreshButton.AnchorRight = 0f;
		refreshButton.AnchorBottom = 0f;
		refreshButton.CustomMinimumSize = Vector2.Zero;
		refreshButton.Position = new Vector2(
			RefreshButtonLeft,
			(layout.Size.Y - RefreshButtonSize.Y) * 0.5f);
		refreshButton.Size = RefreshButtonSize;
		refreshButton.CustomMinimumSize = RefreshButtonSize;
		refreshButton.PivotOffset = RefreshButtonSize * 0.5f;

		MegaRichTextLabel label = refreshButton.GetNode<MegaRichTextLabel>("%Text");
		label.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		label.OffsetLeft = 10f;
		label.OffsetTop = 10f;
		label.OffsetRight = -10f;
		label.OffsetBottom = -10f;
		label.HorizontalAlignment = HorizontalAlignment.Center;

		refreshButton.VoteContainer.OffsetLeft = 0f;
		refreshButton.VoteContainer.OffsetRight = RefreshButtonSize.X - 12f;

		List<NEventOptionButton> regularButtons = options
			.GetChildren()
			.OfType<NEventOptionButton>()
			.ToList();
		if (regularButtons.Count == 0)
		{
			return;
		}

		NEventOptionButton firstButton = regularButtons[0];
		AlignRefreshButtonWithFirstOption(refreshButton, firstButton);
		TaskHelper.RunSafely(AlignRefreshButtonDuringInitialLayout(
			refreshButton,
			firstButton));

		NodePath refreshPath = refreshButton.GetPath();
		NodePath firstPath = firstButton.GetPath();
		NodePath lastPath = regularButtons[^1].GetPath();
		for (int i = 0; i < regularButtons.Count; i++)
		{
			NEventOptionButton button = regularButtons[i];
			button.FocusNeighborLeft = refreshPath;
			button.FocusNeighborRight = button.GetPath();
			button.FocusNeighborTop = i > 0
				? regularButtons[i - 1].GetPath()
				: lastPath;
			button.FocusNeighborBottom = i < regularButtons.Count - 1
				? regularButtons[i + 1].GetPath()
				: firstPath;
		}

		refreshButton.FocusNeighborLeft = refreshPath;
		refreshButton.FocusNeighborRight = firstPath;
		refreshButton.FocusNeighborTop = refreshPath;
		refreshButton.FocusNeighborBottom = refreshPath;
	}

	private static async Task AlignRefreshButtonDuringInitialLayout(
		NEventOptionButton refreshButton,
		NEventOptionButton firstButton)
	{
		for (int i = 0; i < AlignmentFrameCount; i++)
		{
			await refreshButton.AwaitProcessFrame();
			AlignRefreshButtonWithFirstOption(refreshButton, firstButton);
		}
	}

	private static void AlignRefreshButtonWithFirstOption(
		NEventOptionButton refreshButton,
		NEventOptionButton firstButton)
	{
		if (!GodotObject.IsInstanceValid(refreshButton)
			|| !GodotObject.IsInstanceValid(firstButton)
			|| !refreshButton.IsInsideTree()
			|| !firstButton.IsInsideTree())
		{
			return;
		}

		Vector2 position = refreshButton.GlobalPosition;
		refreshButton.GlobalPosition = new Vector2(position.X, firstButton.GlobalPosition.Y);
	}

	internal static NEventOptionButton? FindRefreshButton(NEventLayout layout) =>
		layout
			.GetChildren()
			.OfType<NEventOptionButton>()
			.FirstOrDefault(static button => button.Name == RefreshButtonName);
}

[HarmonyPatch(typeof(NEventLayout), nameof(NEventLayout.ClearOptions))]
internal static class ForkRefreshButtonClearPatch
{
	[HarmonyPrefix]
	private static void Prefix(NEventLayout __instance)
	{
		NEventOptionButton? refreshButton = ForkEventLayoutPatch.FindRefreshButton(__instance);
		if (refreshButton == null)
		{
			return;
		}

		__instance.RemoveChild(refreshButton);
		refreshButton.QueueFree();
	}
}

[HarmonyPatch(typeof(NEventLayout), nameof(NEventLayout.DisableEventOptions))]
internal static class ForkRefreshButtonDisablePatch
{
	[HarmonyPostfix]
	private static void Postfix(NEventLayout __instance) =>
		ForkEventLayoutPatch.FindRefreshButton(__instance)?.Disable();
}

[HarmonyPatch(typeof(NEventLayout), nameof(NEventLayout.BeforeSharedOptionChosen))]
internal static class ForkRefreshButtonSharedChoicePatch
{
	[HarmonyPrefix]
	private static void Prefix(NEventLayout __instance) =>
		ForkEventLayoutPatch.FindRefreshButton(__instance)?.Disable();
}

[HarmonyPatch(typeof(NEventLayout), "OnPlayerVoteChanged")]
internal static class ForkRefreshButtonVotePatch
{
	[HarmonyPostfix]
	private static void Postfix(NEventLayout __instance) =>
		ForkEventLayoutPatch.FindRefreshButton(__instance)?.RefreshVotes();
}
