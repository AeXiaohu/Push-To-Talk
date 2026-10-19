using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace PushToTalk
{
    public sealed class CaptureDialog : Form, IMessageFilter
    {
        private readonly Timer timer = new Timer();
        private readonly Label preview = new Label();
        private readonly Label hint = new Label();
        private HotkeyBinding candidate;
        private bool armed;
        private readonly HashSet<Keys> eventKeys = new HashSet<Keys>();
        public HotkeyBinding Binding { get; private set; }

        public CaptureDialog()
        {
            Text = Localization.Text("添加按键", "Add shortcut");
            Font = new Font(Localization.IsChinese ? "Microsoft YaHei UI" : "Segoe UI", 9F);
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(430, 235);
            BackColor = DarkTheme.Background;
            ForeColor = DarkTheme.Text;
            DarkTheme.ApplyTitleBar(this);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            KeyPreview = true;
            KeyDown += delegate(object sender, KeyEventArgs args) { args.Handled = true; args.SuppressKeyPress = true; };
            StartPosition = FormStartPosition.CenterParent;
            Label title = new Label { Text = Localization.Text("按下想绑定的按键，然后松开", "Press your shortcut, then release"), AutoSize = true,
                Font = new Font(Font.FontFamily, 12F, FontStyle.Bold), Location = new Point(22, 22) };
            preview.SetBounds(22, 62, 386, 60);
            preview.TextAlign = ContentAlignment.MiddleCenter;
            preview.BackColor = DarkTheme.MutedSurface;
            preview.ForeColor = DarkTheme.Accent;
            preview.Font = new Font(Font.FontFamily, 15F, FontStyle.Bold);
            preview.Text = Localization.Text("等待按键…", "Waiting for input…");
            hint.SetBounds(22, 134, 386, 44);
            hint.ForeColor = DarkTheme.Subtle;
            hint.Text = Localization.Text("支持键盘、鼠标中键和侧键，也可同时按组合键。\r\n每次添加一组；多个独立键请分别添加。",
                "Keyboard, middle/side mouse buttons, and key combinations.\r\nAdd each independent shortcut separately.");
            Button cancel = new Button { Text = Localization.Text("取消 (Esc)", "Cancel (Esc)"), DialogResult = DialogResult.Cancel,
                Location = new Point(300, 187), Size = new Size(108, 30) };
            DarkTheme.StyleButton(cancel, false);
            CancelButton = cancel;
            Controls.AddRange(new Control[] { title, preview, hint, cancel });
            timer.Interval = 20;
            timer.Tick += Poll;
            Shown += delegate { Application.AddMessageFilter(this); timer.Start(); };
            FormClosed += delegate { Application.RemoveMessageFilter(this); timer.Dispose(); };
        }

        // Local window messages make even very short keyboard taps capturable.
        // Mouse buttons and physical state still use polling; no global hook is installed.
        public bool PreFilterMessage(ref Message message)
        {
            if (Form.ActiveForm != this) return false;
            int kind = message.Msg;
            bool down = kind == 0x100 || kind == 0x104;
            bool up = kind == 0x101 || kind == 0x105;
            if (!down && !up) return false;
            Keys key = (Keys)message.WParam.ToInt32();
            long data = message.LParam.ToInt64();
            bool right = (data & 0x1000000) != 0;
            if (key == Keys.ControlKey) key = right ? Keys.RControlKey : Keys.LControlKey;
            if (key == Keys.Menu) key = right ? Keys.RMenu : Keys.LMenu;
            if (key == Keys.ShiftKey) key = ((data >> 16) & 255) == 0x36 ? Keys.RShiftKey : Keys.LShiftKey;
            if (key == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); return true; }
            if (!armed || !HotkeyInput.IsBindableKey(key)) return true;
            if (down) eventKeys.Add(key); else eventKeys.Remove(key);
            if (down)
            {
                HashSet<Keys> combined = new HashSet<Keys>(HotkeyInput.GetPressedKeys());
                combined.UnionWith(eventKeys);
                Remember(combined);
            }
            return true;
        }

        private void Remember(ISet<Keys> keys)
        {
            if (candidate == null || keys.Count > candidate.KeyCount)
            {
                candidate = HotkeyInput.CaptureBinding(keys);
                if (candidate != null) preview.Text = candidate.DisplayName;
            }
        }

        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if ((keyData & Keys.KeyCode) == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
            // Enter, Space, Tab and Alt combinations are recorded, never sent to Cancel.
            return true;
        }

        private void Poll(object sender, EventArgs args)
        {
            // Do not capture input if the user switches to another program.
            if (Form.ActiveForm != this) { candidate = null; eventKeys.Clear(); armed = false; return; }
            ISet<Keys> keys = HotkeyInput.GetPressedKeys();
            keys.UnionWith(eventKeys);
            keys.Remove(Keys.LButton);
            keys.Remove(Keys.RButton);
            if (keys.Contains(Keys.Escape)) { DialogResult = DialogResult.Cancel; Close(); return; }
            if (!armed) { if (keys.Count == 0) armed = true; return; }
            if (keys.Count > 0)
            {
                Remember(keys);
            }
            else if (candidate != null)
            {
                Binding = candidate;
                DialogResult = DialogResult.OK;
                Close();
            }
        }
    }
}
