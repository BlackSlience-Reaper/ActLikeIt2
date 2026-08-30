using System;
using System.Collections.Generic;
using System.Linq;
using ActLikeIt2.Runtime;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace ActLikeIt2;

/// <summary>
/// Central registry for act selection candidates and custom map/boss metadata.
/// Only custom acts live here: BaseLib CustomActModel acts are imported automatically and
/// other mods can call <see cref="Register"/> for explicit control. Vanilla acts are not
/// stored — the fork screen always offers the run's current act for a slot as the single
/// vanilla option (see <see cref="GetSelectableActs"/>).
/// </summary>
public static class ActRegistry
{
	private static readonly object Gate = new();
	private static readonly Dictionary<string, ActRegistration> ByEntry = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<Type, ActRegistrationOptions> ByActType = new();
	private static bool _bootstrapped;

	/// <summary>
	/// Registers an Act type during mod initialization. The canonical instance is resolved after
	/// <see cref="ModelDb.InitIds"/>, so the registering mod does not need a Harmony lifecycle patch.
	/// </summary>
	public static void Register<TAct>(
		int actNumber,
		Func<IRunState, bool>? isAvailable = null)
		where TAct : ActModel
	{
		Register<TAct>(new ActRegistrationOptions
		{
			ActNumber = actNumber,
			IsAvailable = isAvailable
		});
	}

	/// <summary>
	/// Registers an Act type and its fork configuration during mod initialization. ActLikeIt2
	/// owns ModelDb readiness and binds the canonical model after IDs have been initialized.
	/// </summary>
	public static void Register<TAct>(ActRegistrationOptions registration)
		where TAct : ActModel
	{
		ArgumentNullException.ThrowIfNull(registration);
		Validate(registration);
		if (typeof(TAct).IsAbstract)
		{
			throw new ArgumentException($"Act type '{typeof(TAct).FullName}' must be concrete.", nameof(TAct));
		}

		lock (Gate)
		{
			ByActType[typeof(TAct)] = registration;
		}
	}

	public static void Register(ActRegistration registration)
	{
		ArgumentNullException.ThrowIfNull(registration);
		ArgumentNullException.ThrowIfNull(registration.CanonicalAct);
		Validate(registration);

		lock (Gate)
		{
			RegisterResolved(registration);
		}
	}

	public static void Register(ActModel canonicalAct, int actNumber, Func<IRunState, bool>? isAvailable = null)
	{
		Register(new ActRegistration
		{
			CanonicalAct = canonicalAct,
			ActNumber = actNumber,
			IsAvailable = isAvailable
		});
	}

	public static bool TryGet(string idEntry, out ActRegistration? registration)
	{
		lock (Gate)
		{
			return ByEntry.TryGetValue(idEntry, out registration);
		}
	}

	public static IReadOnlyList<ActRegistration> GetRegistrationsForSlot(int actNumber)
	{
		EnsureBootstrapped();
		lock (Gate)
		{
			return ByEntry.Values
				.Where(r => r.ActNumber == actNumber)
				.OrderBy(r => r.IdEntry, StringComparer.Ordinal)
				.ToList();
		}
	}

	internal static IReadOnlyList<ActRegistration> GetAllRegistrations()
	{
		EnsureBootstrapped();
		lock (Gate)
		{
			return ByEntry.Values
				.OrderBy(static registration => registration.ActNumber)
				.ThenBy(static registration => registration.IdEntry, StringComparer.Ordinal)
				.ToList();
		}
	}

	internal static void OnModelDbIdsInitialized()
	{
		EnsureBootstrapped();
	}

	/// <summary>
	/// Candidates shown on the fork screen for the given 0-based act index.
	/// The run's current act for the slot is always the first (vanilla) option; the remaining
	/// options are the custom acts registered for that slot. Vanilla act models that are NOT
	/// part of this run (e.g. Underdocks when the run rolled Overgrowth) are never offered.
	/// </summary>
	public static IReadOnlyList<ActModel> GetSelectableActs(int actIndex, IRunState runState)
	{
		int actNumber = actIndex + 1;
		List<ActRegistration> registrations = GetRegistrationsForSlot(actNumber).ToList();

		ActModel? vanillaAct = actIndex >= 0 && actIndex < runState.Acts.Count
			? runState.Acts[actIndex].CanonicalInstance
			: null;

		List<ActModel> result = new();

		if (vanillaAct != null)
		{
			result.Add(vanillaAct);
		}

		foreach (ActRegistration registration in registrations.OrderBy(r => r.IdEntry, StringComparer.Ordinal))
		{
			if (vanillaAct != null
				&& string.Equals(registration.IdEntry, vanillaAct.Id.Entry, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (registration.IsAvailable != null && !registration.IsAvailable(runState))
			{
				continue;
			}

			// ActToggler weights do NOT filter fork visibility: every registered custom act
			// is offered as an option regardless of its weight (a weight of 0 only means
			// ActToggler itself will not pre-roll it). The weights only influence the
			// solo "random" option via ActTogglerCompat.PickWeightedAct. Multiplayer
			// uses uniform synchronized RNG because these local weights are not networked.

			if (!registration.CanonicalAct.IsUnlocked(runState.UnlockState) && !registration.CanonicalAct.IsDefault)
			{
				continue;
			}

			result.Add(registration.CanonicalAct);
		}

		if (result.Count == 0)
		{
			ActModel? fallback = ModelDb.Acts.FirstOrDefault(a => a.Index == actIndex && a.IsDefault)
				?? ModelDb.Acts.FirstOrDefault(a => a.Index == actIndex);
			if (fallback != null)
			{
				result.Add(fallback);
			}
		}

		return result;
	}

	internal static IReadOnlyList<ActSelectionGroup> GetSelectableActGroups(
		int actIndex,
		IRunState runState)
	{
		IReadOnlyList<ActModel> selectableActs = GetSelectableActs(actIndex, runState);
		IReadOnlyList<ActRegistration> registrations = GetRegistrationsForSlot(actIndex + 1);
		Dictionary<string, ActRegistration> registrationByEntry = registrations
			.ToDictionary(static registration => registration.IdEntry, StringComparer.OrdinalIgnoreCase);
		var groups = new List<ActSelectionGroup>();
		var groupedIndexes = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

		foreach (ActModel act in selectableActs)
		{
			registrationByEntry.TryGetValue(act.Id.Entry, out ActRegistration? registration);
			MegaCrit.Sts2.Core.Localization.LocString optionDescription =
				registration?.OptionDescription ?? act.Title;
			if (registration == null || string.IsNullOrWhiteSpace(registration.SelectionGroupId))
			{
				groups.Add(ActSelectionGroup.ForSingleAct(act, optionDescription));
				continue;
			}

			string groupId = registration.SelectionGroupId;
			if (groupedIndexes.TryGetValue(groupId, out int groupIndex))
			{
				groups[groupIndex].Add(act, optionDescription);
				continue;
			}

			groupedIndexes[groupId] = groups.Count;
			groups.Add(new ActSelectionGroup(
				groupId,
				registration.SelectionGroupTitle ?? act.Title,
				registration.SelectionGroupDescription ?? optionDescription,
				act,
				optionDescription));
		}

		return groups;
	}

	private static bool SameLocString(
		MegaCrit.Sts2.Core.Localization.LocString? left,
		MegaCrit.Sts2.Core.Localization.LocString? right)
	{
		return ReferenceEquals(left, right)
			|| (left != null
				&& right != null
				&& string.Equals(left.LocTable, right.LocTable, StringComparison.Ordinal)
				&& string.Equals(left.LocEntryKey, right.LocEntryKey, StringComparison.Ordinal));
	}

	private static void Validate(ActRegistrationOptions registration)
	{
		Validate(registration.ActNumber, registration.ForcedBossOrder, nameof(registration));
	}

	private static void Validate(ActRegistration registration)
	{
		Validate(registration.ActNumber, registration.ForcedBossOrder, nameof(registration));
	}

	private static void Validate(int actNumber, string[] forcedBossOrder, string parameterName)
	{
		if (actNumber < 1)
		{
			throw new ArgumentOutOfRangeException(parameterName, "ActNumber must be a positive integer.");
		}

		ArgumentNullException.ThrowIfNull(forcedBossOrder, parameterName);
	}

	private static void RegisterResolved(ActRegistration registration)
	{
		bool changed = !ByEntry.TryGetValue(registration.IdEntry, out ActRegistration? existing)
			|| !ReferenceEquals(existing.CanonicalAct, registration.CanonicalAct)
			|| existing.ActNumber != registration.ActNumber
			|| existing.IsAvailable != registration.IsAvailable
			|| !SameLocString(existing.OptionDescription, registration.OptionDescription)
			|| !string.Equals(existing.SelectionGroupId, registration.SelectionGroupId, StringComparison.Ordinal)
			|| !SameLocString(existing.SelectionGroupTitle, registration.SelectionGroupTitle)
			|| !SameLocString(existing.SelectionGroupDescription, registration.SelectionGroupDescription)
			|| existing.CustomCreateMap != registration.CustomCreateMap
			|| existing.CustomMapPointTypes != registration.CustomMapPointTypes
			|| !existing.ForcedBossOrder.SequenceEqual(registration.ForcedBossOrder, StringComparer.OrdinalIgnoreCase);
		ByEntry[registration.IdEntry] = registration;
		if (changed)
		{
			ActLikeIt2Mod.Log.Info($"Registered act '{registration.IdEntry}' for slot {registration.ActNumber}.");
		}
	}

	private static void ResolveTypeRegistrations()
	{
		foreach ((Type actType, ActRegistrationOptions options) in ByActType
			.OrderBy(static pair => pair.Key.FullName, StringComparer.Ordinal))
		{
			if (!ModelDb.Contains(actType))
			{
				continue;
			}

			ActModel canonicalAct = ModelDb.GetById<ActModel>(ModelDb.GetId(actType));
			RegisterResolved(options.Bind(canonicalAct));
		}
	}

	/// <summary>
	/// Lazily imports BaseLib's CustomActModel acts into the registry. Deferred until first
	/// use because custom act instances are not registered until after mod initialization.
	/// Vanilla acts are deliberately NOT seeded here: a slot's vanilla option is derived from
	/// the run's current act in <see cref="GetSelectableActs"/>.
	/// Compatibility imports are refreshed on every lookup so mods loaded after ActLikeIt2 are
	/// discovered without requiring a particular initializer order.
	/// </summary>
	private static void EnsureBootstrapped()
	{
		lock (Gate)
		{
			Compat.BaseLibCompat.ImportCustomActsIntoRegistry();
			ResolveTypeRegistrations();
			if (_bootstrapped)
			{
				return;
			}

			ActLikeIt2Mod.Log.Info($"Act registry ready with {ByEntry.Count} custom act(s).");
			_bootstrapped = true;
		}
	}
}
