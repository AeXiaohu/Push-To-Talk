using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace PushToTalk
{
    public sealed class AboutDialog : Form
    {
        private readonly Font uiFont = new Font(Localization.IsChinese ? "Microsoft YaHei UI" : "Segoe UI", 9F);
        private readonly Font titleFont = new Font("Segoe UI", 21F, FontStyle.Bold);
        private readonly Icon appIcon = AppIcon.Load();

        public AboutDialog()
        {
            Text = Localization.Text("关于 Push To Talk", "About Push To Talk");
            Icon = appIcon;
            Font = uiFont;
            BackColor = DarkTheme.Background;
            ForeColor = DarkTheme.Text;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(430, 252);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            DarkTheme.ApplyTitleBar(this);

            Label title = new Label { Text = "Push To Talk", Font = titleFont,
                Location = new Point(24, 23), Size = new Size(380, 44) };
            Label version = new Label { Text = Localization.Text("版本 1.1", "Version 1.1"), ForeColor = DarkTheme.Subtle,
                Location = new Point(27, 76), Size = new Size(378, 24) };
            Label description = new Label { Text = Localization.Text("多个按键，一个麦克风。", "Multiple keys. One microphone."),
                Location = new Point(27, 105), Size = new Size(378, 25) };
            Label author = new Label { Text = Localization.Text("作者", "Author"), ForeColor = DarkTheme.Subtle,
                Location = new Point(27, 143), Size = new Size(42, 25) };
            LinkLabel link = new LinkLabel { Text = "AeXiaohu ↗", AutoSize = true,
                Location = new Point(73, 143), LinkColor = DarkTheme.Accent,
                ActiveLinkColor = DarkTheme.Text, VisitedLinkColor = DarkTheme.Accent,
                LinkBehavior = LinkBehavior.HoverUnderline,
                AccessibleName = Localization.Text("作者 AeXiaohu 的 GitHub 主页", "Author AeXiaohu's GitHub profile"),
                AccessibleDescription = "https://github.com/AeXiaohu" };
            Label error = new Label { ForeColor = DarkTheme.Subtle,
                Location = new Point(27, 173), Size = new Size(378, 25), Visible = false };
            link.LinkClicked += delegate
            {
                try
                {
                    Process.Start(new ProcessStartInfo("https://github.com/AeXiaohu") { UseShellExecute = true });
                }
                catch (Win32Exception) { error.Text = Localization.Text("无法打开浏览器，请访问 github.com/AeXiaohu", "Can't open browser. Visit github.com/AeXiaohu"); error.Visible = true; }
                catch (InvalidOperationException) { error.Text = Localization.Text("无法打开浏览器，请访问 github.com/AeXiaohu", "Can't open browser. Visit github.com/AeXiaohu"); error.Visible = true; }
            };
            Button close = new Button { Text = Localization.Text("关闭", "Close"), DialogResult = DialogResult.Cancel,
                Location = new Point(302, 207), Size = new Size(102, 31) };
            DarkTheme.StyleButton(close, false);
            AcceptButton = close;
            CancelButton = close;
            Controls.AddRange(new Control[] { title, version, description, author, link, error, close });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing)
            {
                titleFont.Dispose();
                uiFont.Dispose();
                appIcon.Dispose();
            }
        }
    }
}
