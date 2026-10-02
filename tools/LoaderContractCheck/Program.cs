using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json.Nodes;

// 只调用加载器的文件校验与选择方法；不调用 Initialize，不安装 Harmony，不运行游戏。
if (args.Length != 2) throw new ArgumentException("LoaderContractCheck <发行目录> <game-refs目录>");
string bundle = Path.GetFullPath(args[0]);
string id = Path.GetFileName(bundle);
AssemblyLoadContext.Default.Resolving += (_, name) => File.Exists(Path.Combine(args[1], name.Name + ".dll")) ? AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(args[1], name.Name + ".dll")) : null;
Assembly loader = AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(bundle, id + ".dll"));
Type type = loader.GetType(id + ".Loader.LoaderBootstrap", throwOnError: true)!;
MethodInfo pick = type.GetMethod("PickVariant", BindingFlags.Static | BindingFlags.NonPublic)!;
string temp = Path.Combine(Path.GetTempPath(), "lor-loader-check-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
int checks = 0;
try
{
    foreach (string file in Directory.EnumerateFiles(bundle, "*", SearchOption.AllDirectories).Where(f => !f.EndsWith(".pck")))
    {
        string destination = Path.Combine(temp, Path.GetRelativePath(bundle, file));
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!); File.Copy(file, destination);
    }
    string? Pick(Version? host)
    {
        object? candidate = pick.Invoke(null, [temp, Path.Combine(temp, "lib"), host]);
        return candidate?.GetType().GetProperty("CompatTarget")?.GetValue(candidate)?.ToString();
    }
    void Expect(Version host, string? target)
    {
        if (Pick(host) != target) throw new Exception($"宿主 {host} 选择错误"); checks++;
    }
    void Reject(Action action)
    {
        try { action(); }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidDataException or InvalidOperationException
            or BadImageFormatException or FileNotFoundException or System.Text.Json.JsonException) { checks++; return; }
        throw new Exception("损坏或未知配置未被拒绝");
    }
    MethodInfo validate = type.GetMethod("ValidateVariantFile", BindingFlags.Static | BindingFlags.NonPublic)!;
    object? Candidate(Version host) => pick.Invoke(null, [temp, Path.Combine(temp, "lib"), host]);
    string Sha(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
    void ReplaceVariantDll(string target, string sourceDll, string manifestPath, string originalManifest)
    {
        string dll = Path.Combine(temp, "lib", target, id + ".dll");
        File.Copy(sourceDll, dll, overwrite: true);
        JsonNode parsed = JsonNode.Parse(originalManifest)!;
        foreach (JsonNode? variant in parsed["variants"]!.AsArray())
            if ((string?)variant!["compatTarget"] == target) variant["sha256"] = Sha(dll);
        File.WriteAllText(manifestPath, parsed.ToJsonString());
    }
    Expect(new Version(0,106,0), null);
    Expect(new Version(0,107,1), "0.107.1");
    Expect(new Version(0,109,0), "0.107.1");
    Expect(new Version(0,111,0), "0.111.0");
    Expect(new Version(0,112,0), "0.111.0");
    Reject(() => Pick(null));
    string manifest = Path.Combine(temp, id.ToLowerInvariant() + "-variants.manifest");
    string original = File.ReadAllText(manifest);
    foreach (var change in new[] { ("directory", "../outside"), ("compatTarget", "0.110.0"), ("sha256", new string('0',64)), ("assembly", "Wrong.dll") })
    {
        JsonNode parsed = JsonNode.Parse(original)!; parsed["variants"]![0]![change.Item1] = change.Item2;
        File.WriteAllText(manifest, parsed.ToJsonString()); Reject(() => Pick(new Version(0,111,0)));
    }
    File.WriteAllText(manifest,original);
    // 两个真实变体都要通过加载前的元数据校验。
    foreach (Version host in new[] { new Version(0,107,1), new Version(0,111,0) })
    {
        validate.Invoke(null, [Candidate(host)]); checks++;
    }
    // 哈希与清单一致但程序集打错：目标元数据不符、身份不符都必须在加载前拒绝。
    string backup107 = Path.Combine(temp, "variant-0.107.1.bak");
    File.Copy(Path.Combine(temp, "lib", "0.107.1", id + ".dll"), backup107);
    ReplaceVariantDll("0.107.1", Path.Combine(bundle, "lib", "0.111.0", id + ".dll"), manifest, original);
    Reject(() => validate.Invoke(null, [Candidate(new Version(0,107,1))]));
    ReplaceVariantDll("0.107.1", Path.Combine(bundle, id + ".dll"), manifest, original);
    Reject(() => validate.Invoke(null, [Candidate(new Version(0,107,1))]));
    File.Copy(backup107, Path.Combine(temp, "lib", "0.107.1", id + ".dll"), overwrite: true);
    File.WriteAllText(manifest, original);
    // 清单缺失、为空或损坏都必须失败，不能退回猜测。
    File.WriteAllText(manifest, "{\"variants\":[]}");
    Reject(() => Pick(new Version(0,111,0)));
    File.WriteAllText(manifest, "{ not json");
    Reject(() => Pick(new Version(0,111,0)));
    File.Delete(manifest);
    Reject(() => Pick(new Version(0,111,0)));
    File.WriteAllText(manifest, original);
    File.Delete(Path.Combine(temp, "lib", "0.111.0", id + ".dll"));
    Reject(() => Pick(new Version(0,111,0)));
    File.Copy(Path.Combine(bundle, "lib", "0.111.0", id + ".dll"), Path.Combine(temp, "lib", "0.111.0", id + ".dll"));
    File.WriteAllText(Path.Combine(temp,"lib","0.107.1","compat-target.txt"),"0.111.0");
    Reject(() => Pick(new Version(0,111,0)));
    Console.WriteLine($"{id}: {checks} 个选择/损坏拒绝检查通过（未运行初始化）。");
}
finally { Directory.Delete(temp, recursive: true); }
