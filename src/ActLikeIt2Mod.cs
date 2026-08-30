using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace ActLikeIt2;

[ModInitializer(nameof(Initialize))]
public static class ActLikeIt2Mod
{
	public const string ModId = "ActLikeIt2";
	public const string HarmonyId = "ActLikeIt2";

	internal static Logger Log { get; } = new Logger(ModId, LogType.Generic);

	private static Harmony? _harmony;

	public static void Initialize()
	{
		try
		{
			// ForkInTheRoadEvent is a static DLL model type: the game's ModelDb.Init subtype scan
			// (ReflectionHelper.GetSubtypesInMods<AbstractModel>) instantiates it automatically.
			// Calling ModelDb.Inject here would create an early instance and then Init would
			// instantiate it again, throwing DuplicateModelException.
			// ActRegistry bootstrap is deferred until first use: ModelDb is not populated yet
			// during mod initialization, so reading ModelDb.Acts here throws KeyNotFoundException.
			Compat.BaseLibCompat.TryDiscover();
			Compat.ActTogglerCompat.TryDiscover();
			_harmony = new Harmony(HarmonyId);
			_harmony.PatchAll(typeof(ActLikeIt2Mod).Assembly);
			Compat.ActTogglerCompat.DisableRunPreselectionPatch();
			Log.Info("ActLikeIt2 initialized.");
		}
		catch (Exception ex)
		{
			Log.Error($"ActLikeIt2 failed to initialize: {ex}");
			throw;
		}
	}
}
