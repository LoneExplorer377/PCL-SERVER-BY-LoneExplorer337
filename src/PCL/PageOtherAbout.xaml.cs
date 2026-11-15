using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;
using Microsoft.VisualBasic.CompilerServices;

namespace PCL;

[DesignerGenerated]
public partial class PageOtherAbout : MyPageRight, IComponentConnector
{
	private new bool IsLoaded;

	[CompilerGenerated]
	[AccessedThroughProperty("PanBack")]
	private MyScrollViewer _PanBack;

	[CompilerGenerated]
	[AccessedThroughProperty("PanMain")]
	private StackPanel _PanMain;

	[CompilerGenerated]
	[AccessedThroughProperty("ItemAboutPcl")]
	private MyListItem _ItemAboutPcl;

	private void Hook_PanBack(MyScrollViewer value)
	{
		_PanBack = value;
		
	}


	private void Hook_PanMain(StackPanel value)
	{
		_PanMain = value;
		
	}


	private void Hook_ItemAboutPcl(MyListItem value)
	{
		_ItemAboutPcl = value;
		
	}


	public PageOtherAbout()
	{
		Loaded += PageOtherAbout_Loaded;
		IsLoaded = false;
		InitializeComponent();
			Hook_PanBack(PanBack);
			Hook_PanMain(PanMain);
			Hook_ItemAboutPcl(ItemAboutPcl);
	}

	private void PageOtherAbout_Loaded(object sender, RoutedEventArgs e)
	{
		PanBack.ScrollToHome();
		if (!IsLoaded)
		{
			IsLoaded = true;
			ItemAboutPcl.Info = ItemAboutPcl.Info.Replace("%VERSION%", ModBase.VersionDisplay).Replace("%VERSIONCODE%", Conversions.ToString(ModBase.VersionCode));
		}
	}

	public static void CopyIdentify()
	{
		ModBase.ClipboardSet(ModBase.Identify);
	}

}
