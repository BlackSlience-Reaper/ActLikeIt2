using System.Collections;
using System.Security.Cryptography;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using System.Text.Json;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace ActLikeIt2.Loader;

[ModInitializer(nameof(Initialize))]
public static class LoaderBootstrap
{
	private const string ModId = "ActLikeIt2";
	private const string RealDllName = "ActLikeIt2.dll";
	private const string VariantManifestName = "actlikeit2-variants.manifest";
	private const string CompatTargetMarkerName = "compat-target.txt";
	private const string CompatTargetMetadataKey = "ActLikeIt2CompatibilityTarget";

	private static readonly MethodInfo? AssociateAssemblyWithModMethod =
		typeof(ModManager).GetMethod(
			"AssociateAssemblyWithMod",
			BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
			binder: null,
			[typeof(string), typeof(Assembly)],
			modifiers: null);
	private static readonly FieldInfo? ModAssembliesField =
		typeof(Mod).GetField(
			"assemblies",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
	private static readonly FieldInfo? LegacyModAssemblyField =
		typeof(Mod).GetField(
			"assembly",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

	private static Assembly? _selectedVariantAssembly;
	private static bool _legacyAssociationCallbackInstalled;
	private static bool _failureCallbackInstalled;

	public static void Initialize()
	{
		string? loaderDirectory = Path.GetDirectoryName(typeof(LoaderBootstrap).Assembly.Location);
		if (string.IsNullOrWhiteSpace(loaderDirectory))
		{
			Log.Error("[ActLikeIt2.Loader] Could not resolve loader directory.");
			return;
		}

		try
		{
			string libRoot = Path.Combine(loaderDirectory, "lib");
			if (!Directory.Exists(libRoot))
			{
				throw new DirectoryNotFoundException($"Missing lib directory: {libRoot}");
			}

			HostVersionSnapshot host = ResolveHostVersion();
			VariantCandidate? variant = PickVariant(loaderDirectory, libRoot, host.Numeric);
			if (variant == null)
			{
				throw new InvalidOperationException(
					$"No compatible variant for host {host.ReleaseLabel ?? host.Numeric?.ToString() ?? "unknown"}.");
			}

			Log.Info(
				$"[ActLikeIt2.Loader] Host version label={host.ReleaseLabel ?? "<none>"} " +
				$"numeric={host.Numeric?.ToString() ?? "<none>"}; picked variant {variant.CompatTarget}.");

			// 先只读元数据校验身份与目标，避免把打错的程序集加载进上下文后再也卸不掉。
			ValidateVariantFile(variant);
			AssemblyLoadContext context =
				AssemblyLoadContext.GetLoadContext(typeof(LoaderBootstrap).Assembly)
				?? AssemblyLoadContext.Default;
			Assembly implementation = context.LoadFromAssemblyPath(variant.DllPath);
			ValidateVariantAssembly(implementation, variant);
			// 关联之后原版每次扫描模组类型都会 GetTypes；前置缺失时必须在关联前失败，不能把异常带进全体模组的类型发现。
			GetLoadableTypes(implementation);
			if (AssociateVariantAssemblyWithGame(implementation))
				InvokeRealInitializer(implementation);
		}
		catch (Exception exception)
		{
			Log.Error($"[ActLikeIt2.Loader] Failed to load implementation: {exception}");
			MarkModFailedWhenDetected();
			throw;
		}
	}

	/// <summary>
	/// 原版吞掉初始化器异常后仍把模组记为 Loaded，依赖本模组的模组会照常加载并引用不存在的实现。
	/// 原版在初始化返回后才写状态并广播检测事件，所以只能在事件里改为 Failed，依赖方随后得到缺少依赖的报错。
	/// </summary>
	private static void MarkModFailedWhenDetected()
	{
		if (_failureCallbackInstalled) return;
		_failureCallbackInstalled = true;
		ModManager.OnModDetected += OnFailedModDetected;
	}

	private static void OnFailedModDetected(Mod mod)
	{
		if (!string.Equals(ReadManifestId(mod), ModId, StringComparison.Ordinal)) return;
		ModManager.OnModDetected -= OnFailedModDetected;
		mod.state = ModLoadState.Failed;
		Log.Error("[ActLikeIt2.Loader] Marked mod as failed so dependent mods are not loaded.");
	}

	private static void ValidateVariantFile(VariantCandidate variant)
	{
		using FileStream stream = File.OpenRead(variant.DllPath);
		using PEReader peReader = new(stream);
		MetadataReader reader = peReader.GetMetadataReader();
		AssemblyDefinition definition = reader.GetAssemblyDefinition();
		string identity = reader.GetString(definition.Name);
		if (!string.Equals(identity, Path.GetFileNameWithoutExtension(RealDllName), StringComparison.Ordinal))
		{
			throw new BadImageFormatException(
				$"Variant identity is {identity}, expected {Path.GetFileNameWithoutExtension(RealDllName)}.");
		}

		string? embeddedTarget = null;
		foreach (CustomAttributeHandle handle in definition.GetCustomAttributes())
		{
			CustomAttribute attribute = reader.GetCustomAttribute(handle);
			if (!IsAssemblyMetadataAttribute(reader, attribute.Constructor)) continue;
			BlobReader blob = reader.GetBlobReader(attribute.Value);
			if (blob.ReadUInt16() != 1) continue;
			if (string.Equals(blob.ReadSerializedString(), CompatTargetMetadataKey, StringComparison.Ordinal))
			{
				embeddedTarget = blob.ReadSerializedString();
				break;
			}
		}

		if (!string.Equals(embeddedTarget, variant.CompatTarget, StringComparison.Ordinal))
		{
			throw new BadImageFormatException(
				$"Variant metadata is {embeddedTarget ?? "<missing>"}, expected {variant.CompatTarget}.");
		}
	}

	private static bool IsAssemblyMetadataAttribute(MetadataReader reader, EntityHandle constructor)
	{
		if (constructor.Kind != HandleKind.MemberReference) return false;
		MemberReference member = reader.GetMemberReference((MemberReferenceHandle)constructor);
		if (member.Parent.Kind != HandleKind.TypeReference) return false;
		TypeReference type = reader.GetTypeReference((TypeReferenceHandle)member.Parent);
		return reader.StringComparer.Equals(type.Namespace, "System.Reflection")
			&& reader.StringComparer.Equals(type.Name, nameof(AssemblyMetadataAttribute));
	}

	private static void ValidateVariantAssembly(Assembly assembly, VariantCandidate variant)
	{
		string? identity = assembly.GetName().Name;
		if (!string.Equals(identity, Path.GetFileNameWithoutExtension(RealDllName), StringComparison.Ordinal))
		{
			throw new BadImageFormatException(
				$"Variant identity is {identity ?? "<missing>"}, expected ActLikeIt2.");
		}

		string? embeddedTarget = assembly
			.GetCustomAttributes<AssemblyMetadataAttribute>()
			.FirstOrDefault(attribute =>
				string.Equals(attribute.Key, CompatTargetMetadataKey, StringComparison.Ordinal))
			?.Value;
		if (!string.Equals(embeddedTarget, variant.CompatTarget, StringComparison.Ordinal))
		{
			throw new BadImageFormatException(
				$"Variant metadata is {embeddedTarget ?? "<missing>"}, expected {variant.CompatTarget}.");
		}
	}

	private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
	{
		try
		{
			return assembly.GetTypes();
		}
		catch (ReflectionTypeLoadException exception)
		{
			throw new AggregateException("变体类型加载不完整，停止初始化。", exception.LoaderExceptions.OfType<Exception>());
		}
	}

	private static bool AssociateVariantAssemblyWithGame(Assembly assembly)
	{
		_selectedVariantAssembly = assembly;
		if (IsAssemblyAssociatedWithMod(assembly)) return true;
		if (AssociateAssemblyWithModMethod != null)
		{
			AssociateAssemblyWithModMethod.Invoke(null, [ModId, assembly]);
			if (IsAssemblyAssociatedWithMod(assembly))
			{
				Log.Info("[ActLikeIt2.Loader] Official assembly association; no reflection bridge.");
				return true;
			}
			throw new InvalidOperationException("Official assembly association did not register the implementation.");
		}
		// STS2 0.107.1 在 initializer 返回后覆盖 Mod.assembly；检测完成后才关联并初始化。
		if (LegacyModAssemblyField != null && !_legacyAssociationCallbackInstalled)
		{
			ModManager.OnModDetected += OnLegacyModDetected;
			_legacyAssociationCallbackInstalled = true;
			return false;
		}
		throw new MissingMemberException("No supported assembly association API; refusing partial type discovery.");
	}

	private static void OnLegacyModDetected(Mod mod)
	{
		if (_selectedVariantAssembly == null
			|| !string.Equals(ReadManifestId(mod), ModId, StringComparison.Ordinal))
		{
			return;
		}

		if (mod.state != ModLoadState.Loaded) return;
		// 原版要等全部模组加载完才首次扫描 ModTypes，并且只取 Loaded 模组的 Mod.assembly；
		// 这里改写之后扫描到的就是实现程序集，不需要额外桥接。
		LegacyModAssemblyField!.SetValue(mod, _selectedVariantAssembly);
		ModManager.OnModDetected -= OnLegacyModDetected;
		_legacyAssociationCallbackInstalled = false;
		Log.Info(
			$"[ActLikeIt2.Loader] Associated variant " +
			$"{_selectedVariantAssembly.GetName().Name} with the STS2 0.107.x mod record.");
		try { InvokeRealInitializer(_selectedVariantAssembly); }
		catch (Exception ex)
		{
			mod.state = ModLoadState.Failed;
			Log.Error($"[ActLikeIt2.Loader] Legacy initialization failed: {ex}");
		}
	}

	private static bool IsAssemblyAssociatedWithMod(Assembly assembly)
	{
		return TryFindMod(out Mod? mod)
			&& ModAssembliesField?.GetValue(mod) is IList assemblies
			&& assemblies.Cast<object>().Any(item => ReferenceEquals(item, assembly));
	}

	private static bool TryFindMod(out Mod? mod)
	{
		mod = ModManager.Mods.FirstOrDefault(candidate =>
			string.Equals(ReadManifestId(candidate), ModId, StringComparison.Ordinal));
		return mod != null;
	}

	private static string? ReadManifestId(Mod mod)
	{
		FieldInfo? manifestField = typeof(Mod).GetField(
			"manifest",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		object? manifest = manifestField?.GetValue(mod);
		FieldInfo? idField = manifest?.GetType().GetField(
			"id",
			BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		return idField?.GetValue(manifest) as string;
	}

	private static void InvokeRealInitializer(Assembly assembly)
	{
		foreach (Type type in GetLoadableTypes(assembly))
		{
			ModInitializerAttribute? attribute = type.GetCustomAttribute<ModInitializerAttribute>();
			if (attribute == null)
			{
				continue;
			}

			MethodInfo? initializer = type.GetMethod(
				attribute.initializerMethod,
				BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			if (initializer == null)
			{
				throw new MissingMethodException(
					$"{type.FullName}.{attribute.initializerMethod} was not found.");
			}

			initializer.Invoke(null, null);
			Log.Info(
				$"[ActLikeIt2.Loader] Invoked implementation initializer " +
				$"{type.FullName}.{initializer.Name}.");
			return;
		}

		throw new MissingMethodException(
			$"No {nameof(ModInitializerAttribute)} was found in {assembly.FullName}.");
	}

	private static VariantCandidate? PickVariant(
		string loaderDirectory,
		string libRoot,
		Version? host)
	{
		List<VariantCandidate> variants = LoadVariantManifest(loaderDirectory, libRoot)
			.OrderBy(candidate => candidate.Version)
			.ToList();
		if (host == null)
			throw new InvalidOperationException("无法识别宿主版本，拒绝猜测兼容变体。");

		return variants.LastOrDefault(candidate => candidate.Version <= host);
	}

	private static List<VariantCandidate> LoadVariantManifest(
		string loaderDirectory,
		string libRoot)
	{
		string manifestPath = Path.Combine(loaderDirectory, VariantManifestName);
		if (!File.Exists(manifestPath))
		{
			throw new FileNotFoundException("Missing variant manifest.", manifestPath);
		}

		BundleVariantManifest? manifest = JsonSerializer.Deserialize<BundleVariantManifest>(
			File.ReadAllText(manifestPath),
			new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
		if (manifest?.Variants == null || manifest.Variants.Count == 0)
		{
			throw new InvalidDataException($"Variant manifest contains no variants: {manifestPath}");
		}

		string fullLibRoot = Path.GetFullPath(libRoot);
		return manifest.Variants
			.Select(entry => CreateVariantCandidate(loaderDirectory, fullLibRoot, entry))
			.ToList();
	}

	private static VariantCandidate CreateVariantCandidate(
		string loaderDirectory,
		string fullLibRoot,
		BundleVariantEntry entry)
	{
		string compatTarget = entry.CompatTarget?.Trim() ?? string.Empty;
		if ((compatTarget != "0.107.1" && compatTarget != "0.111.0") || !TryParseVersion(compatTarget, out Version version))
		{
			throw new InvalidDataException($"Invalid compatibility target '{entry.CompatTarget}'.");
		}

		string expectedDirectory = Path.Combine("lib", compatTarget);
		string relativeDirectory = entry.Directory?.Trim() ?? string.Empty;
		if (!string.Equals(
			relativeDirectory.Replace('\\', '/'),
			expectedDirectory.Replace('\\', '/'),
			StringComparison.Ordinal))
		{
			throw new InvalidDataException(
				$"Variant directory is '{relativeDirectory}', expected '{expectedDirectory}'.");
		}

		string variantDirectory = Path.GetFullPath(Path.Combine(loaderDirectory, relativeDirectory));
		if (!IsUnderDirectory(variantDirectory, fullLibRoot)
			|| !string.Equals(Path.GetFileName(variantDirectory), compatTarget, StringComparison.Ordinal))
		{
			throw new InvalidDataException($"Unsafe variant directory '{relativeDirectory}'.");
		}

		string markerPath = Path.Combine(variantDirectory, CompatTargetMarkerName);
		if (!File.Exists(markerPath)
			|| !string.Equals(File.ReadAllText(markerPath).Trim(), compatTarget, StringComparison.Ordinal))
		{
			throw new InvalidDataException($"Missing or mismatched marker: {markerPath}");
		}

		string assemblyName = entry.Assembly?.Trim() ?? string.Empty;
		if (!string.Equals(assemblyName, RealDllName, StringComparison.Ordinal))
		{
			throw new InvalidDataException($"Unexpected variant assembly '{assemblyName}'.");
		}

		string dllPath = Path.Combine(variantDirectory, assemblyName);
		if (!File.Exists(dllPath))
		{
			throw new FileNotFoundException("Missing variant assembly.", dllPath);
		}

		string actualHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(dllPath)));
		if (!string.Equals(actualHash, entry.Sha256, StringComparison.OrdinalIgnoreCase))
			throw new InvalidDataException($"变体 SHA-256 不匹配：{dllPath}");
		return new VariantCandidate(compatTarget, version, dllPath);
	}

	private static bool IsUnderDirectory(string path, string root)
	{
		string relative = Path.GetRelativePath(root, path);
		return !Path.IsPathRooted(relative)
			&& !string.Equals(relative, "..", StringComparison.Ordinal)
			&& !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
	}

	private static HostVersionSnapshot ResolveHostVersion()
	{
		string? fallbackLabel = null;
		try
		{
			string? label = ReleaseInfoManager.Instance.ReleaseInfo?.Version;
			if (TryCaptureVersion(label, ref fallbackLabel, out HostVersionSnapshot snapshot))
			{
				return snapshot;
			}
		}
		catch
		{
		}

		foreach (string path in GetPublishedReleaseInfoPaths())
		{
			if (TryReadJsonVersion(path, ref fallbackLabel, out HostVersionSnapshot snapshot))
			{
				return snapshot;
			}
		}

		// sts2 程序集版本在各发行版都是 0.1.0.0，不能代表游戏版本；读不到发行版本就按未知宿主失败。
		return new HostVersionSnapshot(null, fallbackLabel);
	}

	private static IEnumerable<string> GetPublishedReleaseInfoPaths()
	{
		string? executablePath = TryCallGodotOsString("GetExecutablePath");
		string? executableDirectory = string.IsNullOrWhiteSpace(executablePath)
			? null
			: Path.GetDirectoryName(executablePath);
		if (string.IsNullOrWhiteSpace(executableDirectory))
		{
			yield break;
		}

		if (string.Equals(TryCallGodotOsString("GetName"), "macOS", StringComparison.Ordinal))
		{
			yield return Path.Combine(executableDirectory, "..", "Resources", "release_info.json");
		}
		yield return Path.Combine(executableDirectory, "release_info.json");
	}

	private static string? TryCallGodotOsString(string methodName)
	{
		try
		{
			Type? osType =
				Type.GetType("Godot.OS, GodotSharp", throwOnError: false)
				?? AppDomain.CurrentDomain.GetAssemblies()
					.Select(assembly => assembly.GetType("Godot.OS", throwOnError: false))
					.FirstOrDefault(type => type != null);
			MethodInfo? method = osType?.GetMethod(
				methodName,
				BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
			return method?.Invoke(null, null) as string;
		}
		catch
		{
			return null;
		}
	}

	private static bool TryReadJsonVersion(
		string path,
		ref string? fallbackLabel,
		out HostVersionSnapshot snapshot)
	{
		snapshot = default;
		try
		{
			if (!File.Exists(path))
			{
				return false;
			}

			using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
			return document.RootElement.TryGetProperty("version", out JsonElement version)
				&& TryCaptureVersion(version.GetString(), ref fallbackLabel, out snapshot);
		}
		catch
		{
			return false;
		}
	}

	private static bool TryCaptureVersion(
		string? label,
		ref string? fallbackLabel,
		out HostVersionSnapshot snapshot)
	{
		snapshot = default;
		if (string.IsNullOrWhiteSpace(label))
		{
			return false;
		}

		fallbackLabel ??= label;
		if (!TryParseVersion(label, out Version version))
		{
			return false;
		}

		snapshot = new HostVersionSnapshot(version, label);
		return true;
	}

	private static bool TryParseVersion(string text, out Version version)
	{
		string value = text.Trim();
		int suffixIndex = value.IndexOfAny(['-', '+']);
		if (suffixIndex >= 0)
		{
			value = value[..suffixIndex].Trim();
		}
		if (value.Length >= 2
			&& (value[0] == 'v' || value[0] == 'V')
			&& char.IsDigit(value[1]))
		{
			value = value[1..];
		}

		if (Version.TryParse(value, out Version? parsed))
		{
			version = parsed;
			return true;
		}

		version = new Version(0, 0);
		return false;
	}

	private sealed record VariantCandidate(string CompatTarget, Version Version, string DllPath);
	private readonly record struct HostVersionSnapshot(Version? Numeric, string? ReleaseLabel);

	private sealed class BundleVariantManifest
	{
		public List<BundleVariantEntry>? Variants { get; set; }
	}

	private sealed class BundleVariantEntry
	{
		public string? CompatTarget { get; set; }
		public string? Directory { get; set; }
		public string? Assembly { get; set; }
		public string? Sha256 { get; set; }
	}
}
