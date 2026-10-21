using System;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Push To Talk")]
[assembly: AssemblyProduct("Push To Talk")]
[assembly: AssemblyDescription("Lightweight microphone control with multiple hotkeys and optional sound feedback")]
[assembly: AssemblyVersion("1.3.0.0")]
[assembly: AssemblyFileVersion("1.3.0.0")]

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
            if (!IsAdministrator())
            {
                if (SelfElevation.WasElevationRequested(args))
                {
                    MessageBox.Show(Localization.Text(
                        "未能获得管理员权限，Push To Talk 已停止启动。请检查 Windows 的程序兼容性设置。",
                        "Administrator permission was not granted. Push To Talk has stopped starting. Check the app's Windows compatibility settings."),
                        "Push To Talk", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return;
                }
                try
                {
                    SelfElevation.Launch(Application.ExecutablePath, startupLaunch, System.IO.File.Exists,
                        delegate(ProcessStartInfo info) { using (Process process = Process.Start(info)) { } });
                }
                catch (Exception error)
                {
                    MessageBox.Show(Localization.Text("无法以管理员身份启动 Push To Talk：", "Unable to start Push To Talk as administrator: ") + error.Message,
                        "Push To Talk", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }
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

        internal static bool IsAdministrator()
        {
            try
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    return identity != null && new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            catch (System.Security.SecurityException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
