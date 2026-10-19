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
        private readonly IStartupRegistrationStore store;

        public StartupRegistration() : this(new CurrentUserStartupRegistrationStore()) { }

        internal StartupRegistration(IStartupRegistrationStore store)
        {
            if (store == null) throw new ArgumentNullException("store");
            this.store = store;
        }

        public bool IsEnabled(string executablePath)
        {
            string commandLine = BuildCommandLine(executablePath);
            string registeredCommand = store.Read() as string;
            return String.Equals(commandLine, registeredCommand, StringComparison.OrdinalIgnoreCase);
        }

        // Only call in direct response to the user's startup checkbox action.
        // Registry errors intentionally propagate so the UI can show the error and revert.
        public void SetEnabled(string executablePath, bool enabled)
        {
            if (enabled) store.Write(BuildCommandLine(executablePath));
            else store.Delete();
        }

        internal static string BuildCommandLine(string executablePath)
        {
            if (String.IsNullOrWhiteSpace(executablePath))
                throw new ArgumentException(Localization.Text("程序路径不能为空。", "The application path cannot be empty."), "executablePath");
            string fullPath = Path.GetFullPath(executablePath);
            string commandLine = "\"" + fullPath + "\" --startup";
            // The Windows Run key supports command lines of at most 260 characters.
            if (commandLine.Length > 260)
                throw new PathTooLongException(Localization.Text("程序路径太长，无法设置开机自启动。请将程序移到较短的路径后重试。", "The application path is too long for startup. Move the application to a shorter path and try again."));
            return commandLine;
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
