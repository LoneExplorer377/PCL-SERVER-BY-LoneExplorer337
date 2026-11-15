using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using MeloongCore;
using Newtonsoft.Json.Linq;
using PCLCS;

namespace PCL;

public enum ServerState
{
	Stopped = 0,
	Starting = 1,
	Running = 2,
	Stopping = 3
}

/// <summary>
/// PCL 服务器模式的一个服务端实例。数据保存在 PCL\Server\&lt;名称&gt;\server.json。
/// </summary>
public class ServerInstance
{
	public string Name = "";

	public string McVersion = "";

	public string ServerType = "Vanilla";

	public string JavaPath = "";

	public int MemoryMB = 2048;

	public int Port = 25565;

	public bool EulaAccepted;

	public string LastStart = "";

	public string LoaderVersion = "";

	internal Process Proc;

	internal StreamWriter Stdin;

	internal StreamWriter LogWriter;

	internal bool IsStarting;

	public ServerState State = ServerState.Stopped;

	public string Dir => ModServer.RootDir + Name + "\\";

	public string JarPath => Dir + "server.jar";

	public bool HasJar
	{
		get
		{
			if (ServerType == "Fabric")
			{
				return File.Exists(JarPath) && File.Exists(Dir + "minecraft-server.jar");
			}
			if (File.Exists(JarPath))
			{
				return true;
			}
			if (!string.IsNullOrEmpty(ModServer.FindArgsFile(this)))
			{
				return true;
			}
			return !string.IsNullOrEmpty(ModServer.FindLegacyForgeJar(this));
		}
	}

	public bool IsRunning
	{
		get
		{
			try
			{
				return Proc != null && !Proc.HasExited;
			}
			catch (Exception)
			{
				return false;
			}
		}
	}

	public override string ToString()
	{
		return Name + "  -  " + ModServer.GetTypeName(ServerType) + " " + McVersion;
	}
}

/// <summary>
/// 服务器模式的全局管理：实例读写、服务端核心下载、进程启动与指令转发。
/// 与客户端模式完全独立，不读写 .minecraft。
/// </summary>
public static class ModServer
{
	private static readonly object Lock = new object();

	private static List<ServerInstance> _Instances;

	public static event Action<ServerInstance> StateChanged;

	public static event Action<ServerInstance, string> LineReceived;

	public static string RootDir => Paths.Base + "PCL\\Server\\";

	public static List<ServerInstance> Instances
	{
		get
		{
			EnsureLoaded();
			return _Instances;
		}
	}
	/// <summary>当前在界面上选中的服务端实例（插件安装目标）。为 null 表示没有选中任何服务端。</summary>
	public static ServerInstance SelectedInstance;


	public static bool HasRunningServer
	{
		get
		{
			EnsureLoaded();
			foreach (ServerInstance item in _Instances)
			{
				if (item.IsRunning)
				{
					return true;
				}
			}
			return false;
		}
	}

	public static int RunningCount
	{
		get
		{
			EnsureLoaded();
			int num = 0;
			foreach (ServerInstance item in _Instances)
			{
				if (item.IsRunning)
				{
					num++;
				}
			}
			return num;
		}
	}

	public static string GetTypeName(string Type)
	{
		return Type switch
		{
			"Paper" => "Paper", 
			"Fabric" => "Fabric", 
			"Forge" => "Forge", 
			_ => "原版", 
		};
	}

	public static void EnsureLoaded()
	{
		if (_Instances != null)
		{
			return;
		}
		lock (Lock)
		{
			if (_Instances != null)
			{
				return;
			}
			List<ServerInstance> list = new List<ServerInstance>();
			try
			{
				string root = RootDir;
				if (Directory.Exists(root))
				{
					foreach (string item in Directory.GetDirectories(root))
					{
						string text = item + "\\server.json";
						if (!File.Exists(text))
						{
							continue;
						}
						try
						{
							ServerInstance serverInstance = FromJson(JObject.Parse(File.ReadAllText(text, Encoding.UTF8)));
							if (!string.IsNullOrEmpty(serverInstance.Name))
							{
								list.Add(serverInstance);
							}
						}
						catch (Exception ex)
						{
							Logger.Warn(ex, "读取服务器实例配置失败：" + text, (LogBehavior)1, "ModServer.cs");
						}
					}
				}
			}
			catch (Exception ex2)
			{
				Logger.Warn(ex2, "读取服务器实例列表失败", (LogBehavior)1, "ModServer.cs");
			}
			list.Sort((ServerInstance a, ServerInstance b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
			_Instances = list;
		}
	}

	public static bool Exists(string Name)
	{
		EnsureLoaded();
		foreach (ServerInstance item in _Instances)
		{
			if (string.Equals(item.Name, Name, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	public static ServerInstance Create(string Name, string McVersion, string ServerType, string JavaPath, int MemoryMB, int Port)
	{
		if (string.IsNullOrWhiteSpace(Name))
		{
			throw new Exception("服务器名称不能为空！");
		}
		Name = Name.Trim();
		if (Name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Name.Contains("\\") || Name.Contains("/"))
		{
			throw new Exception("服务器名称不能包含 \\ / : * ? \" < > | 等字符！");
		}
		if (Exists(Name))
		{
			throw new Exception("已存在同名的服务器实例：" + Name);
		}
		if (MemoryMB < 256)
		{
			throw new Exception("内存分配不能低于 256 MB！");
		}
		if (Port < 1 || Port > 65535)
		{
			throw new Exception("端口必须在 1 - 65535 之间！");
		}
		ServerInstance serverInstance = new ServerInstance
		{
			Name = Name,
			McVersion = McVersion,
			ServerType = ServerType,
			JavaPath = JavaPath,
			MemoryMB = MemoryMB,
			Port = Port
		};
		Directory.CreateDirectory(serverInstance.Dir);
		Directory.CreateDirectory(serverInstance.Dir + "mods\\");
		Directory.CreateDirectory(serverInstance.Dir + "plugins\\");
		Directory.CreateDirectory(serverInstance.Dir + "logs\\");
		WriteProperties(serverInstance);
		Save(serverInstance);
		lock (Lock)
		{
			_Instances.Add(serverInstance);
		}
		Logger.Info("已创建服务器实例：" + serverInstance.Name + "（" + serverInstance.ServerType + " " + serverInstance.McVersion + "）", (LogBehavior)0, "ModServer.cs");
		return serverInstance;
	}

	public static void Delete(ServerInstance Instance, bool DeleteFiles = true)
	{
		if (Instance == null)
		{
			return;
		}
		if (Instance.IsRunning)
		{
			throw new Exception("服务器正在运行，无法删除！");
		}
		EnsureLoaded();
		lock (Lock)
		{
			_Instances.Remove(Instance);
		}
		if (DeleteFiles)
		{
			try
			{
				Directory.Delete(Instance.Dir, true);
			}
			catch (Exception ex)
			{
				Logger.Warn(ex, "删除服务器文件夹失败：" + Instance.Dir, (LogBehavior)1, "ModServer.cs");
			}
		}
	}

	public static void Save(ServerInstance Instance)
	{
		try
		{
			Directory.CreateDirectory(Instance.Dir);
			File.WriteAllText(Instance.Dir + "server.json", ToJson(Instance).ToString(), new UTF8Encoding(false));
		}
		catch (Exception ex)
		{
			Logger.Warn(ex, "保存服务器实例配置失败：" + Instance.Name, (LogBehavior)1, "ModServer.cs");
		}
	}

	/// <summary>
	/// 读取 server.properties 的全部键值（保持文件里的顺序）。
	/// </summary>
	public static List<KeyValuePair<string, string>> ReadProperties(ServerInstance Instance)
	{
		List<KeyValuePair<string, string>> list = new List<KeyValuePair<string, string>>();
		string text = Instance.Dir + "server.properties";
		if (!File.Exists(text))
		{
			return list;
		}
		foreach (string item in File.ReadAllLines(text, Encoding.UTF8))
		{
			string text2 = item;
			if (text2.Length == 0 || text2.StartsWith("#"))
			{
				continue;
			}
			int num = text2.IndexOf('=');
			if (num > 0)
			{
				list.Add(new KeyValuePair<string, string>(text2.Substring(0, num).Trim(), text2.Substring(num + 1)));
			}
		}
		return list;
	}

	/// <summary>
	/// 写回 server.properties：保留未修改的键与注释顺序，只更新传入的键。
	/// </summary>
	public static void SaveProperties(ServerInstance Instance, IDictionary<string, string> Values)
	{
		string text = Instance.Dir + "server.properties";
		List<string> list = new List<string>();
		if (File.Exists(text))
		{
			list.AddRange(File.ReadAllLines(text, Encoding.UTF8));
		}
		if (list.Count == 0)
		{
			list.Add("#Minecraft server properties");
		}
		List<string> list2 = new List<string>();
		HashSet<string> hashSet = new HashSet<string>(Values.Keys);
		foreach (string item in list)
		{
			string text2 = item;
			if (text2.Length > 0 && !text2.StartsWith("#"))
			{
				int num = text2.IndexOf('=');
				if (num > 0)
				{
					string text3 = text2.Substring(0, num).Trim();
					if (Values.ContainsKey(text3))
					{
						list2.Add(text3 + "=" + Values[text3]);
						hashSet.Remove(text3);
						continue;
					}
				}
			}
			list2.Add(text2);
		}
		foreach (string item2 in hashSet)
		{
			list2.Add(item2 + "=" + Values[item2]);
		}
		File.WriteAllText(text, string.Join("\r\n", list2) + "\r\n", new UTF8Encoding(false));
	}

	/// <summary>
	/// 读取 whitelist.json / ops.json 里的玩家名（只用于界面展示）。
	/// </summary>
	public static List<string> ReadPlayerList(ServerInstance Instance, string FileName)
	{
		List<string> list = new List<string>();
		string text = Instance.Dir + FileName;
		if (!File.Exists(text))
		{
			return list;
		}
		try
		{
			JArray jArray = JArray.Parse(File.ReadAllText(text, Encoding.UTF8));
			foreach (JToken item in jArray)
			{
				string text2 = item["name"]?.ToString();
				if (!string.IsNullOrEmpty(text2))
				{
					list.Add(text2);
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warn(ex, "读取 " + FileName + " 失败", (LogBehavior)1, "ModServer.cs");
		}
		return list;
	}

	/// <summary>
	/// 返回最新的崩溃报告路径（没有则返回最新的 latest.log，都没有返回空）。
	/// </summary>
	public static string FindLatestCrash(ServerInstance Instance)
	{
		string text = Instance.Dir + "crash-reports";
		if (Directory.Exists(text))
		{
			FileInfo[] files = new DirectoryInfo(text).GetFiles("*.txt");
			if (files.Length > 0)
			{
				FileInfo fileInfo = files.OrderByDescending((FileInfo f) => f.LastWriteTimeUtc).First();
				return fileInfo.FullName;
			}
		}
		string text2 = Instance.Dir + "logs\\latest.log";
		return File.Exists(text2) ? text2 : "";
	}

	public static void WriteProperties(ServerInstance Instance)
	{
		string text = Instance.Dir + "server.properties";
		if (File.Exists(text))
		{
			return;
		}
		StringBuilder stringBuilder = new StringBuilder();
		stringBuilder.AppendLine("#Minecraft server properties");
		stringBuilder.AppendLine("server-port=" + Instance.Port);
		stringBuilder.AppendLine("motd=PCL 服务器 - " + Instance.Name);
		stringBuilder.AppendLine("max-players=20");
		stringBuilder.AppendLine("online-mode=true");
		File.WriteAllText(text, stringBuilder.ToString(), new UTF8Encoding(false));
	}

	public static void WriteEula(ServerInstance Instance)
	{
		File.WriteAllText(Instance.Dir + "eula.txt", "#By changing the setting below to TRUE you are indicating your agreement to our EULA (https://aka.ms/MinecraftEULA).\r\neula=true\r\n", new UTF8Encoding(false));
	}

	/// <summary>
	/// 该 Minecraft 版本要求的最低 Java 大版本。
	/// </summary>
	public static int RequiredJavaMajor(string McVersion)
	{
		try
		{
			string[] array = (McVersion ?? "").Split('.');
			if (array.Length < 2)
			{
				return 21;
			}
			int num = int.Parse(array[0]);
			int num2 = int.Parse(array[1]);
			int num3 = array.Length > 2 ? int.Parse(array[2].Split('-')[0]) : 0;
			if (num != 1)
			{
				return 21;
			}
			switch (num2)
			{
			case 0:
			case 1:
			case 2:
			case 3:
			case 4:
			case 5:
			case 6:
			case 7:
			case 8:
			case 9:
			case 10:
			case 11:
			case 12:
			case 13:
			case 14:
			case 15:
			case 16:
				return 8;
			case 17:
				return 16;
			case 18:
			case 19:
				return 17;
			default:
				return (num2 == 20 && num3 <= 4) ? 17 : 21;
			}
		}
		catch (Exception)
		{
			return 21;
		}
	}

	/// <summary>
	/// 选择最合适的 Java：优先满足该版本的最低要求，且大版本尽量低（新版 Java 反而容易出兼容问题）。
	/// </summary>
	public static string FindBestJava(string McVersion = "")
	{
		int need = string.IsNullOrEmpty(McVersion) ? 0 : RequiredJavaMajor(McVersion);
		string text = "";
		int num = int.MaxValue;
		string text2 = "";
		int num2 = -1;
		try
		{
			foreach (Java item in Configs.JavaList.Get())
			{
				if (item.Version == null || item.JavaExePath == null)
				{
					continue;
				}
				int major = item.Version.Major;
				if (major > num2)
				{
					num2 = major;
					text2 = item.JavaExePath;
				}
				if (need > 0 && major >= need && major < num)
				{
					num = major;
					text = item.JavaExePath;
				}
			}
		}
		catch (Exception ex)
		{
			Logger.Warn(ex, "读取 Java 列表失败", (LogBehavior)1, "ModServer.cs");
		}
		if (!string.IsNullOrEmpty(text))
		{
			return text;
		}
		if (!string.IsNullOrEmpty(text2))
		{
			return text2;
		}
		string text3 = Environment.GetEnvironmentVariable("JAVA_HOME");
		if (!string.IsNullOrEmpty(text3) && File.Exists(text3 + "\\bin\\java.exe"))
		{
			return text3 + "\\bin\\java.exe";
		}
		return "";
	}

	/// <summary>
	/// 下载服务端核心（阻塞，请在后台线程调用）。Progress 参数为百分比与文本描述。
	/// </summary>
	public static void DownloadServerJar(ServerInstance Instance, Action<int, string> Progress = null)
	{
		Directory.CreateDirectory(Instance.Dir);
		if (Instance.ServerType == "Forge")
		{
			InstallForgeServer(Instance, Progress);
			return;
		}
		if (Instance.ServerType == "NeoForge")
		{
			InstallNeoForgeServer(Instance, Progress);
			return;
		}
		if (Instance.ServerType == "Fabric")
		{
			Progress?.Invoke(1, "正在解析 Fabric 版本…");
			string text2 = ResolveFabricUrl(Instance);
			Progress?.Invoke(3, "正在下载 Fabric 服务端启动器…");
			DownloadWithProgress(text2, Instance.JarPath, Progress, "下载 Fabric 启动器", 10240L, 3, 12);
			Progress?.Invoke(12, "正在下载原版服务端核心（约 50 MB）…");
			DownloadWithProgress("https://bmclapi2.bangbang93.com/version/" + Instance.McVersion + "/server", Instance.Dir + "minecraft-server.jar", Progress, "下载原版服务端核心", 1048576L, 12, 99);
			Save(Instance);
			Progress?.Invoke(100, "Fabric 服务端准备完成");
			Logger.Info("Fabric 服务端准备完成：" + Instance.JarPath, (LogBehavior)0, "ModServer.cs");
			return;
		}
		string url = ((Instance.ServerType == "Paper") ? ResolvePaperUrl(Instance.McVersion) : ("https://bmclapi2.bangbang93.com/version/" + Instance.McVersion + "/server"));
		Logger.Info("开始下载服务端核心：" + url, (LogBehavior)0, "ModServer.cs");
		DownloadWithProgress(url, Instance.JarPath, Progress, "下载服务端核心", 1048576L, 0, 100);
		Progress?.Invoke(100, "服务端核心下载完成");
		Logger.Info("服务端核心下载完成：" + Instance.JarPath, (LogBehavior)0, "ModServer.cs");
	}

	/// <summary>
	/// 带进度的文件下载（阻塞，请在后台线程调用）。From/To 用于把 0-100% 映射到整体进度区间。
	/// </summary>
	private static void DownloadWithProgress(string Url, string TargetPath, Action<int, string> Progress, string Label, long MinBytes, int From = 0, int To = 100, bool Silent = false)
	{
		string text = TargetPath + ".download";
		try
		{
			if (File.Exists(text))
			{
				File.Delete(text);
			}
			HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(Url);
			httpWebRequest.UserAgent = "PCL/2.14 (ServerManager)";
			httpWebRequest.AllowAutoRedirect = true;
			httpWebRequest.Timeout = 30000;
			httpWebRequest.ReadWriteTimeout = 120000;
			httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
			using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
			{
				long num = httpWebResponse.ContentLength;
				long num2 = 0L;
				int num3 = -1;
				using (Stream stream = httpWebResponse.GetResponseStream())
				{
					using Stream stream2 = new FileStream(text, FileMode.Create, FileAccess.Write);
					byte[] array = new byte[131072];
					int num4;
					while ((num4 = stream.Read(array, 0, array.Length)) > 0)
					{
						stream2.Write(array, 0, num4);
						num2 += num4;
						if (num > 0L)
						{
							int num5 = (int)(num2 * 100L / num);
							int num6 = From + (int)((To - From) * (long)num5 / 100L);
							if (num6 != num3)
							{
								num3 = num6;
								Progress?.Invoke(num6, $"{Label}… {num5}%（{num2 / 1048576.0:F1} MB / {num / 1048576.0:F1} MB）");
							}
						}
						else if (num2 % 4194304 < array.Length)
						{
							Progress?.Invoke(From, $"{Label}… {num2 / 1048576.0:F1} MB");
						}
					}
				}
				if (num2 < MinBytes)
				{
					throw new Exception("下载到的文件过小（" + num2 + " 字节），可能不是有效的服务端核心");
				}
			}
			if (File.Exists(TargetPath))
			{
				File.Delete(TargetPath);
			}
			File.Move(text, TargetPath);
		}
		catch (Exception ex)
		{
			try
			{
				if (File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch (Exception)
			{
			}
			Logger.Error(ex, "下载失败：" + Url, (LogBehavior)4, "ModServer.cs");
			throw;
		}
	}

	/// <summary>
	/// 带进度的下载，失败会抛出异常；Silent 为 true 时不写错误日志（用于可跳过的整合包文件）。
	/// </summary>
	public static void DownloadFileWithProgress(string Url, string TargetPath, Action<int, string> Progress, bool Silent = false)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(TargetPath));
		string text = TargetPath + ".download";
		try
		{
			HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(Url);
			httpWebRequest.UserAgent = "PCL/2.14 (ServerManager)";
			httpWebRequest.AllowAutoRedirect = true;
			httpWebRequest.Timeout = 30000;
			httpWebRequest.ReadWriteTimeout = 120000;
			httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
			using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
			using (Stream stream = httpWebResponse.GetResponseStream())
			using (FileStream fileStream = new FileStream(text, FileMode.Create, FileAccess.Write))
			{
				long num = httpWebResponse.ContentLength;
				byte[] array = new byte[131072];
				long num2 = 0L;
				int num3 = 0;
				int num4;
				while ((num4 = stream.Read(array, 0, array.Length)) > 0)
				{
					fileStream.Write(array, 0, num4);
					num2 += num4;
					int num5 = num > 0L ? (int)(num2 * 100L / num) : 0;
					if (num5 != num3 || Progress == null)
					{
						num3 = num5;
						Progress?.Invoke(num5, "正在下载 " + Path.GetFileName(TargetPath) + "：" + num5 + "%（" + (num2 / 1024L) + " KB）");
					}
				}
			}
			if (File.Exists(TargetPath))
			{
				File.Delete(TargetPath);
			}
			File.Move(text, TargetPath);
		}
		catch (Exception ex)
		{
			try
			{
				if (File.Exists(text))
				{
					File.Delete(text);
				}
			}
			catch (Exception)
			{
			}
			if (!Silent)
			{
				Logger.Error(ex, "下载失败：" + Url, (LogBehavior)4, "ModServer.cs");
			}
			throw;
		}
	}

	/// <summary>
	/// 把整合包/用户输入的名字变成可用的文件夹名。
	/// </summary>
	public static string SanitizeName(string Name)
	{
		string text = (Name ?? "").Trim();
		foreach (char c in Path.GetInvalidFileNameChars())
		{
			text = text.Replace(c, '_');
		}
		text = text.Replace(' ', '_');
		if (string.IsNullOrWhiteSpace(text))
		{
			text = "server";
		}
		if (text.Length > 40)
		{
			text = text.Substring(0, 40);
		}
		return text;
	}

	/// <summary>
	/// 生成一个不与现有实例冲突的名字。
	/// </summary>
	public static string NewUniqueName(string Base)
	{
		string text = Base;
		int num = 2;
		while (Exists(text))
		{
			text = Base + "-" + num;
			num++;
		}
		return text;
	}

	/// <summary>
	/// 下载一个文件到本地（用于服务端资源）。下载失败会抛出异常。
	/// </summary>
	public static void DownloadFile(string Url, string TargetPath)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(TargetPath));
		string text = TargetPath + ".download";
		if (File.Exists(text))
		{
			File.Delete(text);
		}
		HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(Url);
		httpWebRequest.UserAgent = "PCL/2.14 (ServerManager)";
		httpWebRequest.AllowAutoRedirect = true;
		httpWebRequest.Timeout = 30000;
		httpWebRequest.ReadWriteTimeout = 120000;
		httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		using (HttpWebResponse httpWebResponse = (HttpWebResponse)httpWebRequest.GetResponse())
		{
			using Stream stream = httpWebResponse.GetResponseStream();
			using (FileStream fileStream = new FileStream(text, FileMode.Create, FileAccess.Write))
			{
				stream.CopyTo(fileStream);
			}
		}
		if (File.Exists(TargetPath))
		{
			File.Delete(TargetPath);
		}
		File.Move(text, TargetPath);
	}

	private static string ResolvePaperUrl(string Version)
	{
		string text = HttpGetText("https://fill.papermc.io/v3/projects/paper/versions/" + Version + "/builds");
		JArray jArray = JArray.Parse(text);
		string text2 = "";
		int num = -1;
		foreach (JToken item in jArray)
		{
			if ((string)item["channel"] != "STABLE")
			{
				continue;
			}
			JToken jToken = item["downloads"]?["server:default"]?["url"];
			if (jToken == null)
			{
				continue;
			}
			int num2 = (int)item["id"];
			if (num2 > num)
			{
				num = num2;
				text2 = (string)jToken;
			}
		}
		if (string.IsNullOrEmpty(text2))
		{
			throw new Exception("未找到 Paper " + Version + " 的稳定版构建");
		}
		Logger.Info("Paper 构建解析成功：" + Version + " build " + num, (LogBehavior)0, "ModServer.cs");
		return text2;
	}

	/// <summary>
	/// 查找 Forge / NeoForge 的启动参数文件（win_args.txt），找不到返回空字符串。
	/// </summary>
	public static string FindArgsFile(ServerInstance Instance)
	{
		string[] array = new string[2]
		{
			Instance.Dir + "libraries\\net\\minecraftforge\\forge\\",
			Instance.Dir + "libraries\\net\\neoforged\\neoforge\\"
		};
		foreach (string text in array)
		{
			try
			{
				if (!Directory.Exists(text))
				{
					continue;
				}
				string[] directories = Directory.GetDirectories(text);
				Array.Sort(directories, StringComparer.OrdinalIgnoreCase);
				for (int num = directories.Length - 1; num >= 0; num--)
				{
					string text2 = directories[num] + "\\win_args.txt";
					if (File.Exists(text2))
					{
						return text2;
					}
				}
			}
			catch (Exception)
			{
			}
		}
		return "";
	}

	/// <summary>
	/// 查找 1.16.5 及更早版本 Forge 安装在实例根目录的核心 jar（返回文件名，找不到返回空字符串）。
	/// </summary>
	public static string FindLegacyForgeJar(ServerInstance Instance)
	{
		try
		{
			foreach (string item in Directory.GetFiles(Instance.Dir, "forge-*.jar"))
			{
				if (!item.EndsWith(".download", StringComparison.OrdinalIgnoreCase) && !item.EndsWith("-installer.jar", StringComparison.OrdinalIgnoreCase))
				{
					return Path.GetFileName(item);
				}
			}
		}
		catch (Exception)
		{
		}
		return "";
	}

	private static void WriteUserJvmArgs(ServerInstance Instance)
	{
		int num = Math.Max(512, Instance.MemoryMB);
		string text = "-Xms" + num / 2 + "M\r\n-Xmx" + num + "M\r\n-Dfile.encoding=UTF-8\r\n-Dstdout.encoding=UTF-8\r\n-Dstderr.encoding=UTF-8\r\n-Dsun.stdout.encoding=UTF-8\r\n-Dsun.stderr.encoding=UTF-8\r\n";
		try
		{
			File.WriteAllText(Instance.Dir + "user_jvm_args.txt", text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
		}
		catch (Exception ex)
		{
			Logger.Warn(ex, "写入 user_jvm_args.txt 失败", (LogBehavior)1, "ModServer.cs");
		}
	}

	private static string ResolveFabricUrl(ServerInstance Instance)
	{
		string text = Instance.LoaderVersion;
		if (string.IsNullOrEmpty(text))
		{
			JArray jArray = JArray.Parse(HttpGetText("https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/loader/" + Instance.McVersion));
			foreach (JToken item in jArray)
			{
				if (item["loader"]?["stable"]?.Value<bool>() ?? false)
				{
					text = (string)item["loader"]["version"];
					break;
				}
			}
			if (string.IsNullOrEmpty(text) && jArray.Count > 0)
			{
				text = (string)jArray[0]["loader"]["version"];
			}
		}
		if (string.IsNullOrEmpty(text))
		{
			throw new Exception("未找到 Fabric " + Instance.McVersion + " 的加载器版本");
		}
		JArray jArray2 = JArray.Parse(HttpGetText("https://bmclapi2.bangbang93.com/fabric-meta/v2/versions/installer"));
		string text2 = "";
		foreach (JToken item2 in jArray2)
		{
			if (item2["stable"]?.Value<bool>() ?? false)
			{
				text2 = (string)item2["version"];
				break;
			}
		}
		if (string.IsNullOrEmpty(text2) && jArray2.Count > 0)
		{
			text2 = (string)jArray2[0]["version"];
		}
		Instance.LoaderVersion = text;
		Logger.Info("Fabric 版本解析成功：loader " + text + "，installer " + text2, (LogBehavior)0, "ModServer.cs");
		return "https://meta.fabricmc.net/v2/versions/loader/" + Instance.McVersion + "/" + text + "/" + text2 + "/server/jar";
	}

	private static void InstallForgeServer(ServerInstance Instance, Action<int, string> Progress)
	{
		string mcVersion = Instance.McVersion;
		Progress?.Invoke(1, "正在获取 Forge 版本信息…");
		JArray jArray = JArray.Parse(HttpGetText("https://bmclapi2.bangbang93.com/forge/promos"));
		string text = "";
		string text2 = "";
		foreach (JToken item in jArray)
		{
			string text3 = (string)item["name"];
			if (text3 == mcVersion + "-recommended")
			{
				text2 = (string)item["build"]?["version"];
			}
			else if (text3 == mcVersion + "-latest")
			{
				text = (string)item["build"]?["version"];
			}
		}
		if (string.IsNullOrEmpty(text2))
		{
			text2 = text;
		}
		if (string.IsNullOrEmpty(text2))
		{
			throw new Exception("未找到 Forge " + mcVersion + " 的可用版本（该版本可能还没有 Forge 支持）");
		}
		Instance.LoaderVersion = text2;
		Progress?.Invoke(2, "正在下载 Forge 安装器 " + text2 + "…");
		string text4 = Instance.Dir + "forge-installer.jar";
		DownloadFile("https://bmclapi2.bangbang93.com/maven/net/minecraftforge/forge/" + mcVersion + "-" + text2 + "/forge-" + mcVersion + "-" + text2 + "-installer.jar", text4);
		try
		{
			RunInstaller(Instance, text4, Progress);
		}
		finally
		{
			try
			{
				File.Delete(text4);
			}
			catch (Exception)
			{
			}
		}
		if (!Instance.HasJar)
		{
			throw new Exception("Forge 安装完成后没有找到可用的服务端核心");
		}
		Save(Instance);
		Progress?.Invoke(100, "Forge 服务端安装完成（" + text2 + "）");
		Logger.Info("Forge 服务端安装完成：" + Instance.Dir + "（" + text2 + "）", (LogBehavior)0, "ModServer.cs");
	}

	private static void InstallNeoForgeServer(ServerInstance Instance, Action<int, string> Progress)
	{
		string mcVersion = Instance.McVersion;
		string[] array = mcVersion.Split('.');
		string text = ((array.Length >= 2) ? (array[0] + "." + array[1] + ".") : (mcVersion + "."));
		Progress?.Invoke(1, "正在获取 NeoForge 版本信息…");
		JObject jObject = JObject.Parse(HttpGetText("https://bmclapi2.bangbang93.com/neoforge/meta/api/maven/details/releases/net/neoforged/neoforge"));
		string text2 = "";
		JToken jToken = jObject["files"];
		if (jToken != null)
		{
			foreach (JToken item in jToken)
			{
				string text3 = (string)item["name"];
				if (!string.IsNullOrEmpty(text3) && text3.StartsWith(text, StringComparison.Ordinal) && CompareVersion(text3, text2) > 0)
				{
					text2 = text3;
				}
			}
		}
		if (string.IsNullOrEmpty(text2))
		{
			throw new Exception("未找到 NeoForge " + mcVersion + " 的可用版本（NeoForge 只支持 1.20.2 及更高版本）");
		}
		Instance.LoaderVersion = text2;
		Progress?.Invoke(2, "正在下载 NeoForge 安装器 " + text2 + "…");
		string text4 = Instance.Dir + "neoforge-installer.jar";
		DownloadFile("https://bmclapi2.bangbang93.com/maven/net/neoforged/neoforge/" + text2 + "/neoforge-" + text2 + "-installer.jar", text4);
		try
		{
			RunInstaller(Instance, text4, Progress);
		}
		finally
		{
			try
			{
				File.Delete(text4);
			}
			catch (Exception)
			{
			}
		}
		if (!Instance.HasJar)
		{
			throw new Exception("NeoForge 安装完成后没有找到可用的服务端核心");
		}
		Save(Instance);
		Progress?.Invoke(100, "NeoForge 服务端安装完成（" + text2 + "）");
		Logger.Info("NeoForge 服务端安装完成：" + Instance.Dir + "（" + text2 + "）", (LogBehavior)0, "ModServer.cs");
	}

	private static int CompareVersion(string A, string B)
	{
		if (string.IsNullOrEmpty(B))
		{
			return 1;
		}
		string[] array = A.Split('.');
		string[] array2 = B.Split('.');
		int num = Math.Min(array.Length, array2.Length);
		for (int i = 0; i < num; i++)
		{
			int result;
			int result2;
			bool flag = int.TryParse(array[i], out result);
			bool flag2 = int.TryParse(array2[i], out result2);
			if (flag && flag2)
			{
				if (result != result2)
				{
					return result.CompareTo(result2);
				}
			}
			else
			{
				int num2 = string.Compare(array[i], array2[i], StringComparison.OrdinalIgnoreCase);
				if (num2 != 0)
				{
					return num2;
				}
			}
		}
		return array.Length.CompareTo(array2.Length);
	}

	private static void RunInstaller(ServerInstance Instance, string InstallerPath, Action<int, string> Progress)
	{
		if (string.IsNullOrEmpty(Instance.JavaPath) || !File.Exists(Instance.JavaPath))
		{
			Instance.JavaPath = FindBestJava(Instance.McVersion);
		}
		if (string.IsNullOrEmpty(Instance.JavaPath) || !File.Exists(Instance.JavaPath))
		{
			throw new Exception("未找到可用的 Java，无法运行服务端安装器");
		}
		Progress?.Invoke(5, "正在安装服务端核心，可能需要 1-3 分钟…");
		Logger.Info("运行服务端安装器：" + InstallerPath + "（Java：" + Instance.JavaPath + "）", (LogBehavior)0, "ModServer.cs");
		ProcessStartInfo processStartInfo = new ProcessStartInfo(Instance.JavaPath, "-jar \"" + InstallerPath + "\" --installServer")
		{
			WorkingDirectory = Instance.Dir,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		StringBuilder stringBuilder = new StringBuilder();
		using Process process = new Process
		{
			StartInfo = processStartInfo,
			EnableRaisingEvents = true
		};
		process.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				stringBuilder.AppendLine(e.Data);
			}
		};
		process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				stringBuilder.AppendLine(e.Data);
			}
		};
		process.Start();
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		DateTime dateTime = DateTime.Now;
		while (!process.HasExited)
		{
			if ((DateTime.Now - dateTime).TotalMinutes > 20.0)
			{
				try
				{
					process.Kill();
				}
				catch (Exception)
				{
				}
				throw new Exception("服务端安装器运行超时（超过 20 分钟）");
			}
			Thread.Sleep(500);
			Progress?.Invoke(5, "正在安装服务端核心，已用时 " + (int)(DateTime.Now - dateTime).TotalSeconds + " 秒…");
		}
		process.WaitForExit();
		string text = stringBuilder.ToString();
		string[] array = text.Replace("\r\n", "\n").Split('\n');
		string text2 = "";
		for (int num = array.Length - 1; num >= 0; num--)
		{
			if (!string.IsNullOrWhiteSpace(array[num]))
			{
				text2 = array[num].Trim();
				break;
			}
		}
		Logger.Info("服务端安装器结束，退出码 " + process.ExitCode + "，末行：" + text2, (LogBehavior)0, "ModServer.cs");
		if (process.ExitCode != 0)
		{
			if (text.Contains("Failed to download version manifest") || text.Contains("launchermeta.mojang.com") || text.Contains("piston-meta.mojang.com"))
			{
				throw new Exception("安装 " + Instance.ServerType + " 服务端需要访问 Mojang 官方下载源（launchermeta.mojang.com / piston-meta.mojang.com），当前网络无法连接。\n\n请先配置可用的网络代理后重试；也可以改用 Fabric 或 Paper 服务端（它们不依赖 Mojang 官方源）。");
			}
			if (text.Contains("Failed to download") || text.Contains("DownloadException") || text.Contains("ConnectException") || text.Contains("UnknownHostException"))
			{
				throw new Exception("服务端安装器下载依赖失败：" + text2 + "\n\n请检查网络后重试。");
			}
			throw new Exception("服务端安装器运行失败（退出码 " + process.ExitCode + "）：" + text2);
		}
		Progress?.Invoke(80, "安装器已结束，正在检查核心文件…");
	}

	public static string HttpGetText(string Url)
	{
		HttpWebRequest httpWebRequest = (HttpWebRequest)WebRequest.Create(Url);
		httpWebRequest.UserAgent = "PCL/2.14 (ServerManager)";
		httpWebRequest.Accept = "application/json";
		httpWebRequest.Timeout = 20000;
		httpWebRequest.ReadWriteTimeout = 30000;
		httpWebRequest.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
		using WebResponse webResponse = httpWebRequest.GetResponse();
		using Stream stream = webResponse.GetResponseStream();
		using StreamReader streamReader = new StreamReader(stream, Encoding.UTF8);
		return streamReader.ReadToEnd();
	}

	public static void Start(ServerInstance Instance)
	{
		if (Instance == null)
		{
			throw new Exception("没有选中任何服务器实例");
		}
		if (Instance.IsRunning)
		{
			throw new Exception("该服务器已经在运行中");
		}
		if (!Instance.HasJar)
		{
			throw new Exception("尚未下载服务端核心，请先完成下载");
		}
		foreach (ServerInstance item in Instances)
		{
			if (item != Instance && item.IsRunning && item.Port == Instance.Port)
			{
				throw new Exception("端口 " + Instance.Port + " 已被正在运行的实例「" + item.Name + "」占用，请先停止它，或改用其它端口。");
			}
		}
		if (string.IsNullOrEmpty(Instance.JavaPath) || !File.Exists(Instance.JavaPath))
		{
			Instance.JavaPath = FindBestJava(Instance.McVersion);
			if (string.IsNullOrEmpty(Instance.JavaPath))
			{
				throw new Exception("未找到可用的 Java，请先在「设置 - 启动」中导入 Java");
			}
			Save(Instance);
		}
		int num = Math.Max(512, Instance.MemoryMB);
		string arguments;
		if (Instance.ServerType == "Forge" || Instance.ServerType == "NeoForge")
		{
			string text = FindArgsFile(Instance);
			if (!string.IsNullOrEmpty(text))
			{
				WriteUserJvmArgs(Instance);
				arguments = "-Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 -Dsun.stdout.encoding=UTF-8 -Dsun.stderr.encoding=UTF-8 @user_jvm_args.txt @\"" + text + "\" nogui";
			}
			else
			{
				string text2 = FindLegacyForgeJar(Instance);
				if (string.IsNullOrEmpty(text2))
				{
					throw new Exception("找不到 Forge 服务端核心，请重新下载服务端核心");
				}
				arguments = $"-Xms{num / 2}M -Xmx{num}M -Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 -Dsun.stdout.encoding=UTF-8 -Dsun.stderr.encoding=UTF-8 -jar \"{text2}\" nogui";
			}
		}
		else if (Instance.ServerType == "Fabric")
		{
			arguments = $"-Xms{num / 2}M -Xmx{num}M -Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 -Dsun.stdout.encoding=UTF-8 -Dsun.stderr.encoding=UTF-8 -Dfabric.installer.server.gameJar=\"minecraft-server.jar\" -jar \"server.jar\" nogui";
		}
		else
		{
			arguments = $"-Xms{num / 2}M -Xmx{num}M -Dfile.encoding=UTF-8 -Dstdout.encoding=UTF-8 -Dstderr.encoding=UTF-8 -Dsun.stdout.encoding=UTF-8 -Dsun.stderr.encoding=UTF-8 -jar \"server.jar\" nogui";
		}
		ProcessStartInfo processStartInfo = new ProcessStartInfo(Instance.JavaPath, arguments)
		{
			WorkingDirectory = Instance.Dir,
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardInput = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		Process process = new Process
		{
			StartInfo = processStartInfo,
			EnableRaisingEvents = true
		};
		process.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				OnServerLine(Instance, e.Data);
			}
		};
		process.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
		{
			if (e.Data != null)
			{
				OnServerLine(Instance, e.Data);
			}
		};
		process.Exited += delegate
		{
			int exitCode = -1;
			try
			{
				exitCode = process.ExitCode;
			}
			catch (Exception)
			{
			}
			Instance.State = ServerState.Stopped;
			Instance.Proc = null;
			Instance.Stdin = null;
			Instance.IsStarting = false;
			RaiseLine(Instance, "[PCL] 服务器进程已结束（退出码 " + exitCode + "）");
			CloseLog(Instance);
			RaiseStateChanged(Instance);
		};
		Instance.State = ServerState.Starting;
		Instance.IsStarting = true;
		OpenLog(Instance);
		process.Start();
		Instance.Proc = process;
		try
		{
			Instance.Stdin = new StreamWriter(process.StandardInput.BaseStream, new UTF8Encoding(false))
			{
				AutoFlush = true
			};
		}
		catch (Exception ex)
		{
			Logger.Warn(ex, "无法获取服务端标准输入流", (LogBehavior)1, "ModServer.cs");
		}
		process.BeginOutputReadLine();
		process.BeginErrorReadLine();
		Instance.LastStart = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
		Save(Instance);
		RaiseLine(Instance, "[PCL] 正在启动服务器，进程 ID " + process.Id + "，Java：" + Instance.JavaPath);
		RaiseLine(Instance, "[PCL] 工作目录：" + Instance.Dir);
		RaiseStateChanged(Instance);
		Logger.Info("服务器启动：" + Instance.Name + "（" + Instance.ServerType + " " + Instance.McVersion + "，pid " + process.Id + "）", (LogBehavior)0, "ModServer.cs");
	}

	public static void SendCommand(ServerInstance Instance, string Command)
	{
		if (Instance == null || !Instance.IsRunning)
		{
			throw new Exception("服务器尚未运行，无法发送指令");
		}
		if (Instance.Stdin == null)
		{
			throw new Exception("服务器输入流不可用");
		}
		Instance.Stdin.WriteLine(Command);
		RaiseLine(Instance, "> " + Command);
	}

	public static void Stop(ServerInstance Instance, bool Force = false)
	{
		if (Instance == null || !Instance.IsRunning)
		{
			return;
		}
		Instance.State = ServerState.Stopping;
		RaiseStateChanged(Instance);
		if (Force)
		{
			try
			{
				Instance.Proc.Kill();
			}
			catch (Exception ex)
			{
				Logger.Warn(ex, "强制结束服务器进程失败", (LogBehavior)1, "ModServer.cs");
			}
			return;
		}
		try
		{
			SendCommand(Instance, "stop");
		}
		catch (Exception ex2)
		{
			Logger.Warn(ex2, "发送 stop 指令失败，将直接强制结束", (LogBehavior)1, "ModServer.cs");
			try
			{
				Instance.Proc.Kill();
			}
			catch (Exception)
			{
			}
			return;
		}
		Process process = Instance.Proc;
		if (process != null)
		{
			ModBase.RunInNewThread(delegate
			{
				try
				{
					if (!process.WaitForExit(30000))
					{
						process.Kill();
					}
				}
				catch (Exception)
				{
				}
			}, "Server Stop Watchdog");
		}
	}

	public static void StopAll(bool Force)
	{
		EnsureLoaded();
		foreach (ServerInstance item in _Instances)
		{
			try
			{
				Stop(item, Force);
			}
			catch (Exception ex)
			{
				Logger.Warn(ex, "结束服务器失败：" + item.Name, (LogBehavior)1, "ModServer.cs");
			}
		}
	}

	private static void OnServerLine(ServerInstance Instance, string Line)
	{
		try
		{
			if (Instance.IsStarting && Line.Contains("Done ("))
			{
				Instance.IsStarting = false;
				Instance.State = ServerState.Running;
				RaiseStateChanged(Instance);
			}
		}
		catch (Exception)
		{
		}
		try
		{
			StreamWriter logWriter = Instance.LogWriter;
			if (logWriter != null)
			{
				logWriter.WriteLine(DateTime.Now.ToString("HH:mm:ss") + " " + Line);
			}
		}
		catch (Exception)
		{
		}
		RaiseLine(Instance, Line);
	}

	private static void RaiseLine(ServerInstance Instance, string Line)
	{
		try
		{
			LineReceived?.Invoke(Instance, Line);
		}
		catch (Exception ex)
		{
			Logger.Warn(ex, "处理服务器输出时出错", (LogBehavior)1, "ModServer.cs");
		}
	}

	private static void RaiseStateChanged(ServerInstance Instance)
	{
		try
		{
			StateChanged?.Invoke(Instance);
		}
		catch (Exception ex)
		{
			Logger.Warn(ex, "处理服务器状态变化时出错", (LogBehavior)1, "ModServer.cs");
		}
	}

	private static void OpenLog(ServerInstance Instance)
	{
		try
		{
			Directory.CreateDirectory(Instance.Dir + "logs\\");
			string text = Instance.Dir + "logs\\PCL-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".log";
			Instance.LogWriter = new StreamWriter(text, append: true, new UTF8Encoding(false))
			{
				AutoFlush = true
			};
			RaiseLine(Instance, "[PCL] 控制台日志将保存到：" + text);
		}
		catch (Exception ex)
		{
			Logger.Warn(ex, "创建服务器控制台日志失败", (LogBehavior)1, "ModServer.cs");
		}
	}

	private static void CloseLog(ServerInstance Instance)
	{
		try
		{
			Instance.LogWriter?.Dispose();
		}
		catch (Exception)
		{
		}
		Instance.LogWriter = null;
	}

	private static ServerInstance FromJson(JObject Object)
	{
		return new ServerInstance
		{
			Name = (string)Object["name"],
			McVersion = (string)Object["mcVersion"],
			ServerType = (string)Object["serverType"] ?? "Vanilla",
			JavaPath = (string)Object["javaPath"] ?? "",
			MemoryMB = Object["memoryMB"] != null ? (int)Object["memoryMB"] : 2048,
			Port = Object["port"] != null ? (int)Object["port"] : 25565,
			EulaAccepted = Object["eulaAccepted"] != null && (bool)Object["eulaAccepted"],
			LastStart = (string)Object["lastStart"] ?? "",
			LoaderVersion = (string)Object["loaderVersion"] ?? ""
		};
	}

	private static JObject ToJson(ServerInstance Instance)
	{
		return new JObject
		{
			["name"] = Instance.Name,
			["mcVersion"] = Instance.McVersion,
			["serverType"] = Instance.ServerType,
			["javaPath"] = Instance.JavaPath,
			["memoryMB"] = Instance.MemoryMB,
			["port"] = Instance.Port,
			["eulaAccepted"] = Instance.EulaAccepted,
			["lastStart"] = Instance.LastStart,
			["loaderVersion"] = Instance.LoaderVersion
		};
	}
}
