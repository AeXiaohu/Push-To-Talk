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
        internal const string LegacyLauncherFileName = "Push To Talk Startup.exe";
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
                && fileExists(NormalizeExecutablePath(executablePath));
        }

        // Upgrade only this installation's already-enabled 1.1 companion entry.
        // The 1.0 direct command is already correct for the 1.2 self-elevating app.
        // A fresh or unrelated registration must never turn startup on merely by
        // opening the app.
        public bool MigrateLegacyRegistration(string executablePath)
        {
            string legacyCommand = "\"" + GetLegacyLauncherPath(executablePath) + "\"";
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
                string command = BuildCommandLine(executablePath);
                string executable = NormalizeExecutablePath(executablePath);
                if (!fileExists(executable))
                    throw new FileNotFoundException(Localization.Text(
                        "找不到主程序，无法设置开机自启动。",
                        "The application could not be found. Windows startup was not enabled."), executable);
                store.Write(command);
            }
            else store.Delete();
        }

        internal static string BuildCommandLine(string executablePath)
        {
            string commandLine = "\"" + NormalizeExecutablePath(executablePath) + "\" --startup";
            // The Windows Run key supports command lines of at most 260 characters.
            if (commandLine.Length > 260)
                throw new PathTooLongException(Localization.Text("程序路径太长，无法设置开机自启动。请将程序移到较短的路径后重试。", "The application path is too long for startup. Move the application to a shorter path and try again."));
            return commandLine;
        }

        internal static string GetLegacyLauncherPath(string executablePath)
        {
            return Path.Combine(Path.GetDirectoryName(NormalizeExecutablePath(executablePath)), LegacyLauncherFileName);
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
