using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PushToTalk
{
    internal static class AppIcon
    {
        // The caller owns the returned icon; it remains valid after the temporary icon is disposed.
        public static Icon Load()
        {
            try
            {
                using (Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath))
                {
                    if (icon != null) return (Icon)icon.Clone();
                }
            }
            catch (ArgumentException) { }
            catch (IOException) { }
            catch (ExternalException) { }
            return (Icon)SystemIcons.Application.Clone();
        }
    }
}
