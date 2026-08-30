using System;
using Godot;
using HarmonyLib;
using ActLikeIt2.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Events;

namespace ActLikeIt2.Patches;

[HarmonyPatch(typeof(NEventLayout), nameof(NEventLayout.AddOptions))]
internal static class ForkEventLayoutPatch
{
	private const int MaxVisibleOptions = 4;
	private const float OptionHeight = 100f;
	private const string ScrollName = "ActLikeIt2ForkOptionsScroll";

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
		int optionCount = options.GetChildCount();
		if (options.GetParent() is ScrollContainer existingScroll)
		{
			Configure(existingScroll, options, optionCount);
			return;
		}

		if (optionCount <= MaxVisibleOptions || options.GetParent() is not Container parent)
		{
			return;
		}

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
}
