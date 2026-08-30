using System;
using System.Collections.Generic;
using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Unlocks;

namespace ActLikeIt2;

/// <summary>
/// Reusable base for a custom Act that inherits another Act's structure and presentation.
/// Override <see cref="GenerateAllEncounters"/> when the custom Act owns a different monster
/// roster; all other Act data continues to come from <see cref="TemplateAct"/> unless the
/// subclass overrides that member explicitly.
/// </summary>
public abstract class TemplateActModel : ActModel
{
	private static readonly PropertyInfo NumberOfWeakEncountersProperty =
		typeof(ActModel).GetProperty(
			"NumberOfWeakEncounters",
			BindingFlags.Instance | BindingFlags.NonPublic)
		?? throw new InvalidOperationException("ActModel.NumberOfWeakEncounters property not found.");

	private static readonly PropertyInfo BaseNumberOfRoomsProperty =
		typeof(ActModel).GetProperty(
			"BaseNumberOfRooms",
			BindingFlags.Instance | BindingFlags.NonPublic)
		?? throw new InvalidOperationException("ActModel.BaseNumberOfRooms property not found.");

	/// <summary>The canonical Act whose non-overridden data is reused by this custom Act.</summary>
	public abstract ActModel TemplateAct { get; }

	public override int Index => TemplateAct.Index;

	public override bool IsDefault => false;

	public override Color MapTraveledColor => TemplateAct.MapTraveledColor;

	public override Color MapUntraveledColor => TemplateAct.MapUntraveledColor;

	public override Color MapBgColor => TemplateAct.MapBgColor;

	public override string[] BgMusicOptions => TemplateAct.BgMusicOptions;

	public override string[] MusicBankPaths => TemplateAct.MusicBankPaths;

	public override string AmbientSfx => TemplateAct.AmbientSfx;

	protected override int NumberOfWeakEncounters =>
		ReadTemplateInt(NumberOfWeakEncountersProperty);

	protected override int BaseNumberOfRooms =>
		ReadTemplateInt(BaseNumberOfRoomsProperty);

	public override string ChestSpineResourcePath => TemplateAct.ChestSpineResourcePath;

	public override string ChestSpineSkinNameNormal => TemplateAct.ChestSpineSkinNameNormal;

	public override string ChestSpineSkinNameStroke => TemplateAct.ChestSpineSkinNameStroke;

	public override string ChestOpenSfx => TemplateAct.ChestOpenSfx;

	public override IEnumerable<EncounterModel> BossDiscoveryOrder =>
		TemplateAct.BossDiscoveryOrder;

	public override IEnumerable<AncientEventModel> AllAncients => TemplateAct.AllAncients;

	public override IEnumerable<EventModel> AllEvents => TemplateAct.AllEvents;

	public override IEnumerable<EncounterModel> GenerateAllEncounters() =>
		TemplateAct.GenerateAllEncounters();

	public override bool IsUnlocked(UnlockState unlockState) =>
		TemplateAct.IsUnlocked(unlockState);

	public override IEnumerable<AncientEventModel> GetUnlockedAncients(UnlockState state) =>
		TemplateAct.GetUnlockedAncients(state);

	protected override void ApplyActDiscoveryOrderModifications(UnlockState unlockState)
	{
		// Template discovery ordering commonly names the template's monster encounters.
		// A custom Act can override this when it owns an equivalent ordered sequence.
	}

	public override MapPointTypeCounts GetMapPointTypes(Rng mapRng) =>
		TemplateAct.GetMapPointTypes(mapRng);

	private int ReadTemplateInt(PropertyInfo property) =>
		(int)(property.GetValue(TemplateAct)
			?? throw new InvalidOperationException(
				$"Template Act '{TemplateAct.Id.Entry}' returned no value for {property.Name}."));
}
