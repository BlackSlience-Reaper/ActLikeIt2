namespace ActLikeIt2.Compat;

/// <summary>
/// Harmony instance ids of soft dependencies that patch the same asset getters as ActLikeIt2.
/// Used with <see cref="HarmonyLib.HarmonyAfter"/>; ids of absent mods are ignored by Harmony.
/// </summary>
internal static class ExternalHarmonyIds
{
	/// <summary>BaseLib's main Harmony instance (CustomActModel asset prefixes).</summary>
	public const string BaseLib = "BaseLib";

	/// <summary>RitsuLib's content-asset patcher (IModActAssetOverrides and ExternalAssetOverrideRegistry).</summary>
	public const string RitsuLibContentAssets = "com.ritsukage.sts2-RitsuLib.framework-content-assets";
}
