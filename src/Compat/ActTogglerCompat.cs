using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace ActLikeIt2.Compat;

/// <summary>
/// Soft dependency bridge for ActToggler weight config.
/// Reads EnabledAct1/2/3 static properties when ActToggler is loaded.
/// Weights influence only the solo fork's "random" option (PickWeightedAct); they do not
/// hide acts from the fork options themselves. Multiplayer uses the synchronized run RNG
/// uniformly because ActToggler configuration is local and is not replicated to peers.
/// </summary>
internal static class ActTogglerCompat
{
	private const string ConfigTypeName = "ActToggler.ActTogglerCode.ActTogglerConfig";

	private static bool _present;
	private static PropertyInfo? _slot1;
	private static PropertyInfo? _slot2;
	private static PropertyInfo? _slot3;
	private static bool _preselectionPatchDisabled;

	public static bool IsPresent => _present;

	public static void DisableRunPreselectionPatch()
	{
		if (_preselectionPatchDisabled)
		{
			return;
		}

		TryDiscover();
		MethodInfo? target = AccessTools.Method(typeof(ActModel), nameof(ActModel.GetRandomList));
		HarmonyLib.Patches? patchInfo = target == null ? null : Harmony.GetPatchInfo(target);
		if (target != null
			&& patchInfo?.Finalizers.Any(static patch => patch.owner == "ActToggler") == true)
		{
			new Harmony(ActLikeIt2Mod.HarmonyId)
				.Unpatch(target, HarmonyPatchType.Finalizer, "ActToggler");
			ActLikeIt2Mod.Log.Info(
				"[Fork] Disabled ActToggler's pre-run act replacement; its weights remain active for the fork's random option.");
			_preselectionPatchDisabled = true;
			return;
		}

		_preselectionPatchDisabled = _present;
	}

	public static void TryDiscover()
	{
		if (_present)
		{
			return;
		}

		Type? configType = ResolveType(ConfigTypeName);
		if (configType == null)
		{
			return;
		}

		_slot1 = configType.GetProperty("EnabledAct1", BindingFlags.Public | BindingFlags.Static);
		_slot2 = configType.GetProperty("EnabledAct2", BindingFlags.Public | BindingFlags.Static);
		_slot3 = configType.GetProperty("EnabledAct3", BindingFlags.Public | BindingFlags.Static);
		_present = _slot1 != null && _slot2 != null && _slot3 != null;
		if (_present)
		{
			ActLikeIt2Mod.Log.Info("ActToggler detected; its per-act weights will drive the solo fork's random option.");
		}
	}

	public static bool IsActEnabled(int actNumber, string actTypeName)
	{
		TryDiscover();
		if (!_present)
		{
			return true;
		}

		Dictionary<string, int> weights = ParseWeights(actNumber);
		if (weights.Count == 0)
		{
			return true;
		}

		if (weights.TryGetValue(actTypeName, out int weight))
		{
			return weight > 0;
		}

		// Unknown acts default to disabled when ActToggler has an explicit config string.
		return false;
	}

	public static ActModel? PickWeightedAct(int actNumber, IReadOnlyList<ActModel> candidates, Rng rng)
	{
		if (candidates.Count == 0)
		{
			return null;
		}

		TryDiscover();
		if (!_present)
		{
			return rng.NextItem(candidates);
		}

		Dictionary<string, int> weights = ParseWeights(actNumber);
		List<(ActModel act, int weight)> weighted = candidates
			.Select(a => (act: a, weight: weights.GetValueOrDefault(a.GetType().Name, 0)))
			.Where(x => x.weight > 0)
			.ToList();

		if (weighted.Count == 0)
		{
			return rng.NextItem(candidates);
		}

		int total = weighted.Sum(x => x.weight);
		int roll = rng.NextInt(total);
		int cumulative = 0;
		foreach ((ActModel act, int weight) in weighted)
		{
			cumulative += weight;
			if (roll < cumulative)
			{
				return act;
			}
		}

		return weighted[^1].act;
	}

	private static Dictionary<string, int> ParseWeights(int actNumber)
	{
		PropertyInfo? property = actNumber switch
		{
			1 => _slot1,
			2 => _slot2,
			3 => _slot3,
			_ => null
		};

		if (property == null)
		{
			return new Dictionary<string, int>(StringComparer.Ordinal);
		}

		string raw = property.GetValue(null) as string ?? string.Empty;
		Dictionary<string, int> weights = new(StringComparer.Ordinal);
		foreach (string token in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			string[] parts = token.Split(':', StringSplitOptions.TrimEntries);
			if (parts.Length == 2 && int.TryParse(parts[1], out int weight))
			{
				weights[parts[0]] = Math.Clamp(weight, 0, 100);
			}
			else if (parts.Length == 1)
			{
				weights[parts[0]] = 1;
			}
		}

		return weights;
	}

	private static Type? ResolveType(string fullName)
	{
		foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
		{
			Type? type = assembly.GetType(fullName, throwOnError: false);
			if (type != null)
			{
				return type;
			}
		}

		return null;
	}
}
