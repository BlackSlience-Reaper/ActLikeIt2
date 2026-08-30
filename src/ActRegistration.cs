using System;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace ActLikeIt2;

/// <summary>
/// Public registration record for a selectable Act.
/// Other mods call <see cref="ActRegistry.Register"/> with one of these.
/// </summary>
public sealed class ActRegistration
{
	public required ActModel CanonicalAct { get; init; }

	/// <summary>1-based act slot. Slots after 3 extend the run beyond the vanilla acts.</summary>
	public int ActNumber { get; init; }

	/// <summary>
	/// Optional predicate. Return false to hide this act from the fork for the current run.
	/// </summary>
	public Func<IRunState, bool>? IsAvailable { get; init; }

	/// <summary>
	/// Optional localized description shown below this act's title in the fork.
	/// Defaults to <see cref="ActModel.Title"/> when omitted.
	/// </summary>
	public LocString? OptionDescription { get; init; }

	/// <summary>
	/// Optional display group. Registrations with the same non-empty id are shown as one
	/// fork option; choosing it rolls one member with the run RNG.
	/// </summary>
	public string? SelectionGroupId { get; init; }

	/// <summary>Localized title for a grouped fork option.</summary>
	public LocString? SelectionGroupTitle { get; init; }

	/// <summary>Localized description for a grouped fork option.</summary>
	public LocString? SelectionGroupDescription { get; init; }

	/// <summary>
	/// Optional map generation override. When set, replaces <see cref="ActModel.CreateMap"/> for this act.
	/// Return null to fall through to vanilla generation.
	/// </summary>
	public Func<RunState, bool, ActMap?>? CustomCreateMap { get; init; }

	/// <summary>
	/// Optional map point type counts override for standard map generation.
	/// </summary>
	public Func<Rng, MapPointTypeCounts>? CustomMapPointTypes { get; init; }

	/// <summary>
	/// Forced boss encounter ids (ModelId.Entry), in order. First unbeaten entry is forced.
	/// Empty means no forced order.
	/// </summary>
	public string[] ForcedBossOrder { get; init; } = Array.Empty<string>();

	public string IdEntry => CanonicalAct.Id.Entry;
}
