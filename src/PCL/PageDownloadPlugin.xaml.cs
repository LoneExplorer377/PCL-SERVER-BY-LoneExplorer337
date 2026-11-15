using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows.Markup;
using MeloongCore;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL;

[DesignerGenerated]
public partial class PageDownloadPlugin : MyPageRight, IComponentConnector
{
	[CompilerGenerated]
	[AccessedThroughProperty("Content")]
	private PageResource _Content;

	private void Hook_Content(PageResource value)
	{
		_Content = value;
		
	}


	public PageDownloadPlugin()
	{
		InitializeComponent();
			Hook_Content(Content);
		// [MOD] 插件页：只走 Modrinth（CurseForge 没有可靠的插件分类），并把目标版本对齐当前选中的服务端实例
		try
		{
			foreach (object item in Content.ComboSearchSource.Items)
			{
				MyComboBoxItem comboItem = item as MyComboBoxItem;
				if (comboItem != null && comboItem.Tag != null && comboItem.Tag.ToString() == "2")
				{
					Content.ComboSearchSource.SelectedItem = comboItem;
					break;
				}
			}
			Content.ComboSearchSource.IsEnabled = false;
			PageEnter += ApplyPluginTarget;
			ApplyPluginTarget();
		}
		catch (Exception ex)
		{
			Logger.Warn(ex, "初始化插件下载页面失败", (LogBehavior)0, "PageDownloadPlugin.xaml.cs");
		}
	}

	private void ApplyPluginTarget()
	{
		try
		{
			ServerInstance server = ModServer.SelectedInstance;
			if (server != null)
			{
				if (string.IsNullOrEmpty(Content.TextSearchVersion.Text))
				{
					Content.TextSearchVersion.Text = server.McVersion;
				}
				ModMain.Hint("插件将安装到服务端实例「" + server.Name + "」的 plugins 文件夹", ModMain.HintType.Blue, Log: false);
			}
			else
			{
				ModMain.Hint("插件只能装进服务端：请先在版本选择里选中一个服务端实例", ModMain.HintType.Red, Log: false);
			}
		}
		catch (Exception ex)
		{
			Logger.Warn(ex, "设置插件下载目标失败", (LogBehavior)0, "PageDownloadPlugin.xaml.cs");
		}
	}

}
