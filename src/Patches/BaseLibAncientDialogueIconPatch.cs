using System;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Models;

namespace ActLikeIt2.Patches;

/// <summary>
/// The dialogue line reads AncientEventModel.RunHistoryIcon directly. Resolve BaseLib's
/// custom ancient icon from that instance, including when ImageHelper falls back to the
/// game's generated run-history path for an act selected through the fork.
/// </summary>
[HarmonyPatch(typeof(AncientEventModel), nameof(AncientEventModel.RunHistoryIcon), MethodType.Getter)]
internal static class BaseLibAncientDialogueIconPatch
{
	[HarmonyPrefix]
	private static bool Prefix(AncientEventModel __instance, ref Texture2D __result) =>
		!BaseLibAncientIconResolver.TryLoad(__instance, "CustomRunHistoryIconPath", ref __result);
}

[HarmonyPatch(typeof(AncientEventModel), nameof(AncientEventModel.RunHistoryIconOutline), MethodType.Getter)]
internal static class BaseLibAncientDialogueIconOutlinePatch
{
	[HarmonyPrefix]
	private static bool Prefix(AncientEventModel __instance, ref Texture2D __result) =>
		!BaseLibAncientIconResolver.TryLoad(__instance, "CustomRunHistoryIconOutlinePath", ref __result);
}

internal static class BaseLibAncientIconResolver
{
	private const string CustomAncientModelTypeName = "BaseLib.Abstracts.CustomAncientModel";

	public static bool TryLoad(AncientEventModel ancient, string propertyName, ref Texture2D result)
	{
		for (Type? type = ancient.GetType(); type != null; type = type.BaseType)
		{
			if (type.FullName != CustomAncientModelTypeName)
			{
				continue;
			}

			string? path = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)
				?.GetValue(ancient) as string;
			if (string.IsNullOrWhiteSpace(path))
			{
				return false;
			}

			path = path.Replace('\\', '/');
			if (!ResourceLoader.Exists(path))
			{
				return false;
			}

			result = PreloadManager.Cache.GetCompressedTexture2D(path);
			return true;
		}

		return false;
	}
}
