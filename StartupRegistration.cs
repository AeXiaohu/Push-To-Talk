using System;
using System.IO;
using Microsoft.Win32;

namespace PushToTalk
{
    // This registry entry is the source of truth: loading settings never enables startup.
    public sealed class StartupRegistration
    {
        internal const string RegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal const string RegistryValueName = "Push To Talk";
        internal const string LauncherFileName = "Push To Talk Startup.exe";
        private readonly IStartupRegistrationStore store;
        private readonly Func<string, bool> fileExists;

        public StartupRegistration() : this(new CurrentUserStartupRegistrationStore(), File.Exists) { }

        internal StartupRegistration(IStartupRegistrationStore store, Func<string, bool> fileExists)
        {
            if (store == null) throw new ArgumentNullException("store");
            if (fileExists == null) throw new ArgumentNullException("fileExists");
            this.store = store;
            this.fileExists = fileExists;
        }

        public bool IsEnabled(string executablePath)
        {
            string commandLine = BuildCommandLine(executablePath);
            string registeredCommand = store.Read() as string;
            return String.Equals(commandLine, registeredCommand, StringComparison.OrdinalIgnoreCase)
                && fileExists(GetLauncherPath(executablePath));
        }

        // Upgrade only this installation's already-enabled 1.0 entry. A fresh or
        // unrelated registration must never turn startup on merely by opening the app.
        public bool MigrateLegacyRegistration(string executablePath)
        {
            string legacyCommand = "\"" + NormalizeExecutablePath(executablePath) + "\" --startup";
            if (!String.Equals(store.Read() as string, legacyCommand, StringComparison.OrdinalIgnoreCase)) return false;
            SetEnabled(executablePath, true);
            return true;
        }

        // Called for a checkbox action or migration of an already-enabled entry.
        // Errors propagate so the UI never pretends that registration succeeded.
        public void SetEnabled(string executablePath, bool enabled)
        {
            if (enabled)
            {
                if (!String.Equals(Path.GetFileName(executablePath), "Push To Talk.exe", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(Localization.Text(
                        "启用开机自启动时，请保持主程序文件名为 Push To Talk.exe。",
                        "Keep the main application named Push To Talk.exe to enable Windows startup."));
                string command = BuildCommandLine(executablePath);
                string launcher = GetLauncherPath(executablePath);
                if (!fileExists(launcher))
                    throw new FileNotFoundException(Localization.Text(
                        "缺少开机启动器。请将 Push To Talk Startup.exe 与主程序放在同一文件夹。",
                        "Startup launcher is missing. Keep Push To Talk Startup.exe beside the main application."), launcher);
                store.Write(command);
            }
            else store.Delete();
        }

        internal static string BuildCommandLine(string executablePath)
        {
            string commandLine = "\"" + GetLauncherPath(executablePath) + "\"";
            // The Windows Run key supports command lines of at most 260 characters.
            if (commandLine.Length > 260)
                throw new PathTooLongException(Localization.Text("程序路径太长，无法设置开机自启动。请将程序移到较短的路径后重试。", "The application path is too long for startup. Move the application to a shorter path and try again."));
            return commandLine;
        }

        internal static string GetLauncherPath(string executablePath)
        {
            return Path.Combine(Path.GetDirectoryName(NormalizeExecutablePath(executablePath)), LauncherFileName);
        }

        private static string NormalizeExecutablePath(string executablePath)
        {
            if (String.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException(Localization.Text("程序路径不能为空。", "The application path cannot be empty."), "executablePath");
            return Path.GetFullPath(executablePath);
        }

        internal static bool IsStartupLaunch(string[] args)
        {
            if (args != null)
                foreach (string argument in args)
                    if (String.Equals(argument, "--startup", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    internal interface IStartupRegistrationStore
    {
        object Read();
        void Write(string commandLine);
        void Delete();
    }

    internal sealed class CurrentUserStartupRegistrationStore : IStartupRegistrationStore
    {
        public object Read()
        {
            // Opening the app must not create a Run key or register startup.
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RegistryPath, false))
                return key == null ? null : key.GetValue(StartupRegistration.RegistryValueName, null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames);
        }

        public void Write(string commandLine)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(StartupRegistration.RegistryPath))
            {
                if (key == null) throw new IOException(Localization.Text("无法打开当前用户的开机启动设置。", "Unable to open startup settings for the current user."));
                key.SetValue(StartupRegistration.RegistryValueName, commandLine, RegistryValueKind.String);
            }
        }

        public void Delete()
        {
            // A missing key/value is already disabled. Never delete the shared Run key.
            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(StartupRegistration.RegistryPath, true))
                if (key != null) key.DeleteValue(StartupRegistration.RegistryValueName, false);
        }
    }
}
