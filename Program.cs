using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Push To Talk")]
[assembly: AssemblyProduct("Push To Talk")]
[assembly: AssemblyDescription("Lightweight microphone control with multiple hotkeys and optional sound feedback")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace PushToTalk
{
    internal static class Program
    {
        [DllImport("user32.dll")] private static extern bool SetProcessDPIAware();

        [STAThread]
        private static void Main(string[] args)
        {
            string settingsError;
            AppSettings launchSettings = AppSettings.Load(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.xml"), out settingsError);
            Localization.SetLanguage(launchSettings.Language);
            bool startupLaunch = StartupRegistration.IsStartupLaunch(args);
            bool created;
            // Keep the original mutex identity so old and renamed builds cannot both control the mic.
            using (Mutex mutex = new Mutex(true, "Local\\PushToTalkLite.SingleInstance.6EB72C80", out created))
            {
                if (!created)
                {
                    if (!startupLaunch) MessageBox.Show(Localization.Text("Push To Talk 已在运行，请在系统托盘中打开。", "Push To Talk is already running. Open it from the system tray."), "Push To Talk");
                    return;
                }
                SetProcessDPIAware();
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                MainForm form = null;
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    if (form != null) form.StopForError(Localization.Text("发生错误，控制已停止。", "An error occurred and control has stopped. ") + e.Exception.Message);
                };
                try { form = new MainForm(startupLaunch); Application.Run(form); }
                finally { if (form != null) form.Dispose(); mutex.ReleaseMutex(); }
            }
        }
    }
}
