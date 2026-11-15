using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using MeloongCore;
using Newtonsoft.Json.Linq;

namespace PCL;

/// <summary>
/// Modrinth 上的服务端整合包（.mrpack）。
/// </summary>
public class ServerPackHit
{
	public string ProjectId = "";

	public string Slug = "";

	public string Title = "";

	public string Description = "";

	public long Downloads;

	public override string ToString()
	{
		return Title;
	}
}

/// <summary>
/// 服务端整合包：搜索、下载 .mrpack、按服务端侧过滤后装成一个独立的服务器实例。
/// </summary>
public static class ModServerPack
{
	private const string Api = "https://api.modrinth.com/v2";

	private const string UserAgent = "PCL/2.14 (ServerManager)";

	public static List<ServerPackHit> Search(string Query, string McVersion, int Limit = 20)
	{
		List<string> list = new List<string> { "[\"project_type:modpack\"]" };
		if (!string.IsNullOrEmpty(McVersion))
		{
			list.Add("[\"versions:" + McVersion + "\"]");
		}
		string Url = Api + "/search?query=" + Uri.EscapeDataString(Query ?? "") + "&facets=" + Uri.EscapeDataString("[" + string.Join(",", list) + "]") + "&limit=" + Limit + "&index=relevance";
		JObject jObject = JObject.Parse(ModServer.HttpGetText(Url));
		List<ServerPackHit> list2 = new List<ServerPackHit>();
		JArray jArray = jObject["hits"] as JArray;
		if (jArray == null)
		{
			return list2;
		}
		foreach (JToken item in jArray)
		{
			list2.Add(new ServerPackHit
			{
				ProjectId = item["project_id"]?.ToString() ?? "",
				Slug = item["slug"]?.ToString() ?? "",
				Title = item["title"]?.ToString() ?? "",
				Description = item["description"]?.ToString() ?? "",
				Downloads = item["downloads"]?.ToObject<long>() ?? 0
			});
		}
		return list2;
	}

	/// <summary>
	/// 把整合包装成一个新的服务器实例，并下载服务端需要的文件与加载器核心。
	/// </summary>
	public static ServerInstance Install(ServerPackHit Hit, string McVersion, Action<int, string> Progress)
	{
		Progress?.Invoke(1, "正在读取整合包版本信息…");
		JArray Versions = JArray.Parse(ModServer.HttpGetText(Api + "/project/" + Hit.ProjectId + "/version"));
		JObject Version = null;
		if (Versions.Count > 0)
		{
			Version = Versions[0] as JObject;
			if (!string.IsNullOrEmpty(McVersion))
			{
				foreach (JToken item in Versions)
				{
					JArray jArray = item["game_versions"] as JArray;
					if (jArray != null && jArray.Any((JToken x) => x.ToString() == McVersion))
					{
						Version = item as JObject;
						break;
					}
				}
			}
		}
		if (Version == null)
		{
			throw new Exception("该整合包没有任何可用版本。");
		}
		JArray Files = Version["files"] as JArray;
		if (Files == null || Files.Count == 0)
		{
			throw new Exception("该整合包版本没有可下载的文件。");
		}
		string Url = Files[0]["url"]?.ToString();
		string PackName = Version["name"]?.ToString() ?? Hit.Title;
		string Temp = Path.Combine(Path.GetTempPath(), "pcl-pack-" + Guid.NewGuid().ToString("N") + ".mrpack");
		Progress?.Invoke(4, "正在下载整合包：" + PackName);
		ModServer.DownloadFileWithProgress(Url, Temp, delegate(int Pct, string Msg)
		{
			Progress?.Invoke(4 + Pct * 26 / 100, Msg);
		});
		Progress?.Invoke(31, "正在解析整合包…");
		JObject Index;
		using (ZipArchive zipArchive = ZipFile.OpenRead(Temp))
		{
			ZipArchiveEntry zipArchiveEntry = zipArchive.GetEntry("modrinth.index.json");
			if (zipArchiveEntry == null)
			{
				throw new Exception("这个 .mrpack 里没有 modrinth.index.json，无法识别。");
			}
			using StreamReader streamReader = new StreamReader(zipArchiveEntry.Open(), Encoding.UTF8);
			Index = JObject.Parse(streamReader.ReadToEnd());
		}
		JObject Deps = Index["dependencies"] as JObject ?? new JObject();
		string text = Deps["minecraft"]?.ToString() ?? McVersion;
		string ServerType = "Vanilla";
		string LoaderVersion = "";
		if (Deps["fabric-loader"] != null)
		{
			ServerType = "Fabric";
			LoaderVersion = Deps["fabric-loader"].ToString();
		}
		else if (Deps["neoforge"] != null)
		{
			ServerType = "NeoForge";
			LoaderVersion = Deps["neoforge"].ToString();
		}
		else if (Deps["forge"] != null)
		{
			ServerType = "Forge";
			LoaderVersion = Deps["forge"].ToString();
		}
		else if (Deps["quilt-loader"] != null)
		{
			throw new Exception("暂不支持 Quilt 服务端（本功能支持 Fabric / Forge / NeoForge）。");
		}
		string text2 = ModServer.NewUniqueName(ModServer.SanitizeName(Index["name"]?.ToString() ?? Hit.Title));
		ServerInstance serverInstance = ModServer.Create(text2, text, ServerType, "", 2048, 25565);
		serverInstance.LoaderVersion = LoaderVersion;
		serverInstance.EulaAccepted = true;
		ModServer.WriteEula(serverInstance);
		ModServer.Save(serverInstance);
		Progress?.Invoke(33, "已创建实例 " + serverInstance.Name + "，正在下载文件…");
		List<JToken> list = new List<JToken>();
		JArray Files2 = Index["files"] as JArray;
		if (Files2 != null)
		{
			foreach (JToken item2 in Files2)
			{
				JObject jObject = item2["env"] as JObject;
				string text3 = jObject?["server"]?.ToString() ?? "required";
				if (text3 != "unsupported")
				{
					list.Add(item2);
				}
			}
		}
		int Count = list.Count;
		for (int i = 0; i < Count; i++)
		{
			JObject jObject2 = list[i] as JObject;
			string Path2 = jObject2["path"]?.ToString() ?? "";
			JArray jArray2 = jObject2["downloads"] as JArray;
			string DlUrl = jArray2 != null && jArray2.Count > 0 ? jArray2[0].ToString() : null;
			if (string.IsNullOrEmpty(Path2) || string.IsNullOrEmpty(DlUrl))
			{
				continue;
			}
			string TargetPath = Path.Combine(serverInstance.Dir, Path2.Replace('/', '\\'));
			int Current = i;
			try
			{
				ModServer.DownloadFileWithProgress(DlUrl, TargetPath, delegate(int Pct, string Msg)
				{
					Progress?.Invoke(33 + Current * 45 / Math.Max(Count, 1) + Pct * 45 / Math.Max(Count, 1) / 100, "正在下载整合包文件 " + (Current + 1) + "/" + Count + "：" + Path.GetFileName(TargetPath));
				}, Silent: true);
			}
			catch (Exception ex)
			{
				Logger.Warn(ex, "整合包文件下载失败，已跳过：" + Path2, (LogBehavior)1, "ModServerPack.cs");
			}
		}
		Progress?.Invoke(79, "正在解压整合包覆盖文件…");
		using (ZipArchive zipArchive2 = ZipFile.OpenRead(Temp))
		{
			ExtractFolder(zipArchive2, "overrides/", serverInstance.Dir);
			ExtractFolder(zipArchive2, "server-overrides/", serverInstance.Dir);
		}
		try
		{
			File.Delete(Temp);
		}
		catch (Exception)
		{
		}
		Progress?.Invoke(83, "正在下载服务端核心…");
		ModServer.DownloadServerJar(serverInstance, delegate(int Pct, string Msg)
		{
			Progress?.Invoke(83 + Pct * 17 / 100, Msg);
		});
		Progress?.Invoke(100, "整合包安装完成");
		return serverInstance;
	}

	private static void ExtractFolder(ZipArchive Zip, string Prefix, string TargetDir)
	{
		foreach (ZipArchiveEntry entry in Zip.Entries)
		{
			if (!entry.FullName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) || entry.FullName.EndsWith("/"))
			{
				continue;
			}
			string text = Path.Combine(TargetDir, entry.FullName.Substring(Prefix.Length).Replace('/', '\\'));
			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(text));
				entry.ExtractToFile(text, overwrite: true);
			}
			catch (Exception ex)
			{
				Logger.Warn(ex, "整合包覆盖文件解压失败：" + entry.FullName, (LogBehavior)1, "ModServerPack.cs");
			}
		}
	}
}
