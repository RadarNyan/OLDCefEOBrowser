using System;
using System.Windows.Forms;

namespace EOBrowser
{
	static class Program
	{
		/// <summary>
		/// The main entry point for the application.
		/// </summary>
		[STAThread]
		static void Main(string[] args)
		{
            if ( args.Length == 0 )
            {
#if !DEBUG
                MessageBox.Show( "此浏览器无法单独启动，请直接运行本体程序。", "消息", MessageBoxButtons.OK, MessageBoxIcon.Information );
				Application.Exit();
#else
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);
				Application.Run(new FormBrowser("DEBUG"));
#endif
            } else {
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);
				Application.Run(new FormBrowser(args[0]));
			}
		}
	}
}
