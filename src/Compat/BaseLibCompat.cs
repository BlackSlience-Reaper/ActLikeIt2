using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace ActLikeIt2.Compat;

/// <summary>
/// Soft dependency bridge for BaseLib CustomActModel registration.
/// </summary>
internal static class BaseLibCompat
{
	private const string CustomActModelTypeName = "BaseLib.Abstracts.CustomActModel";
	private const string CustomContentDictionaryTypeName = "BaseLib.Patches.Content.CustomContentDictionary";
	private const string ActNumberExtensionTypeName = "BaseLib.Extensions.ActModelExtensions";
	private const string CurrentGeneratingRunStateTypeName = "BaseLib.Patches.Content.CurrentGeneratingRunState";

	private static bool _present;

	public static bool IsPresent => _present;

	public static void TryDiscover()
	{
		if (_present)
		{
			return;
		}

		_present = ResolveType(CustomActModelTypeName) != null;
		if (_present)
		{
			ActLikeIt2Mod.Log.Info("BaseLib detected; CustomActModel acts will be imported.");
		}
	}

	public static void ImportCustomActsIntoRegistry()
	{
		TryDiscover();
		if (!_present)
		{
			return;
		}

		Type? dictionaryType = ResolveType(CustomContentDictionaryTypeName);
		if (dictionaryType == null)
		{
			ActLikeIt2Mod.Log.Warn("BaseLib present but CustomContentDictionary type not found.");
			return;
		}

		FieldInfo? customActsField = dictionaryType.GetField("CustomActs", BindingFlags.Public | BindingFlags.Static);
		if (customActsField?.GetValue(null) is not IEnumerable customActs)
		{
			ActLikeIt2Mod.Log.Warn("BaseLib CustomActs list unavailable.");
			return;
		}

		MethodInfo? actNumberMethod = ResolveType(ActNumberExtensionTypeName)?
			.GetMethod("ActNumber", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(ActModel) }, null);

		foreach (object customAct in customActs)
		{
			if (customAct is not ActModel actModel)
			{
				continue;
			}

			int actNumber = ResolveActNumber(actModel, actNumberMethod);
			if (actNumber < 1)
			{
				ActLikeIt2Mod.Log.Warn($"Skipping BaseLib act '{actModel.Id.Entry}' with invalid slot {actNumber}.");
				continue;
			}

			ActRegistry.Register(new ActRegistration
			{
				CanonicalAct = actModel,
				ActNumber = actNumber
			});
		}
	}

	/// <summary>
	/// BaseLib only publishes CurrentGeneratingRunState.State inside RunManager.GenerateRooms.
	/// Custom ancients read it from IsValidForAct/ShouldForceSpawn (for example to stay out of
	/// runs whose modifiers depend on Neow), and BaseLib itself uses it to avoid repeating a
	/// custom ancient across acts. Fork regeneration runs outside that method, so expose the
	/// run state for the duration of the regeneration and restore the previous value afterward.
	/// </summary>
	public static IDisposable EnterRoomGenerationScope(RunState runState)
	{
		TryDiscover();
		PropertyInfo? stateProperty = _present
			? ResolveType(CurrentGeneratingRunStateTypeName)?
				.GetProperty("State", BindingFlags.Public | BindingFlags.Static)
			: null;
		MethodInfo? setter = stateProperty?.GetSetMethod(nonPublic: true);
		if (stateProperty == null || setter == null)
		{
			if (_present)
			{
				ActLikeIt2Mod.Log.Warn("BaseLib CurrentGeneratingRunState.State unavailable; custom ancients cannot see the run during fork regeneration.");
			}

			return NoopScope.Instance;
		}

		object? previous = stateProperty.GetValue(null);
		setter.Invoke(null, new object?[] { runState });
		return new RestoreScope(() => setter.Invoke(null, new[] { previous }));
	}

	private sealed class NoopScope : IDisposable
	{
		public static readonly NoopScope Instance = new();

		public void Dispose()
		{
		}
	}

	private sealed class RestoreScope(Action restore) : IDisposable
	{
		private Action? _restore = restore;

		public void Dispose()
		{
			Interlocked.Exchange(ref _restore, null)?.Invoke();
		}
	}

	private static int ResolveActNumber(ActModel actModel, MethodInfo? actNumberMethod)
	{
		if (actNumberMethod != null)
		{
			try
			{
				return (int)actNumberMethod.Invoke(null, new object[] { actModel })!;
			}
			catch (Exception ex)
			{
				ActLikeIt2Mod.Log.Warn($"BaseLib ActNumber extension failed for '{actModel.Id.Entry}': {ex.Message}");
			}
		}

		return actModel.Index + 1;
	}

	private static Type? ResolveType(string fullName)
	{
		Type? direct = Type.GetType(fullName, throwOnError: false);
		if (direct != null)
		{
			return direct;
		}

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
