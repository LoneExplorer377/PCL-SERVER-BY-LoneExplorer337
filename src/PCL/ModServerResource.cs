using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Newtonsoft.Json.Linq;

namespace PCL;

/// <summary>
/// 一条 Modrinth 搜索结果（用于服务端资源浏览）。
/// </summary>
public class ServerResourceHit
{
	public string ProjectId;

	public string Slug;

	public string Title;

	public string Description;

	public string ProjectType;

	public string ServerSide;

	public long Downloads;

	public string IconUrl;

	public List<string> Categories = new List<string>();

	public List<string> GameVersions = new List<string>();

	public override string ToString()
	{
		return Title;
	}
}

/// <summary>
/// 服务端资源（Mod / 插件）下载：只走 Modrinth，安装进实例自己的 mods 或 plugins 文件夹，
/// 与客户端实例完全隔离。
/// </summary>
public static class ModServerResource
{
	public const string Api = "https://api.modrinth.com/v2";

	private const string UserAgent = "PCL/2.14 (ServerManager)";

	/// <summary>该服务端类型是否支持插件（plugins）。</summary>
	public static bool SupportsPlugins(ServerInstance Instance)
	{
		return Instance != null && Instance.ServerType == "Paper";
	}

	/// <summary>该服务端类型是否支持 Mod（mods）。</summary>
	public static bool SupportsMods(ServerInstance Instance)
	{
		if (Instance == null)
		{
			return false;
		}
		string serverType = Instance.ServerType;
		if (!(serverType == "Fabric") && !(serverType == "Forge"))
		{
			return serverType == "NeoForge";
		}
		return true;
	}

	public static bool IsSupported(ServerInstance Instance)
	{
		if (!SupportsPlugins(Instance))
		{
			return SupportsMods(Instance);
		}
		return true;
	}

	/// <summary>Modrinth 的 project_type。</summary>
	public static string ProjectTypeOf(ServerInstance Instance)
	{
		if (SupportsPlugins(Instance))
		{
			return "plugin";
		}
		return "mod";
	}

	/// <summary>资源安装目录（末尾带反斜杠）。</summary>
	public static string TargetFolderOf(ServerInstance Instance)
	{
		if (SupportsPlugins(Instance))
		{
			return Instance.Dir + "plugins\\";
		}
		return Instance.Dir + "mods\\";
	}

	/// <summary>搜索时用于过滤的加载器标签（Modrinth 的 categories 字段）。</summary>
	public static List<string> LoadersOf(ServerInstance Instance)
	{
		List<string> list = new List<string>();
		switch (Instance.ServerType)
		{
			case "Paper":
				list.Add("paper");
				list.Add("spigot");
				list.Add("bukkit");
				list.Add("purpur");
				break;
			case "Fabric":
				list.Add("fabric");
				break;
			case "Forge":
				list.Add("forge");
				break;
			case "NeoForge":
				list.Add("neoforge");
				break;
		}
		return list;
	}

	public static string GetTypeName(ServerInstance Instance)
	{
		if (SupportsPlugins(Instance))
		{
			return "插件";
		}
		if (SupportsMods(Instance))
		{
			Instance.ToString();
			return "Mod";
		}
		Instance.ToString();
		return "不支持";
	}

	/// <summary>
	/// 在 Modrinth 上搜索适用于该服务端的资源。Query 可为空（返回热门结果）。
	/// </summary>
	public static List<ServerResourceHit> Search(ServerInstance Instance, string Query, int Limit = 20)
	{
		JArray jArray = new JArray();
		jArray.Add(new JArray("project_type:" + ProjectTypeOf(Instance)));
		JArray jArray2 = new JArray();
		foreach (string item in LoadersOf(Instance))
		{
			jArray2.Add("categories:" + item);
		}
		if (jArray2.Count > 0)
		{
			jArray.Add(jArray2);
		}
		if (!string.IsNullOrEmpty(Instance.McVersion))
		{
			jArray.Add(new JArray("versions:" + Instance.McVersion));
		}
		string text = Api + "/search?query=" + Uri.EscapeDataString(Query ?? "") + "&facets=" + Uri.EscapeDataString(jArray.ToString(Newtonsoft.Json.Formatting.None)) + "&limit=" + Limit + "&index=relevance";
		JObject jObject = JObject.Parse(HttpGetText(text));
		List<ServerResourceHit> list = new List<ServerResourceHit>();
		JToken jToken = jObject["hits"];
		if (jToken is JArray jArray3)
		{
			foreach (JToken item2 in jArray3)
			{
				ServerResourceHit serverResourceHit = new ServerResourceHit
				{
					ProjectId = ((object)item2["project_id"])?.ToString(),
					Slug = ((object)item2["slug"])?.ToString(),
					Title = ((object)item2["title"])?.ToString(),
					Description = ((object)item2["description"])?.ToString(),
					ProjectType = ((object)item2["project_type"])?.ToString(),
					ServerSide = ((object)item2["server_side"])?.ToString(),
					Downloads = (((object)item2["downloads"]) is JToken jToken2) ? ((long)jToken2) : 0L,
					IconUrl = ((object)item2["icon_url"])?.ToString()
				};
				if (item2["categories"] is JArray jArray4)
				{
					foreach (JToken item3 in jArray4)
					{
						serverResourceHit.Categories.Add(item3.ToString());
					}
				}
				if (item2["versions"] is JArray jArray5)
				{
					foreach (JToken item4 in jArray5)
					{
						serverResourceHit.GameVersions.Add(item4.ToString());
					}
				}
				list.Add(serverResourceHit);
			}
		}
		return list;
	}

	/// <summary>
	/// 取该项目适用于当前服务端的最新版本对象（JObject），找不到返回 null。
	/// </summary>
	public static JObject FindBestVersion(ServerInstance Instance, string ProjectId)
	{
		string text = Api + "/project/" + Uri.EscapeDataString(ProjectId) + "/version";
		JArray jArray = JArray.Parse(HttpGetText(text));
		List<string> loaders = LoadersOf(Instance);
		JObject jObject = null;
		foreach (JToken item in jArray)
		{
			if (item["game_versions"] is JArray jArray2 && !jArray2.ToString().Contains("\"" + Instance.McVersion + "\""))
			{
				continue;
			}
			if (item["loaders"] is JArray jArray3)
			{
				bool flag = false;
				foreach (string loader in loaders)
				{
					if (jArray3.ToString().Contains("\"" + loader + "\""))
					{
						flag = true;
						break;
					}
				}
				if (!flag)
				{
					continue;
				}
			}
			jObject = (JObject)item;
			break;
		}
		return jObject;
	}

	/// <summary>
	/// 安装一个资源到实例目录，并递归安装它的必需前置。Log 用于回显到控制台。
	/// </summary>
	public static void Install(ServerInstance Instance, ServerResourceHit Hit, Action<string> Log)
	{
		HashSet<string> hashSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		InstallProject(Instance, Hit.ProjectId, Hit.Title, Log, hashSet, 0);
	}

	private static void InstallProject(ServerInstance Instance, string ProjectId, string HintName, Action<string> Log, HashSet<string> Visited, int Depth)
	{
		if (ProjectId == null || Depth > 4 || !Visited.Add(ProjectId))
		{
			return;
		}
		JObject jObject = FindBestVersion(Instance, ProjectId);
		if (jObject == null)
		{
			Log("[PCL] 跳过 " + (HintName ?? ProjectId) + "：没有适用于 " + Instance.McVersion + " / " + Instance.ServerType + " 的版本。");
			return;
		}
		string text = ((object)jObject["version_number"])?.ToString();
		string text2 = ((object)jObject["name"])?.ToString();
		if (jObject["dependencies"] is JArray jArray)
		{
			foreach (JToken item in jArray)
			{
				string text3 = ((object)item["dependency_type"])?.ToString();
				if (!(text3 == "required"))
				{
					continue;
				}
				string text4 = ((object)item["project_id"])?.ToString();
				string text5 = ((object)item["version_id"])?.ToString();
				string hint = ((object)item["file_name"])?.ToString();
				if (!string.IsNullOrEmpty(text4))
				{
					InstallProject(Instance, text4, hint, Log, Visited, Depth + 1);
				}
				else if (!string.IsNullOrEmpty(text5))
				{
					InstallExactVersion(Instance, text5, hint, Log, Visited, Depth + 1);
				}
			}
		}
		JToken jToken = null;
		if (jObject["files"] is JArray jArray2)
		{
			foreach (JToken item2 in jArray2)
			{
				if (((object)item2["primary"]) is JToken jToken2 && jToken2.Type == JTokenType.Boolean && (bool)jToken2)
				{
					jToken = item2;
					break;
				}
			}
			if (jToken == null && jArray2.Count > 0)
			{
				jToken = jArray2[0];
			}
		}
		if (jToken == null)
		{
			Log("[PCL] 跳过 " + (HintName ?? ProjectId) + "：该版本没有可下载文件。");
			return;
		}
		string text6 = ((object)jToken["url"])?.ToString();
		string text7 = ((object)jToken["filename"])?.ToString();
		long fileSize = (((object)jToken["size"]) is JToken jToken3) ? ((long)jToken3) : 0L;
		if (string.IsNullOrEmpty(text6) || string.IsNullOrEmpty(text7))
		{
			Log("[PCL] 跳过 " + (HintName ?? ProjectId) + "：下载地址缺失。");
			return;
		}
		string targetFolder = TargetFolderOf(Instance);
		Directory.CreateDirectory(targetFolder);
		string text8 = targetFolder + Path.GetFileName(text7);
		Log("[PCL] 正在下载 " + text7 + "（" + (fileSize / 1024) + " KB，版本 " + (text ?? text2) + "）…");
		ModServer.DownloadFile(text6, text8);
		Log("[PCL] 已安装 " + Path.GetFileName(text8) + "  ->  " + targetFolder);
	}

	private static void InstallExactVersion(ServerInstance Instance, string VersionId, string HintName, Action<string> Log, HashSet<string> Visited, int Depth)
	{
		if (Depth > 4)
		{
			return;
		}
		JObject jObject = JObject.Parse(HttpGetText(Api + "/version/" + Uri.EscapeDataString(VersionId)));
		string text = ((object)jObject["project_id"])?.ToString();
		if (text != null && !Visited.Add(text))
		{
			return;
		}
		JToken jToken = null;
		if (jObject["files"] is JArray jArray && jArray.Count > 0)
		{
			jToken = jArray[0];
		}
		if (jToken == null)
		{
			return;
		}
		string text2 = ((object)jToken["url"])?.ToString();
		string text3 = ((object)jToken["filename"])?.ToString();
		if (!string.IsNullOrEmpty(text2) && !string.IsNullOrEmpty(text3))
		{
			string targetFolder = TargetFolderOf(Instance);
			Directory.CreateDirectory(targetFolder);
			string text4 = targetFolder + Path.GetFileName(text3);
			Log("[PCL] 正在下载前置 " + text3 + " …");
			ModServer.DownloadFile(text2, text4);
			Log("[PCL] 已安装前置 " + Path.GetFileName(text4) + "  ->  " + targetFolder);
		}
	}

	public static string HttpGetText(string Url)
	{
		HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(Url);
		httpWebRequest.UserAgent = UserAgent;
		httpWebRequest.Accept = "application/json";
		httpWebRequest.Timeout = 20000;
		httpWebRequest.ReadWriteTimeout = 30000;
		httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		using WebResponse webResponse = httpWebRequest.GetResponse();
		using Stream stream = webResponse.GetResponseStream();
		using StreamReader streamReader = new StreamReader(stream, Encoding.UTF8);
		return streamReader.ReadToEnd();
	}
}
