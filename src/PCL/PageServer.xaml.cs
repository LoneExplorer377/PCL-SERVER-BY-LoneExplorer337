using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MeloongCore;
using PCLCS;

namespace PCL;

/// <summary>
/// 服务器模式主页面：实例管理 + 内嵌控制台（不弹出任何命令行窗口）。
/// </summary>
public partial class PageServer : MyPageRight
{
	private static readonly string[] CommonVersions = new string[]
	{
		"26.3", "1.21.7", "1.21.5", "1.21.4", "1.21.1", "1.21", "1.20.6", "1.20.4", "1.20.1", "1.19.4", "1.19.2", "1.18.2", "1.17.1", "1.16.5", "1.15.2", "1.12.2", "1.8.9"
	};

	private readonly List<string> _JavaPaths = new List<string>();

	private readonly List<int> _JavaMajors = new List<int>();

	private bool _IsRefreshing;

	private const int MaxConsoleLines = 1500;

	private ServerInstance Current => ComboServers.SelectedItem is ComboBoxItem comboBoxItem ? comboBoxItem.Tag as ServerInstance : null;

	public PageServer()
	{
		InitializeComponent();
		ModServer.StateChanged -= ModServer_StateChanged;
		ModServer.StateChanged += ModServer_StateChanged;
		ModServer.LineReceived -= ModServer_LineReceived;
		ModServer.LineReceived += ModServer_LineReceived;
		InitWizard();
		InitPropCombos();
		RefreshServers(null);
		AppendLine("[PCL] 服务器模式已就绪。实例文件夹：" + ModServer.RootDir, Brushes.Gray);
		AppendLine("[PCL] 服务器进程不会显示命令行窗口，所有输出都会显示在这里；在下方输入框输入的指令会直接发送给服务端。", Brushes.Gray);
	}

	private void ModServer_StateChanged(ServerInstance Instance)
	{
		ModBase.RunInUi(delegate
		{
			RefreshState();
		});
	}

	private void ModServer_LineReceived(ServerInstance Instance, string Line)
	{
		ModBase.RunInUi(delegate
		{
			ServerInstance current = Current;
			if (current != null && !(current.Name == Instance.Name))
			{
				AppendLine("[" + Instance.Name + "] " + Line, GetLineBrush(Line, dim: true));
			}
			else
			{
				AppendLine(Line, GetLineBrush(Line, dim: false));
			}
		});
	}

	private static Brush GetLineBrush(string Line, bool dim)
	{
		if (Line.StartsWith("[PCL]"))
		{
			return new SolidColorBrush(Color.FromRgb(159, 223, 159));
		}
		if (Line.StartsWith("> "))
		{
			return new SolidColorBrush(Color.FromRgb(127, 179, 255));
		}
		if (Line.Contains("ERROR") || Line.Contains("SEVERE") || Line.Contains("Exception") || Line.Contains("FATAL"))
		{
			return new SolidColorBrush(Color.FromRgb(255, 123, 114));
		}
		if (Line.Contains("WARN"))
		{
			return new SolidColorBrush(Color.FromRgb(224, 179, 77));
		}
		return new SolidColorBrush(dim ? Color.FromRgb(140, 140, 140) : Color.FromRgb(220, 220, 220));
	}

	private void AppendLine(string Line, Brush Color)
	{
		try
		{
			Paragraph paragraph = new Paragraph(new Run(Line))
			{
				Margin = new Thickness(0),
				Foreground = Color
			};
			TxtConsole.Document.Blocks.Add(paragraph);
			while (TxtConsole.Document.Blocks.Count > MaxConsoleLines)
			{
				TxtConsole.Document.Blocks.Remove(TxtConsole.Document.Blocks.FirstBlock);
			}
			TxtConsole.ScrollToEnd();
		}
		catch (Exception)
		{
		}
	}

	private void RefreshServers(ServerInstance Select)
	{
		_IsRefreshing = true;
		try
		{
			string text = Select?.Name;
			if (text == null && Current != null)
			{
				text = Current.Name;
			}
			ComboServers.Items.Clear();
			foreach (ServerInstance item in ModServer.Instances)
			{
				ComboServers.Items.Add(new MyComboBoxItem
				{
					Content = item.ToString(),
					Tag = item
				});
			}
			int num = -1;
			if (text != null)
			{
				for (int i = 0; i < ComboServers.Items.Count; i++)
				{
					if (((ServerInstance)((ComboBoxItem)ComboServers.Items[i]).Tag).Name == text)
					{
						num = i;
						break;
					}
				}
			}
			if (num < 0 && ComboServers.Items.Count > 0)
			{
				num = 0;
			}
			ComboServers.SelectedIndex = num;
			ModServer.SelectedInstance = Current;
		}
		catch (Exception ex)
		{
			Logger.Warn(ex, "刷新服务器实例列表失败", (LogBehavior)1, "PageServer.xaml.cs");
		}
		finally
		{
			_IsRefreshing = false;
		}
		RefreshState();
	}

	private void RefreshState()
	{
		try
		{
			if (ModMain.FrmLaunchLeft != null)
			{
				ModMain.FrmLaunchLeft.RefreshServerMode();
			}
		}
		catch (Exception ex)
		{
			Logger.Warn(ex, "刷新启动页服务器面板失败", (LogBehavior)1, "PageServer.xaml.cs");
		}
		ServerInstance current = Current;
		bool flag = current != null && current.IsRunning;
		BtnServerStart.IsEnabled = current != null && !flag;
		BtnServerStop.IsEnabled = flag;
		BtnServerKill.IsEnabled = flag;
		BtnServerSend.IsEnabled = flag;
		BtnServerDelete.IsEnabled = current != null && !flag;
		BtnServerFolder.IsEnabled = current != null;
		BtnServerRes.IsEnabled = current != null;
		BtnServerProps.IsEnabled = current != null;
		BtnServerPlayers.IsEnabled = current != null;
		BtnServerCrash.IsEnabled = current != null;
		if (current == null)
		{
			DotServerState.Fill = new SolidColorBrush(Color.FromRgb(154, 154, 154));
			LabServerState.Text = "未运行";
			LabServerInfo.Text = "尚未创建任何服务器实例。点击「新建」创建一个专属服务端，它与启动游戏完全独立。";
			return;
		}
		if (flag)
		{
			DotServerState.Fill = current.State == ServerState.Starting ? new SolidColorBrush(Color.FromRgb(224, 179, 77)) : new SolidColorBrush(Color.FromRgb(80, 200, 120));
			LabServerState.Text = current.State == ServerState.Starting ? "正在启动…" : (current.State == ServerState.Stopping ? "正在停止…" : "运行中");
		}
		else
		{
			DotServerState.Fill = new SolidColorBrush(Color.FromRgb(154, 154, 154));
			LabServerState.Text = "未运行";
		}
		string text = current.HasJar ? "已下载" : "未下载，无法启动";
		string text2 = string.IsNullOrEmpty(current.JavaPath) ? "未指定（启动时自动选择）" : current.JavaPath;
		LabServerInfo.Text = $"类型：{ModServer.GetTypeName(current.ServerType)}    游戏版本：{current.McVersion}    端口：{current.Port}    内存：{current.MemoryMB} MB\n核心：{text}\n文件夹：{current.Dir}\nJava：{text2}";
	}

	private void ComboServers_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_IsRefreshing)
		{
			return;
		}
		ModServer.SelectedInstance = Current;
		RefreshState();
	}

	/// <summary>从其它页面（版本选择、启动页）跳进服务器控制台时，指定要显示的实例。</summary>
	public void SelectInstance(ServerInstance Instance)
	{
		ModServer.SelectedInstance = Instance;
		RefreshServers(Instance);
	}

	private void BtnServerNew_Click(object sender, MouseButtonEventArgs e)
	{
		TextNewName.Text = "";
		CheckNewEula.Checked = false;
		PanServerNew.Visibility = Visibility.Visible;
		TextNewName.Focus();
	}

	public void OpenNewWizard(string McVersion)
	{
		TextNewName.Text = "";
		TextNewVersion.Text = McVersion ?? "";
		CheckNewEula.Checked = false;
		PanServerNew.Visibility = Visibility.Visible;
		SelectDefaultJava();
		TextNewName.Focus();
	}

	private void BtnNewCancel_Click(object sender, MouseButtonEventArgs e)
	{
		PanServerNew.Visibility = Visibility.Collapsed;
	}

	private void ComboNewType_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
	}

	private void BtnNewCreate_Click(object sender, MouseButtonEventArgs e)
	{
		string text = (TextNewName.Text ?? "").Trim();
		if (string.IsNullOrEmpty(text))
		{
			ModMain.MyMsgBox("请填写服务器名称！", "无法创建", IsWarn: true);
			return;
		}
		string text2 = (TextNewVersion.Text ?? "").Trim();
		if (string.IsNullOrEmpty(text2))
		{
			ModMain.MyMsgBox("请填写游戏版本，例如 1.21.1！", "无法创建", IsWarn: true);
			return;
		}
		if (!int.TryParse(TextNewMemory.Text, out var result) || result < 256)
		{
			ModMain.MyMsgBox("内存分配必须是不小于 256 的整数（单位 MB）！", "无法创建", IsWarn: true);
			return;
		}
		if (!int.TryParse(TextNewPort.Text, out var result2) || result2 < 1 || result2 > 65535)
		{
			ModMain.MyMsgBox("端口必须是 1 - 65535 之间的整数！", "无法创建", IsWarn: true);
			return;
		}
		if (!CheckNewEula.Checked)
		{
			ModMain.MyMsgBox("必须勾选同意 Minecraft EULA 才能创建服务器。", "无法创建", IsWarn: true);
			return;
		}
		string serverType = ComboNewType.SelectedIndex switch
		{
			0 => "Vanilla", 
			1 => "Paper", 
			2 => "Fabric", 
			3 => "Forge", 
			4 => "NeoForge", 
			_ => "Vanilla", 
		};
		string javaPath = "";
		int selectedIndex = ComboNewJava.SelectedIndex;
		if (selectedIndex >= 0 && selectedIndex < _JavaPaths.Count)
		{
			javaPath = _JavaPaths[selectedIndex];
		}
		try
		{
			ServerInstance serverInstance = ModServer.Create(text, text2, serverType, javaPath, result, result2);
			serverInstance.EulaAccepted = true;
			ModServer.WriteEula(serverInstance);
			ModServer.Save(serverInstance);
			PanServerNew.Visibility = Visibility.Collapsed;
			RefreshServers(serverInstance);
			AppendLine("[PCL] 已创建服务器实例：" + serverInstance.Name + "（" + ModServer.GetTypeName(serverInstance.ServerType) + " " + serverInstance.McVersion + "）", null);
			DownloadJarAsync(serverInstance);
		}
		catch (Exception ex)
		{
			ModMain.MyMsgBox(ex.Message, "无法创建服务器", IsWarn: true);
		}
	}

	private void DownloadJarAsync(ServerInstance Instance)
	{
		LabServerState.Text = "正在下载服务端核心…";
		ModBase.RunInNewThread(delegate
		{
			string last = "";
			try
			{
				ModServer.DownloadServerJar(Instance, delegate(int pct, string msg)
				{
					if (msg != last)
					{
						last = msg;
						ModBase.RunInUi(delegate
						{
							LabServerInfo.Text = msg;
						});
					}
				});
				ModBase.RunInUi(delegate
				{
					AppendLine("[PCL] 服务端核心下载完成：" + Instance.JarPath, new SolidColorBrush(Color.FromRgb(159, 223, 159)));
					RefreshState();
				});
			}
			catch (Exception ex)
			{
				ModBase.RunInUi(delegate
				{
					AppendLine("[PCL] 下载服务端核心失败：" + ex.Message, new SolidColorBrush(Color.FromRgb(255, 123, 114)));
					RefreshState();
					ModMain.MyMsgBox("下载服务端核心失败：" + ex.Message + "\n\n可以删除该实例后重试，或检查网络连接。", "下载失败", IsWarn: true);
				});
			}
		}, "Server Jar Download");
	}

	private void BtnServerDelete_Click(object sender, MouseButtonEventArgs e)
	{
		ServerInstance current = Current;
		if (current == null)
		{
			return;
		}
		if (current.IsRunning)
		{
			ModMain.MyMsgBox("服务器正在运行，请先停止后再删除。", "无法删除", IsWarn: true);
			return;
		}
		if (ModMain.MyMsgBox("确定要删除服务器「" + current.Name + "」吗？\n该实例的全部文件（包括世界存档、插件与 Mod）都会被永久删除！", "删除服务器", "删除", "取消", IsWarn: true) != 1)
		{
			return;
		}
		try
		{
			ModServer.Delete(current);
			AppendLine("[PCL] 已删除服务器实例：" + current.Name, Brushes.Gray);
			RefreshServers(null);
		}
		catch (Exception ex)
		{
			ModMain.MyMsgBox(ex.Message, "无法删除", IsWarn: true);
		}
	}

	private void BtnServerFolder_Click(object sender, MouseButtonEventArgs e)
	{
		ServerInstance current = Current;
		if (current == null)
		{
			return;
		}
		try
		{
			Directory.CreateDirectory(current.Dir);
			Process.Start("explorer.exe", "\"" + current.Dir.TrimEnd('\\') + "\"");
		}
		catch (Exception ex)
		{
			ModMain.MyMsgBox(ex.Message, "无法打开文件夹", IsWarn: true);
		}
	}

	private void BtnServerStart_Click(object sender, MouseButtonEventArgs e)
	{
		ServerInstance current = Current;
		if (current == null)
		{
			return;
		}
		if (!current.EulaAccepted)
		{
			if (ModMain.MyMsgBox("启动服务器表示你已阅读并同意 Minecraft 最终用户许可协议（EULA）。\nhttps://aka.ms/MinecraftEULA\n\n是否同意并继续？", "同意 EULA", "同意并启动", "取消", IsWarn: false, HighLight: true, ForceWait: true) != 1)
			{
				return;
			}
			current.EulaAccepted = true;
			try
			{
				ModServer.WriteEula(current);
				ModServer.Save(current);
			}
			catch (Exception ex)
			{
				ModMain.MyMsgBox(ex.Message, "写入 eula.txt 失败", IsWarn: true);
				return;
			}
		}
		try
		{
			ModServer.Start(current);
			RefreshState();
		}
		catch (Exception ex2)
		{
			ModMain.MyMsgBox(ex2.Message, "无法启动服务器", IsWarn: true);
		}
	}

	private void BtnServerStop_Click(object sender, MouseButtonEventArgs e)
	{
		ServerInstance current = Current;
		if (current == null || !current.IsRunning)
		{
			return;
		}
		try
		{
			ModServer.Stop(current);
			RefreshState();
		}
		catch (Exception ex)
		{
			ModMain.MyMsgBox(ex.Message, "无法停止服务器", IsWarn: true);
		}
	}

	private void BtnServerKill_Click(object sender, MouseButtonEventArgs e)
	{
		ServerInstance current = Current;
		if (current == null || !current.IsRunning)
		{
			return;
		}
		if (ModMain.MyMsgBox("强制结束服务器会丢失尚未保存的世界数据！\n确定要强制结束吗？", "强制结束", "强制结束", "取消", IsWarn: true) != 1)
		{
			return;
		}
		try
		{
			ModServer.Stop(current, Force: true);
			RefreshState();
		}
		catch (Exception ex)
		{
			ModMain.MyMsgBox(ex.Message, "无法结束服务器", IsWarn: true);
		}
	}

	private void BtnServerSend_Click(object sender, MouseButtonEventArgs e)
	{
		SendCurrentCommand();
	}

	private void TextCommand_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return || e.Key == Key.Enter)
		{
			e.Handled = true;
			SendCurrentCommand();
		}
	}

	private void SendCurrentCommand()
	{
		ServerInstance current = Current;
		string text = (TextCommand.Text ?? "").Trim();
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		if (current == null || !current.IsRunning)
		{
			ModMain.MyMsgBox("服务器尚未运行，无法发送指令。", "无法发送", IsWarn: true);
			return;
		}
		try
		{
			ModServer.SendCommand(current, text);
			TextCommand.Text = "";
		}
		catch (Exception ex)
		{
			ModMain.MyMsgBox(ex.Message, "无法发送指令", IsWarn: true);
		}
	}

	private void InitWizard()
	{
		try
		{
			TextNewVersion.Text = "1.21.1";
			ComboNewType.Items.Add(new MyComboBoxItem
			{
				Content = "原版服务端（Vanilla）"
			});
			ComboNewType.Items.Add(new MyComboBoxItem
			{
				Content = "Paper（支持插件）"
			});
			ComboNewType.Items.Add(new MyComboBoxItem
			{
				Content = "Fabric（支持 Mod）"
			});
			ComboNewType.Items.Add(new MyComboBoxItem
			{
				Content = "Forge（支持 Mod）"
			});
			ComboNewType.Items.Add(new MyComboBoxItem
			{
				Content = "NeoForge（支持 Mod）"
			});
			ComboNewType.SelectedIndex = 0;
			List<string> list = new List<string>();
			try
			{
				foreach (Java item in Configs.JavaList.Get())
				{
					if (item.JavaExePath == null || !File.Exists(item.JavaExePath))
					{
						continue;
					}
					list.Add(item.Version + "  " + item.JavaExePath);
					_JavaPaths.Add(item.JavaExePath);
					_JavaMajors.Add(item.Version.Major);
				}
			}
			catch (Exception ex)
			{
				Logger.Warn(ex, "读取 Java 列表失败", (LogBehavior)1, "PageServer.xaml.cs");
			}
			if (list.Count == 0)
			{
				string text = ModServer.FindBestJava();
				if (!string.IsNullOrEmpty(text))
				{
					list.Add("自动检测  " + text);
					_JavaPaths.Add(text);
					_JavaMajors.Add(0);
				}
				else
				{
					list.Add("自动检测（未找到 Java，请先在设置中导入）");
					_JavaPaths.Add("");
					_JavaMajors.Add(0);
				}
			}
			ComboNewJava.Items.Clear();
			foreach (string item2 in list)
			{
				ComboNewJava.Items.Add(new MyComboBoxItem
				{
					Content = item2
				});
			}
			ComboNewJava.SelectedIndex = 0;
			SelectDefaultJava();
		}
		catch (Exception ex2)
		{
			Logger.Warn(ex2, "初始化服务器向导失败", (LogBehavior)1, "PageServer.xaml.cs");
		}
	}

	private void SelectDefaultJava()
	{
		if (_JavaPaths.Count == 0)
		{
			return;
		}
		int need = ModServer.RequiredJavaMajor((TextNewVersion.Text ?? "").Trim());
		int num = -1;
		int num2 = int.MaxValue;
		for (int i = 0; i < _JavaMajors.Count; i++)
		{
			int num3 = _JavaMajors[i];
			if (num3 > 0 && num3 >= need && num3 < num2)
			{
				num2 = num3;
				num = i;
			}
		}
		if (num < 0)
		{
			int num4 = -1;
			int num5 = -1;
			for (int j = 0; j < _JavaMajors.Count; j++)
			{
				if (_JavaMajors[j] > num5)
				{
					num5 = _JavaMajors[j];
					num4 = j;
				}
			}
			num = num4;
		}
		if (num >= 0)
		{
			ComboNewJava.SelectedIndex = num;
		}
	}

	private void TextNewVersion_TextChanged(object sender, TextChangedEventArgs e)
	{
		SelectDefaultJava();
	}

	// ================= 服务端资源（Modrinth） =================

	private void BtnServerRes_Click(object sender, MouseButtonEventArgs e)
	{
		ServerInstance current = Current;
		if (current == null)
		{
			ModMain.MyMsgBox("请先在「实例」里创建或选择一个服务器实例。", "提示");
			return;
		}
		if (!ModServerResource.IsSupported(current))
		{
			ModMain.MyMsgBox("当前实例是「" + ModServer.GetTypeName(current.ServerType) + "」服务端，不支持 Mod 与插件。\n\n原版服务端只支持数据包；需要插件请新建 Paper 服务端，需要 Mod 请使用 Fabric / Forge / NeoForge 服务端。", "暂不支持");
			return;
		}
		TextResSearch.Text = "";
		PanServerRes.Visibility = Visibility.Visible;
		BeginSearch(string.Empty);
	}

	private void BtnResClose_Click(object sender, MouseButtonEventArgs e)
	{
		PanServerRes.Visibility = Visibility.Collapsed;
	}

	private void BtnResSearch_Click(object sender, MouseButtonEventArgs e)
	{
		BeginSearch(TextResSearch.Text);
	}

	private void TextResSearch_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			BeginSearch(TextResSearch.Text);
		}
	}

	private void BeginSearch(string Query)
	{
		ServerInstance current = Current;
		if (current == null)
		{
			return;
		}
		PanResList.Children.Clear();
		LabResStatus.Text = "正在向 Modrinth 搜索「" + (string.IsNullOrEmpty(Query) ? "热门" : Query) + "」…";
		ModBase.RunInNewThread(delegate
		{
			List<ServerResourceHit> list = null;
			string text = null;
			try
			{
				list = ModServerResource.Search(current, Query, 20);
			}
			catch (Exception ex)
			{
				text = ex.Message;
			}
			ModBase.RunInUi(delegate
			{
				if (Current != current)
				{
					return;
				}
				PanResList.Children.Clear();
				if (text != null)
				{
					LabResStatus.Text = "搜索失败：" + text;
					return;
				}
				int num = 0;
				foreach (ServerResourceHit item in list)
				{
					if (item.ServerSide == "unsupported")
					{
						continue;
					}
					PanResList.Children.Add(BuildResRow(current, item));
					num++;
				}
				LabResStatus.Text = ((num == 0) ? ("没有找到适用于 " + current.McVersion + " 的" + ModServerResource.GetTypeName(current) + "。") : ("共 " + num + " 个结果，安装目录：" + ModServerResource.TargetFolderOf(current)));
			});
		}, "Server resource search");
	}

	private UIElement BuildResRow(ServerInstance Instance, ServerResourceHit Hit)
	{
		Grid grid = new Grid
		{
			Margin = new Thickness(0, 0, 0, 8)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		Border border = new Border
		{
			Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
			CornerRadius = new CornerRadius(4.0),
			Padding = new Thickness(10.0, 8.0, 10.0, 8.0),
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
		};
		StackPanel stackPanel = new StackPanel();
		stackPanel.Children.Add(new TextBlock
		{
			Text = Hit.Title,
			FontWeight = FontWeights.Bold,
			TextTrimming = TextTrimming.CharacterEllipsis
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = Hit.Description,
			TextWrapping = TextWrapping.Wrap,
			MaxHeight = 34.0,
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0),
			Opacity = 0.7
		});
		string text = string.Join(", ", Hit.Categories.ToArray());
		stackPanel.Children.Add(new TextBlock
		{
			Text = text + "   下载量 " + Hit.Downloads,
			FontSize = 11.0,
			Opacity = 0.55,
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0),
			TextTrimming = TextTrimming.CharacterEllipsis
		});
		border.Child = stackPanel;
		grid.Children.Add(border);
		MyButton myButton = new MyButton
		{
			Text = "安装",
			Width = 68.0,
			Height = 30.0,
			VerticalAlignment = VerticalAlignment.Center,
			Tag = Hit
		};
		myButton.Click += BtnResInstall_Click;
		Grid.SetColumn(myButton, 1);
		grid.Children.Add(myButton);
		return grid;
	}

	private void BtnResInstall_Click(object sender, MouseButtonEventArgs e)
	{
		ServerInstance current = Current;
		ServerResourceHit serverResourceHit = ((sender is FrameworkElement frameworkElement) ? frameworkElement.Tag : null) as ServerResourceHit;
		if (current == null || serverResourceHit == null)
		{
			return;
		}
		AppendLine("[PCL] 开始安装服务端资源：" + serverResourceHit.Title + "（" + ModServerResource.GetTypeName(current) + "）", new SolidColorBrush(Color.FromRgb(159, 223, 159)));
		LabResStatus.Text = "正在安装 " + serverResourceHit.Title + " …（进度见控制台）";
		ModBase.RunInNewThread(delegate
		{
			string text = null;
			try
			{
				ModServerResource.Install(current, serverResourceHit, delegate(string line)
				{
					ModBase.RunInUi(delegate
					{
						AppendLine(line, new SolidColorBrush(Color.FromRgb(159, 223, 159)));
					});
				});
			}
			catch (Exception ex)
			{
				text = ex.Message;
			}
			ModBase.RunInUi(delegate
			{
				if (text != null)
				{
					AppendLine("[PCL] 安装失败：" + text, new SolidColorBrush(Color.FromRgb(255, 123, 114)));
					LabResStatus.Text = "安装失败：" + text;
				}
				else
				{
					AppendLine("[PCL] 安装完成。如果服务器正在运行，需要重启后生效。", new SolidColorBrush(Color.FromRgb(159, 223, 159)));
					LabResStatus.Text = "安装完成。如果服务器正在运行，需要重启后生效。";
				}
			});
		}, "Server resource install");
	}
	// ================= 服务端整合包（Modrinth） =================

	private string PackTargetVersion()
	{
		ServerInstance current = Current;
		if (current != null)
		{
			return current.McVersion;
		}
		return (TextNewVersion.Text ?? "").Trim();
	}

	private void BtnServerPack_Click(object sender, MouseButtonEventArgs e)
	{
		TextPackSearch.Text = "";
		PanServerPack.Visibility = Visibility.Visible;
		BeginPackSearch(string.Empty);
	}

	private void BtnPackClose_Click(object sender, MouseButtonEventArgs e)
	{
		PanServerPack.Visibility = Visibility.Collapsed;
	}

	private void BtnPackSearch_Click(object sender, MouseButtonEventArgs e)
	{
		BeginPackSearch(TextPackSearch.Text);
	}

	private void TextPackSearch_KeyDown(object sender, KeyEventArgs e)
	{
		if (e.Key == Key.Return)
		{
			BeginPackSearch(TextPackSearch.Text);
		}
	}

	private void BeginPackSearch(string Query)
	{
		string Version = PackTargetVersion();
		PanPackList.Children.Clear();
		LabPackStatus.Text = "正在向 Modrinth 搜索整合包「" + (string.IsNullOrEmpty(Query) ? "热门" : Query) + "」（" + Version + "）…";
		ModBase.RunInNewThread(delegate
		{
			List<ServerPackHit> list = null;
			string text = null;
			try
			{
				list = ModServerPack.Search(Query, Version, 20);
			}
			catch (Exception ex)
			{
				text = ex.Message;
			}
			ModBase.RunInUi(delegate
			{
				PanPackList.Children.Clear();
				if (text != null)
				{
					LabPackStatus.Text = "搜索失败：" + text;
					return;
				}
				int num = 0;
				foreach (ServerPackHit item in list)
				{
					PanPackList.Children.Add(BuildPackRow(item));
					num++;
				}
				LabPackStatus.Text = ((num == 0) ? ("没有找到适用于 " + Version + " 的服务端整合包。") : ("共 " + num + " 个整合包，安装时会新建独立实例。"));
			});
		}, "Server pack search");
	}

	private UIElement BuildPackRow(ServerPackHit Hit)
	{
		Grid grid = new Grid
		{
			Margin = new Thickness(0, 0, 0, 8)
		};
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = new GridLength(1.0, GridUnitType.Star)
		});
		grid.ColumnDefinitions.Add(new ColumnDefinition
		{
			Width = GridLength.Auto
		});
		Border border = new Border
		{
			Background = new SolidColorBrush(Color.FromRgb(245, 245, 245)),
			CornerRadius = new CornerRadius(4.0),
			Padding = new Thickness(10.0, 8.0, 10.0, 8.0),
			Margin = new Thickness(0.0, 0.0, 8.0, 0.0)
		};
		StackPanel stackPanel = new StackPanel();
		stackPanel.Children.Add(new TextBlock
		{
			Text = Hit.Title,
			FontWeight = FontWeights.Bold,
			TextTrimming = TextTrimming.CharacterEllipsis
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = Hit.Description,
			TextWrapping = TextWrapping.Wrap,
			MaxHeight = 34.0,
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0),
			Opacity = 0.7
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "下载量 " + Hit.Downloads,
			FontSize = 11.0,
			Opacity = 0.55,
			Margin = new Thickness(0.0, 2.0, 0.0, 0.0)
		});
		border.Child = stackPanel;
		grid.Children.Add(border);
		MyButton myButton = new MyButton
		{
			Text = "安装",
			Width = 68.0,
			Height = 30.0,
			VerticalAlignment = VerticalAlignment.Center,
			Tag = Hit
		};
		myButton.Click += BtnPackInstall_Click;
		Grid.SetColumn(myButton, 1);
		grid.Children.Add(myButton);
		return grid;
	}

	private void BtnPackInstall_Click(object sender, MouseButtonEventArgs e)
	{
		ServerPackHit serverPackHit = ((sender is FrameworkElement frameworkElement) ? frameworkElement.Tag : null) as ServerPackHit;
		if (serverPackHit == null)
		{
			return;
		}
		if (ModMain.MyMsgBox("将把整合包「" + serverPackHit.Title + "」安装成一个新的服务器实例（只下载服务端需要的文件）。\n整个过程可能需要几分钟，期间请勿关闭 PCL。\n\n是否继续？", "安装服务端整合包", "开始安装", "取消", IsWarn: false, HighLight: true, ForceWait: true) != 1)
		{
			return;
		}
		string Version = PackTargetVersion();
		LabPackStatus.Text = "正在准备安装「" + serverPackHit.Title + "」…";
		AppendLine("[PCL] 开始安装服务端整合包：" + serverPackHit.Title, new SolidColorBrush(Color.FromRgb(159, 223, 159)));
		ModBase.RunInNewThread(delegate
		{
			string text = null;
			ServerInstance serverInstance = null;
			string last = "";
			try
			{
				serverInstance = ModServerPack.Install(serverPackHit, Version, delegate(int Pct, string Msg)
				{
					if (Msg != last)
					{
						last = Msg;
						ModBase.RunInUi(delegate
						{
							LabPackStatus.Text = Pct + "%  " + Msg;
						});
					}
				});
			}
			catch (Exception ex)
			{
				text = ex.Message;
			}
			ModBase.RunInUi(delegate
			{
				if (text != null)
				{
					LabPackStatus.Text = "安装失败：" + text;
					AppendLine("[PCL] 整合包安装失败：" + text, new SolidColorBrush(Color.FromRgb(255, 123, 114)));
					ModMain.MyMsgBox("安装整合包失败：" + text + "\n\n实例文件已保留在 PCL\\Server 下，可直接删除。", "安装失败", IsWarn: true);
					return;
				}
				PanServerPack.Visibility = Visibility.Collapsed;
				AppendLine("[PCL] 整合包安装完成，已创建实例：" + serverInstance.Name, new SolidColorBrush(Color.FromRgb(159, 223, 159)));
				RefreshServers(serverInstance);
			});
		}, "Server pack install");
	}

	// ================= server.properties 图形化 =================

	private void InitPropCombos()
	{
		ComboPropDifficulty.Items.Add(new MyComboBoxItem { Content = "和平（peaceful）" });
		ComboPropDifficulty.Items.Add(new MyComboBoxItem { Content = "简单（easy）" });
		ComboPropDifficulty.Items.Add(new MyComboBoxItem { Content = "普通（normal）" });
		ComboPropDifficulty.Items.Add(new MyComboBoxItem { Content = "困难（hard）" });
		ComboPropOnlineMode.Items.Add(new MyComboBoxItem { Content = "开启（仅正版账号）" });
		ComboPropOnlineMode.Items.Add(new MyComboBoxItem { Content = "关闭（离线 / 盗版可进）" });
		ComboPropGamemode.Items.Add(new MyComboBoxItem { Content = "生存（survival）" });
		ComboPropGamemode.Items.Add(new MyComboBoxItem { Content = "创造（creative）" });
		ComboPropGamemode.Items.Add(new MyComboBoxItem { Content = "冒险（adventure）" });
		ComboPropGamemode.Items.Add(new MyComboBoxItem { Content = "旁观（spectator）" });
		ComboPropPvp.Items.Add(new MyComboBoxItem { Content = "开启" });
		ComboPropPvp.Items.Add(new MyComboBoxItem { Content = "关闭" });
		ComboPropWhitelist.Items.Add(new MyComboBoxItem { Content = "开启" });
		ComboPropWhitelist.Items.Add(new MyComboBoxItem { Content = "关闭" });
	}

	private static string PropGet(Dictionary<string, string> Dict, string Key, string Default)
	{
		return Dict.TryGetValue(Key, out var value) && !string.IsNullOrEmpty(value) ? value : Default;
	}

	private static int PropIndexOf(string[] Values, string Value)
	{
		for (int i = 0; i < Values.Length; i++)
		{
			if (Values[i] == Value)
			{
				return i;
			}
		}
		return 0;
	}

	private void BtnServerProps_Click(object sender, MouseButtonEventArgs e)
	{
		ServerInstance current = Current;
		if (current == null)
		{
			ModMain.MyMsgBox("请先选择一个服务器实例。", "提示");
			return;
		}
		try
		{
			ModServer.WriteProperties(current);
			Dictionary<string, string> dictionary = new Dictionary<string, string>();
			foreach (KeyValuePair<string, string> item in ModServer.ReadProperties(current))
			{
				dictionary[item.Key] = item.Value;
			}
			TextPropMotd.Text = PropGet(dictionary, "motd", "PCL 服务器 - " + current.Name);
			TextPropMaxPlayers.Text = PropGet(dictionary, "max-players", "20");
			TextPropPort.Text = PropGet(dictionary, "server-port", current.Port.ToString());
			TextPropViewDistance.Text = PropGet(dictionary, "view-distance", "10");
			TextPropSimDistance.Text = PropGet(dictionary, "simulation-distance", "10");
			TextPropSpawnProtection.Text = PropGet(dictionary, "spawn-protection", "16");
			TextPropServerIp.Text = PropGet(dictionary, "server-ip", "");
			ComboPropDifficulty.SelectedIndex = PropIndexOf(new string[4] { "peaceful", "easy", "normal", "hard" }, PropGet(dictionary, "difficulty", "easy"));
			ComboPropOnlineMode.SelectedIndex = (PropGet(dictionary, "online-mode", "true") == "true") ? 0 : 1;
			ComboPropGamemode.SelectedIndex = PropIndexOf(new string[4] { "survival", "creative", "adventure", "spectator" }, PropGet(dictionary, "gamemode", "survival"));
			ComboPropPvp.SelectedIndex = (PropGet(dictionary, "pvp", "true") == "true") ? 0 : 1;
			ComboPropWhitelist.SelectedIndex = (PropGet(dictionary, "white-list", "false") == "true") ? 0 : 1;
			LabPropsHint.Text = (current.IsRunning ? "服务器正在运行，修改会在重启后生效。" : "修改会在下次启动服务器时生效。") + "\n配置文件：" + current.Dir + "server.properties";
			PanServerProps.Visibility = Visibility.Visible;
		}
		catch (Exception ex)
		{
			ModMain.MyMsgBox(ex.Message, "无法读取服务器设置", IsWarn: true);
		}
	}

	private void BtnPropsClose_Click(object sender, MouseButtonEventArgs e)
	{
		PanServerProps.Visibility = Visibility.Collapsed;
	}

	private void BtnPropsSave_Click(object sender, MouseButtonEventArgs e)
	{
		ServerInstance current = Current;
		if (current == null)
		{
			return;
		}
		if (!int.TryParse(TextPropMaxPlayers.Text, out var result) || result < 1 || result > 1000)
		{
			ModMain.MyMsgBox("人数上限必须是 1 - 1000 之间的整数！", "无法保存", IsWarn: true);
			return;
		}
		if (!int.TryParse(TextPropPort.Text, out var result2) || result2 < 1 || result2 > 65535)
		{
			ModMain.MyMsgBox("端口必须是 1 - 65535 之间的整数！", "无法保存", IsWarn: true);
			return;
		}
		if (!int.TryParse(TextPropViewDistance.Text, out var result3) || result3 < 3 || result3 > 32)
		{
			ModMain.MyMsgBox("视距必须是 3 - 32 之间的整数！", "无法保存", IsWarn: true);
			return;
		}
		if (!int.TryParse(TextPropSimDistance.Text, out var result4) || result4 < 3 || result4 > 32)
		{
			ModMain.MyMsgBox("模拟距离必须是 3 - 32 之间的整数！", "无法保存", IsWarn: true);
			return;
		}
		if (!int.TryParse(TextPropSpawnProtection.Text, out var result5) || result5 < 0 || result5 > 1000)
		{
			ModMain.MyMsgBox("出生点保护必须是 0 - 1000 之间的整数！", "无法保存", IsWarn: true);
			return;
		}
		try
		{
			Dictionary<string, string> dictionary = new Dictionary<string, string>
			{
				["motd"] = (TextPropMotd.Text ?? "").Replace("\\n", " ").Trim(),
				["max-players"] = result.ToString(),
				["server-port"] = result2.ToString(),
				["view-distance"] = result3.ToString(),
				["simulation-distance"] = result4.ToString(),
				["spawn-protection"] = result5.ToString(),
				["server-ip"] = (TextPropServerIp.Text ?? "").Trim(),
				["difficulty"] = PropPick(ComboPropDifficulty.SelectedIndex, new string[4] { "peaceful", "easy", "normal", "hard" }),
				["gamemode"] = ((ComboPropGamemode.SelectedIndex >= 0) ? new string[4] { "survival", "creative", "adventure", "spectator" }[ComboPropGamemode.SelectedIndex] : "survival"),
				["online-mode"] = ((ComboPropOnlineMode.SelectedIndex == 0) ? "true" : "false"),
				["pvp"] = ((ComboPropPvp.SelectedIndex == 0) ? "true" : "false"),
				["white-list"] = ((ComboPropWhitelist.SelectedIndex == 0) ? "true" : "false")
			};
			ModServer.SaveProperties(current, dictionary);
			if (current.Port != result2)
			{
				current.Port = result2;
				ModServer.Save(current);
			}
			PanServerProps.Visibility = Visibility.Collapsed;
			RefreshState();
			AppendLine("[PCL] 已保存服务器设置（server.properties）", new SolidColorBrush(Color.FromRgb(159, 223, 159)));
		}
		catch (Exception ex)
		{
			ModMain.MyMsgBox(ex.Message, "无法保存服务器设置", IsWarn: true);
		}
	}

	private static string PropPick(int Index, string[] Values)
	{
		return (Index >= 0 && Index < Values.Length) ? Values[Index] : Values[0];
	}

	// ================= 白名单 / OP =================

	private void BtnServerPlayers_Click(object sender, MouseButtonEventArgs e)
	{
		if (Current == null)
		{
			ModMain.MyMsgBox("请先选择一个服务器实例。", "提示");
			return;
		}
		RefreshPlayersStatus();
		PanServerPlayers.Visibility = Visibility.Visible;
	}

	private void BtnPlayersClose_Click(object sender, MouseButtonEventArgs e)
	{
		PanServerPlayers.Visibility = Visibility.Collapsed;
	}

	private void RefreshPlayersStatus()
	{
		ServerInstance current = Current;
		if (current == null)
		{
			return;
		}
		List<string> list = ModServer.ReadPlayerList(current, "whitelist.json");
		List<string> list2 = ModServer.ReadPlayerList(current, "ops.json");
		string text = ((list.Count == 0) ? "（空）" : string.Join("、", list.ToArray()));
		string text2 = ((list2.Count == 0) ? "（空）" : string.Join("、", list2.ToArray()));
		string text3 = (current.IsRunning ? "服务器正在运行，操作会立即生效。" : "服务器未运行：请先「启动」服务器，这些操作才能生效。");
		LabPlayersStatus.Text = "白名单（" + list.Count + "）：" + text + "\nOP（" + list2.Count + "）：" + text2 + "\n\n" + text3;
		bool isEnabled = current.IsRunning;
		BtnPlayerWhitelistAdd.IsEnabled = isEnabled;
		BtnPlayerWhitelistRemove.IsEnabled = isEnabled;
		BtnPlayerOp.IsEnabled = isEnabled;
		BtnPlayerDeOp.IsEnabled = isEnabled;
	}

	private void SendPlayerCommand(string Command, string SuccessHint)
	{
		ServerInstance current = Current;
		if (current == null)
		{
			return;
		}
		string text = (TextPlayerName.Text ?? "").Trim();
		if (string.IsNullOrEmpty(text))
		{
			ModMain.MyMsgBox("请先填写玩家名。", "提示", IsWarn: true);
			return;
		}
		if (!current.IsRunning)
		{
			ModMain.MyMsgBox("服务器尚未运行，无法执行该操作。请先启动服务器。", "无法执行", IsWarn: true);
			return;
		}
		try
		{
			ModServer.SendCommand(current, Command + " " + text);
			AppendLine("[PCL] 已发送指令：" + Command + " " + text + "（" + SuccessHint + "）", new SolidColorBrush(Color.FromRgb(159, 223, 159)));
			ModBase.RunInUi(delegate
			{
				RefreshPlayersStatus();
			});
		}
		catch (Exception ex)
		{
			ModMain.MyMsgBox(ex.Message, "无法发送指令", IsWarn: true);
		}
	}

	private void BtnPlayerWhitelistAdd_Click(object sender, MouseButtonEventArgs e)
	{
		SendPlayerCommand("whitelist add", "加入白名单");
	}

	private void BtnPlayerWhitelistRemove_Click(object sender, MouseButtonEventArgs e)
	{
		SendPlayerCommand("whitelist remove", "移出白名单");
	}

	private void BtnPlayerOp_Click(object sender, MouseButtonEventArgs e)
	{
		SendPlayerCommand("op", "设为 OP");
	}

	private void BtnPlayerDeOp_Click(object sender, MouseButtonEventArgs e)
	{
		SendPlayerCommand("deop", "取消 OP");
	}

	// ================= 崩溃报告 =================

	private void BtnServerCrash_Click(object sender, MouseButtonEventArgs e)
	{
		ServerInstance current = Current;
		if (current == null)
		{
			ModMain.MyMsgBox("请先选择一个服务器实例。", "提示");
			return;
		}
		try
		{
			string text = ModServer.FindLatestCrash(current);
			if (string.IsNullOrEmpty(text))
			{
				ModMain.MyMsgBox("这个实例还没有崩溃报告，也没有 latest.log。\n\n先启动一次服务器再回来看看。", "没有日志");
				return;
			}
			AppendLine("[PCL] 打开日志：" + text, Brushes.Gray);
			Process.Start("notepad.exe", "\"" + text + "\"");
		}
		catch (Exception ex)
		{
			ModMain.MyMsgBox(ex.Message, "无法打开崩溃报告", IsWarn: true);
		}
	}

}