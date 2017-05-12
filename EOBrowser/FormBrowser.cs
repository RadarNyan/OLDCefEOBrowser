using System;
using System.Windows.Forms;
using CefSharp;
using CefSharp.WinForms;
using System.IO;
using System.Runtime.InteropServices;
using System.ServiceModel;
using BrowserLib;
using Codeplex.Data;
using System.Drawing;
using System.Drawing.Imaging;
using System.Text.RegularExpressions;

namespace EOBrowser
{
	[ServiceBehavior(InstanceContextMode = InstanceContextMode.Single)]
	public partial class FormBrowser : Form, BrowserLib.IBrowser
	{
		private void ReadConfigFile()
		{
			string config_file = Path.Combine(cef_path, @"config.json");
			string config_string;
			if (File.Exists(config_file)) {
				config_string = File.ReadAllText(config_file);
			} else {
				#region Default Config

config_string =
@"{
  ""listen_port"":0,
  ""local_cache"":{
    ""enabled"":false
  },
  ""hacks"":{
    ""change_cookie_region"":false
  }
}";

				#endregion
				File.WriteAllText(config_file, config_string);
			}
			var config = DynamicJson.Parse(config_string);
			twp_listen_port = Convert.ToInt32(config.listen_port); // was double
		}

		#region CEF-Sharp
		private ChromiumWebBrowser Browser;

		private string cef_path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"CefEOBrowser");

		private bool Cef_started = false;

		private void InitializeChromium(string proxy, string url)
		{
			CefLibraryHandle libraryLoader = new CefLibraryHandle(Path.Combine(cef_path, @"bin\libcef.dll"));
			CefSettings settings = new CefSettings();
			settings.CachePath = Path.Combine(cef_path, @"cache");
			settings.UserDataPath = Path.Combine(cef_path, @"userdata");
			settings.ResourcesDirPath = Path.Combine(cef_path, @"bin");
			settings.LocalesDirPath = Path.Combine(cef_path, @"bin\locales");
			settings.BrowserSubprocessPath = Path.Combine(cef_path, @"bin\CefSharp.BrowserSubprocess.exe");
			settings.CefCommandLineArgs.Add("proxy-server", proxy);
			//settings.UserAgent = "Mozilla/5.0 (Windows NT 6.1; WOW64; Trident/7.0; rv:11.0) like Gecko"; // Fake UA as IE11 on Windows 7
			settings.LogSeverity = LogSeverity.Disable;
			Cef.Initialize(settings);
			//chromeBrowser = new ChromiumWebBrowser("chrome://view-http-cache/");
			//Browser = new ChromiumWebBrowser("http://www.whoishostingthis.com/tools/user-agent/");
			//Browser = new ChromiumWebBrowser(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"CefEOBrowser\html\default.htm"));
			Browser = new ChromiumWebBrowser(url);
			Cef_started = true;
			//chromeBrowser.RequestHandler = new RequestHandler();
			this.SizeAdjuster.Controls.Add(Browser);
			Browser.Dock = DockStyle.Fill;
			libraryLoader.Dispose();
		}
		#endregion

		#region Titanium-Web-Proxy
		private static readonly ProxyController TWP = new ProxyController();
		private int twp_listen_port;
		#endregion

		public void SetProxy(string proxy)
		{
			int    proxy_port_74eo = 0;
			string proxy_cef;

			ushort port;
			if (ushort.TryParse(proxy, out port)) { // no upstream proxy (use system proxy)
				proxy_port_74eo = port;
			} else {
				string pattern = @"(http|https)=(.*?):(\d*)";
				MatchCollection matches = Regex.Matches(proxy, pattern);

				switch (matches.Count)
				{
					case 1: // http only, no upstream proxy.
						// MessageBox.Show(string.Format("http only, host={0} port={1}",
						// 	matches[0].Groups[2].Value, matches[0].Groups[3].Value));
						proxy_port_74eo = Int32.Parse(matches[0].Groups[3].Value);
						proxy = "";
						break;
					case 2: // http and https, upstream proxy (https) found.
						// MessageBox.Show(string.Format("http, host={0} port={1}\r\nhttps, host={2} port={3}", 
						// 	matches[0].Groups[2].Value, matches[0].Groups[3].Value,
						// 	matches[1].Groups[2].Value, matches[1].Groups[3].Value));
						proxy_port_74eo = Int32.Parse(matches[0].Groups[3].Value);
						proxy = ";https=" + matches[1].Groups[2].Value + ":" + Int32.Parse(matches[1].Groups[3].Value);
						break;
					default: // WTF
						MessageBox.Show(matches.Count.ToString());
						Exit();
						break;
				}
					#region R.I.P String.Split
	/*
					string[] separators = { "http=", ":", ";https=" };
					string[] substrings = proxy.Split(separators, StringSplitOptions.RemoveEmptyEntries);

					switch (substrings.Length)
					{
						case 2: // http only
							MessageBox.Show(string.Format("http only, host={0} port={1}", substrings[0], substrings[1]));
							break;
						case 4: // http and https
							MessageBox.Show(string.Format("http, host={0} port={1}\r\nhttps, host={2} port={3}", substrings[0], substrings[1], substrings[2], substrings[3]));
							break;
						default: // WTF
							MessageBox.Show(substrings.Length.ToString());
							break;
					}
	*/
					#endregion
			}

			// Auto choose a port for TWP if not set in config.json
			if (twp_listen_port == 0)
				twp_listen_port = proxy_port_74eo + 9;

			if (TWP.started) {
				// TODO: change settings on-the-fly
			} else {
				// Start Titanium-Web-Proxy
				TWP.StartProxy(twp_listen_port, "localhost", proxy_port_74eo);
			}

			// Build proxy string for Cef Browser (http to TWP, https to upstream)
			proxy_cef = "http=127.0.0.1:" + twp_listen_port + proxy;
			// AddLog(2, string.Format("Cef => TWP@{0} => 74eo@{1}{2}", twp_listen_port, proxy_port_74eo, proxy));

			if (Cef_started) {
				// TODO: change settings on-the-fly
			} else {
				// Start Cef Browser
				if (Configuration.IsEnabled) {
					InitializeChromium(proxy_cef, Configuration.LogInPageURL);
				} else {
					InitializeChromium(proxy_cef, Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"CefEOBrowser\html\default.htm"));
				}
			}
		}

		// KanColleSize (Game-Area, 1x zoom)
		private readonly Size KanColleSize = new Size( 800, 480 );

		// BrowserSize ( 最小化时设置浏览器尺寸为 0 以释放 CPU 资源，用此变量储存原尺寸 )
		private Size BrowserSize;

		// FormBrowserHostの通信サーバ
		private string ServerUri;

		// FormBrowserの通信サーバ
		private PipeCommunicator<BrowserLib.IBrowserHost> BrowserHost;

		private BrowserLib.BrowserConfiguration Configuration;

		// 親プロセスが生きているか定期的に確認するためのタイマー
		private Timer HeartbeatTimer = new Timer();
		private IntPtr HostWindow;

		public FormBrowser(string serverUri) // serverUri: ホストプロセスとの通信用URL
		{
			ServerUri = serverUri;
			ReadConfigFile();
			InitializeComponent();
			this.ToolMenu.Renderer = new ToolStripOverride(); // remove stupid rounded corner

			_volumeManager = new VolumeManager( (uint)System.Diagnostics.Process.GetCurrentProcess().Id );
			// 音量設定用コントロールの追加
			{
				var control = new NumericUpDown();
				control.Name = "ToolMenu_Other_Volume_VolumeControl";
				control.Maximum = 100;
				control.TextAlign = HorizontalAlignment.Right;
				control.Font = ToolMenu_Other_Volume.Font;

				control.ValueChanged += ToolMenu_Other_Volume_ValueChanged;
				control.Tag = false;

				var host = new ToolStripControlHost( control, "ToolMenu_Other_Volume_VolumeControlHost" );

				control.Size = new Size( host.Width - control.Margin.Horizontal, host.Height - control.Margin.Vertical );
				control.Location = new Point( control.Margin.Left, control.Margin.Top );


				ToolMenu_Other_Volume.DropDownItems.Add( host );
			}
#if DEBUG
			InitializeChromium("", "");
#endif
		}

		private void FormBrowser_FormClosing(object sender, FormClosingEventArgs e)
		{
			Cef.Shutdown();
			TWP.Stop();
		}

		[DllImport("user32.dll", EntryPoint = "GetWindowLongA", SetLastError = true)]
		private static extern uint GetWindowLong(IntPtr hwnd, int nIndex);

		[DllImport("user32.dll", EntryPoint = "SetWindowLongA", SetLastError = true)]
		private static extern uint SetWindowLong(IntPtr hwnd, int nIndex, uint dwNewLong);

		private void FormBrowser_Load(object sender, EventArgs e)
		{
			SetWindowLong(this.Handle, (-16), 0x40000000); // GWL_STYLE = (-16), WS_CHILD = 0x40000000

			// ホストプロセスに接続
			BrowserHost = new PipeCommunicator<BrowserLib.IBrowserHost>(
				this, typeof(BrowserLib.IBrowser), ServerUri + "Browser", "Browser");
			BrowserHost.Connect(ServerUri + "/BrowserHost");
			BrowserHost.Faulted += BrowserHostChannel_Faulted;

			ConfigurationChanged(BrowserHost.Proxy.Configuration);

			// ウィンドウの親子設定＆ホストプロセスから接続してもらう
			BrowserHost.Proxy.ConnectToBrowser(this.Handle);

			// 親ウィンドウが生きているか確認 
			HeartbeatTimer.Tick += (EventHandler)((sender2, e2) => {
				BrowserHost.AsyncRemoteRun(() => { HostWindow = BrowserHost.Proxy.HWND; });
			});
			HeartbeatTimer.Interval = 2000; // 2秒ごと　
			HeartbeatTimer.Start();

			BrowserHost.AsyncRemoteRun(() => BrowserHost.Proxy.GetIconResource());
		}

		private void Exit()
		{
			if (!BrowserHost.Closed)
			{
				Cef.Shutdown();
				TWP.Stop();
				BrowserHost.Close();
				HeartbeatTimer.Stop();
				Application.Exit();
			}
		}

		private void BrowserHostChannel_Faulted(Exception e)
		{
			Exit();
		}

		public void CloseBrowser()
		{
			HeartbeatTimer.Stop();
			// リモートコールでClose()呼ぶのばヤバそうなので非同期にしておく
			BeginInvoke((Action)(() => Exit()));
		}

		public void ConfigurationChanged(BrowserLib.BrowserConfiguration conf)
		{
			Configuration = conf;

			SizeAdjuster.AutoScroll = Configuration.IsScrollable;
			ToolMenu_Other_Zoom_Fit.Checked = Configuration.ZoomFit;
			ApplyZoom();
			// ToolMenu_Other_AppliesStyleSheet.Checked = Configuration.AppliesStyleSheet;
			ToolMenu.Dock = (DockStyle)Configuration.ToolMenuDockStyle;
			ToolMenu.Visible = Configuration.IsToolMenuVisible;

			this.SizeAdjuster.BackColor = System.Drawing.Color.FromArgb(unchecked((int)Configuration.BackColor));
			this.ToolMenu.BackColor = System.Drawing.Color.FromArgb(unchecked((int)Configuration.BackColor));
		}

		// 艦これが読み込まれているかどうか
		private bool IsKanColleLoaded { get; set; }

		public void InitialAPIReceived()
		{
			IsKanColleLoaded = true;

			//ロード直後の適用ではレイアウトがなぜか崩れるのでこのタイミングでも適用
			ApplyStyleSheet();
			ApplyZoom();

			//起動直後はまだ音声が鳴っていないのでミュートできないため、この時点で有効化
			SetVolumeState();
		}

		// folderPath: 保存するフォルダへのパス
		// screenShotFormat: スクリーンショットのフォーマット。1=jpg, 2=png
		public void SaveScreenShot( string folderPath, int screenShotFormat )
		{
			if (!Directory.Exists(folderPath))
			{
				Directory.CreateDirectory(folderPath);
			}

			string ext;
			System.Drawing.Imaging.ImageFormat format;

			switch ( screenShotFormat ) {
				case 1:
					ext = "jpg";
					format = System.Drawing.Imaging.ImageFormat.Jpeg;
					break;
				case 2:
				default:
					ext = "png";
					format = System.Drawing.Imaging.ImageFormat.Png;
					break;
			}


			SaveScreenShot( string.Format(
				"{0}\\{1:yyyyMMdd_HHmmssff}.{2}",
				folderPath,
				DateTime.Now,
				ext ), format );
		}

		private void SaveScreenShot(string path, System.Drawing.Imaging.ImageFormat format)
		{
			var wb = Browser;
			if (!IsKanColleLoaded)
			{
				// AddLog(3, "", "因为", "『艦これ』", "还没有载入，无法截图。");
				System.Media.SystemSounds.Beep.Play();
				return;
			}
			try
			{
				// fuck it, I'll do it on my own.
			}
			catch (Exception ex)
			{
				BrowserHost.AsyncRemoteRun(() =>
				   BrowserHost.Proxy.SendErrorReport(ex.ToString(), "スクリーンショットの保存時にエラーが発生しました。"));
				System.Media.SystemSounds.Beep.Play();
			}
		}

		public void RefreshBrowser()
		{
			Browser.Reload();
		}

		private void ToolMenu_Refresh_Click(object sender, EventArgs e)
		{
			ToolMenu_Other_Refresh_Click(sender, e);
		}

		private void ToolMenu_Other_Refresh_Click(object sender, EventArgs e)
		{
			if (!Configuration.ConfirmAtRefresh ||
				MessageBox.Show("即将刷新浏览器。\r\n确认刷新吗？", "要求确认",
					MessageBoxButtons.OKCancel, MessageBoxIcon.Exclamation, MessageBoxDefaultButton.Button2) ==
				DialogResult.OK)
			{
				RefreshBrowser();
			}
		}

		public void Navigate(string url)
		{
			Browser.Load(url);
		}

		private void ToolMenu_NavigateToLogInPage_Click(object sender, EventArgs e)
		{
			ToolMenu_Other_NavigateToLogInPage_Click(sender, e);
		}

		private void ToolMenu_Other_NavigateToLogInPage_Click(object sender, EventArgs e)
		{
			if (MessageBox.Show("即将转到登录页。\r\n确认跳转吗？", "要求确认",
					MessageBoxButtons.OKCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) ==
				DialogResult.OK)
			{
				Navigate(Configuration.LogInPageURL);
			}
		}

		private void CenteringBrowser() {
			if (SizeAdjuster.Width == 0 || SizeAdjuster.Height == 0)
				return;
			SizeAdjuster.SuspendLayout();
			if (BrowserSize.Width != 0 && BrowserSize.Height != 0)
				Browser.Size = BrowserSize;
			int x = Browser.Location.X, y = Browser.Location.Y;
			bool isScrollable = Configuration.IsScrollable;
			Browser.Dock = DockStyle.None;
			if ( !isScrollable || Browser.Width <= SizeAdjuster.Width ) {
				x = ( SizeAdjuster.Width - Browser.Width ) / 2;
			}
			if ( !isScrollable || Browser.Height <= SizeAdjuster.Height ) {
				y = ( SizeAdjuster.Height - Browser.Height ) / 2;
			}
			Browser.Anchor = AnchorStyles.None;
			Browser.Location = new Point( x, y );
			SizeAdjuster.ResumeLayout();
		}

		public void ApplyZoom()
		{
			int zoomRate = Configuration.ZoomRate;
			bool fit = Configuration.ZoomFit && StyleSheetApplied;

			try
			{
				double zoomFactor;

				if (fit)
				{
					double rateX = (double)SizeAdjuster.Width / KanColleSize.Width;
					double rateY = (double)SizeAdjuster.Height / KanColleSize.Height;
					zoomFactor = Math.Min(rateX, rateY);
				}
				else
				{
					if (zoomRate < 10)
						zoomRate = 10;
					if (zoomRate > 1000)
						zoomRate = 1000;

					zoomFactor = zoomRate / 100.0;
				}

				if (StyleSheetApplied)
				{
					// Browser.Size = Browser.MinimumSize = new Size(
					// Setting MinimumSize would cause CPU usage to be high, why?
					BrowserSize = new Size(
						(int)(KanColleSize.Width * zoomFactor),
						(int)(KanColleSize.Height * zoomFactor)
						);
					CenteringBrowser(); // this also causes high cpu usage
				}

				if (fit)
				{
					ToolMenu_Other_Zoom_Current.Text = string.Format("当前 : 自适应");
				}
				else
				{
					ToolMenu_Other_Zoom_Current.Text = string.Format("当前: {0}%", zoomRate);
				}


			}
			catch (Exception ex)
			{
				AddLog(3, "", "调整缩放比例失败。" + ex.Message);
			}
		}

		private bool StyleSheetApplied;
		#region JavaScripts (Apply & Restore)

private readonly string Page_JS =
@"(function () {
var node = document.getElementById('da1733f9ca1d');
if (node) document.head.removeChild(node);
node = document.createElement('style');
node.id = 'eobrowser_stylish';
node.innerHTML = 'body { visibility: hidden; overflow: hidden; } \
div #block_background { visibility: visible; } \
div #alert { visibility: visible; overflow: scroll; top: 0 !important; left: 3% !important; width: 90% !important; height: 100%; padding:2%;} \
div.dmm-ntgnavi { display: none; } \
#area-game { position: fixed; left: 0; top: 0; width: 100%; height: 100%; } \
#game_frame { visibility: visible; width: 100%; height: 100%; }';
document.head.appendChild(node);
})();";

private readonly string Frame_JS =
@"(function () {
var node = document.getElementById('da1733f9ca1d');
if (node) document.head.removeChild(node);
node = document.createElement('style');
node.id = 'eobrowser_stylish';
node.innerHTML = 'body { visibility: hidden; } \
#flashWrap { position: fixed; left: 0; top: 0; width: 100%; height: 100%; } \
#externalswf { visibility: visible; width: 100%; height: 100%; }';
document.head.appendChild(node);
})();";

private readonly string Restore_JS =
@"(function () {
var node = document.getElementById('da1733f9ca1d');
if (node) document.head.removeChild(node);
})();";

		#endregion

		public void ApplyStyleSheet()
		{
			if (!StyleSheetApplied && !Configuration.AppliesStyleSheet)
				return;

			try
			{
				if (StyleSheetApplied)
				{
					var browser = Browser.GetBrowser();
					bool has_game_frame = false;
					foreach (var i in browser.GetFrameIdentifiers())
					{
						IFrame frame = browser.GetFrame(i);
						if (frame.Name == "game_frame")
						{
							has_game_frame = true;
							frame.ExecuteJavaScriptAsync(Restore_JS);
							break;
						}
					}
					if (has_game_frame)
					{
						browser.MainFrame.ExecuteJavaScriptAsync(Restore_JS);
						StyleSheetApplied = false;
					}
				}
				else if (!StyleSheetApplied && Configuration.AppliesStyleSheet)
				{
					var browser = Browser.GetBrowser();
					bool has_game_frame = false;
					foreach (var i in browser.GetFrameIdentifiers())
					{
						IFrame frame = browser.GetFrame(i);
						if (frame.Name == "game_frame")
						{
							has_game_frame = true;
							frame.ExecuteJavaScriptAsync(Frame_JS);
							break;
						}
					}
					if (has_game_frame)
					{
						browser.MainFrame.ExecuteJavaScriptAsync(Page_JS);
						StyleSheetApplied = true;
					}
				}

				// if ( Browser.Address.StartsWith( GAME_URL ) )
				// {

				ApplyZoom(); // something's not right, high cpu usage

			}
			catch (Exception ex)
			{

				BrowserHost.AsyncRemoteRun(() =>
				   BrowserHost.Proxy.SendErrorReport(ex.ToString(), "スタイルシートの適用に失敗しました。"));
			}
		}

		public void SetIconResource(byte[] canvas)
		{
			string[] keys = new string[] {
				"Browser_ScreenShot",
				"Browser_Zoom",
				"Browser_ZoomIn",
				"Browser_ZoomOut",
				"Browser_Unmute",
				"Browser_Mute",
				"Browser_Refresh",
				"Browser_Navigate",
				"Browser_Other",
			};
			int unitsize = 16 * 16 * 4;

			for (int i = 0; i < keys.Length; i++)
			{
				Bitmap bmp = new Bitmap( 16, 16, PixelFormat.Format32bppArgb );

				if (canvas != null)
				{
					BitmapData bmpdata = bmp.LockBits( new Rectangle( 0, 0, bmp.Width, bmp.Height ), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb );
					Marshal.Copy(canvas, unitsize * i, bmpdata.Scan0, unitsize);
					bmp.UnlockBits(bmpdata);
				}

				Icons.Images.Add(keys[i], bmp);
			}


			ToolMenu_ScreenShot.Image = ToolMenu_Other_ScreenShot.Image =
				Icons.Images["Browser_ScreenShot"];
			ToolMenu_Zoom.Image = ToolMenu_Other_Zoom.Image =
				Icons.Images["Browser_Zoom"];
			ToolMenu_Other_Zoom_Increment.Image =
				Icons.Images["Browser_ZoomIn"];
			ToolMenu_Other_Zoom_Decrement.Image =
				Icons.Images["Browser_ZoomOut"];
			ToolMenu_Refresh.Image = ToolMenu_Other_Refresh.Image =
				Icons.Images["Browser_Refresh"];
			ToolMenu_NavigateToLogInPage.Image = ToolMenu_Other_NavigateToLogInPage.Image =
				Icons.Images["Browser_Navigate"];
			ToolMenu_Other.Image =
				Icons.Images["Browser_Other"];

			SetVolumeState();
		}

		private VolumeManager _volumeManager;

		private NumericUpDown ToolMenu_Other_Volume_VolumeControl {
			get { return (NumericUpDown)( (ToolStripControlHost)ToolMenu_Other_Volume.DropDownItems["ToolMenu_Other_Volume_VolumeControlHost"] ).Control; }
		}

		private void SetVolumeState() {

			bool mute;
			float volume;

			try {
				mute = _volumeManager.IsMute;
				volume = _volumeManager.Volume * 100;

			} catch ( Exception ) {
				// 音量データ取得不能時
				mute = false;
				volume = 100;
			}

			ToolMenu_Mute.Image = ToolMenu_Other_Mute.Image =
				Icons.Images[mute ? "Browser_Mute" : "Browser_Unmute"];

			{
				var control = ToolMenu_Other_Volume_VolumeControl;
				control.Tag = false;
				control.Value = (decimal)volume;
				control.Tag = true;
			}

			Configuration.Volume = volume;
			Configuration.IsMute = mute;
			ConfigurationUpdated();
		}

		private void toolStripButton1_Click(object sender, EventArgs e)
		{
			Browser.ShowDevTools();
		}

		private void ToolMenu_Other_Navigate_Click(object sender, EventArgs e)
		{
			BrowserHost.AsyncRemoteRun(() => BrowserHost.Proxy.RequestNavigation(Browser.Address == null ? null : Browser.Address.ToString()));
		}

		private void ToolMenu_Other_AppliesStyleSheet_Click(object sender, EventArgs e)
		{
			Configuration.AppliesStyleSheet = ToolMenu_Other_AppliesStyleSheet.Checked;
			ApplyStyleSheet();
			ConfigurationUpdated();
		}

		private void ConfigurationUpdated()
		{
			BrowserHost.AsyncRemoteRun(() => BrowserHost.Proxy.ConfigurationUpdated(Configuration));
		}

		private void AddLog(int priority, string message, string msgchs1 = "", string msgjap2 = "", string msgchs2 = "", string msgjap3 = "", string msgchs3 = "")
		{
			BrowserHost.AsyncRemoteRun(() => BrowserHost.Proxy.AddLog(priority, message, msgchs1, msgjap2, msgchs2, msgjap3, msgchs3));
		}

		private void SizeAdjuster_SizeChanged(object sender, System.EventArgs e)
		{
			if (Browser != null) {
				var tempSize = new Size(Browser.Width, Browser.Height);
				if (SizeAdjuster.Width == 0 || SizeAdjuster.Height == 0) { // Minimized
					if (tempSize.Width != 0 && tempSize.Height != 0) {
						BrowserSize = tempSize;
					}
					Browser.Size = new Size(0, 0); // Reduce CPU usage
					return;
				}
				CenteringBrowser();
			}
		}

		public void DestroyDMMreloadDialog()
		{
			return;
		}

		private void ToolMenu_Mute_Click(object sender, EventArgs e)
		{
			try {
				_volumeManager.ToggleMute();

			} catch ( Exception ) {
				System.Media.SystemSounds.Beep.Play();
			}

			SetVolumeState();
		}

		void ToolMenu_Other_Volume_ValueChanged( object sender, EventArgs e ) {

			var control = ToolMenu_Other_Volume_VolumeControl;

			try {
				if ( (bool)control.Tag )
					_volumeManager.Volume = (float)( control.Value / 100 );
				control.BackColor = SystemColors.Window;

			} catch ( Exception ) {
				control.BackColor = Color.MistyRose;

			}

		}
	}

	public class ToolStripOverride : ToolStripProfessionalRenderer
	{
		public ToolStripOverride()
		{
			this.RoundedEdges = false;
		}
		protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
	}
}