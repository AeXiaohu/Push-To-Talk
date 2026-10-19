using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace PushToTalk
{
    internal static class DarkTheme
    {
        public static readonly Color Background = Color.FromArgb(20, 22, 27);
        public static readonly Color Surface = Color.FromArgb(29, 33, 40);
        public static readonly Color Field = Color.FromArgb(24, 28, 35);
        public static readonly Color Text = Color.FromArgb(235, 239, 245);
        public static readonly Color Subtle = Color.FromArgb(162, 174, 191);
        public static readonly Color Accent = Color.FromArgb(120, 177, 255);
        public static readonly Color Border = Color.FromArgb(66, 76, 91);
        public static readonly Color MutedSurface = Color.FromArgb(34, 44, 60);
        public static readonly Color LiveSurface = Color.FromArgb(23, 55, 45);
        public static readonly Color LiveText = Color.FromArgb(120, 226, 171);
        private static readonly Color Primary = Color.FromArgb(49, 102, 185);
        private static readonly Color Selection = Color.FromArgb(47, 68, 98);

        public static void StyleButton(Button button, bool primary)
        {
            Color normal = primary ? Primary : Surface;
            Color hover = primary ? Color.FromArgb(58, 118, 210) : Color.FromArgb(43, 50, 61);
            button.FlatStyle = FlatStyle.Flat;
            button.UseVisualStyleBackColor = false;
            button.ForeColor = Text;
            button.BackColor = normal;
            button.FlatAppearance.BorderColor = primary ? Primary : Border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = hover;
            button.FlatAppearance.MouseDownBackColor = primary ? Color.FromArgb(43, 86, 155) : Field;
            button.EnabledChanged += delegate
            {
                button.BackColor = button.Enabled ? normal : Field;
                button.ForeColor = button.Enabled ? Text : Subtle;
                button.Invalidate();
            };
            // WinForms substitutes its own low-contrast text color for disabled buttons.
            // Paint their full face last so the label remains readable on our dark field.
            button.Paint += delegate(object sender, PaintEventArgs args)
            {
                if (button.Enabled) return;
                Rectangle bounds = button.ClientRectangle;
                using (SolidBrush brush = new SolidBrush(Field)) args.Graphics.FillRectangle(brush, bounds);
                if (bounds.Width > 1 && bounds.Height > 1)
                    using (Pen pen = new Pen(Border))
                        args.Graphics.DrawRectangle(pen, 0, 0, bounds.Width - 1, bounds.Height - 1);
                bounds.Inflate(-6, 0);
                TextRenderer.DrawText(args.Graphics, button.Text, button.Font, bounds, Subtle,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            };
            button.GotFocus += delegate { button.FlatAppearance.BorderColor = Accent; };
            button.LostFocus += delegate { button.FlatAppearance.BorderColor = primary ? Primary : Border; };
        }

        public static void StyleComboBox(ComboBox combo)
        {
            combo.BackColor = Field;
            combo.ForeColor = Text;
            combo.FlatStyle = FlatStyle.Flat;
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = Math.Max(22, combo.Font.Height + 7);
            combo.DrawItem += delegate(object sender, DrawItemEventArgs args)
            {
                bool selected = (args.State & DrawItemState.Selected) != 0;
                Color background = selected ? Selection : Field;
                using (SolidBrush brush = new SolidBrush(background)) args.Graphics.FillRectangle(brush, args.Bounds);
                string value = args.Index >= 0 && args.Index < combo.Items.Count
                    ? combo.GetItemText(combo.Items[args.Index]) : combo.Text;
                Rectangle bounds = args.Bounds;
                bounds.X += 7;
                bounds.Width = Math.Max(0, bounds.Width - 9);
                TextRenderer.DrawText(args.Graphics, value, args.Font, bounds, combo.Enabled ? Text : Subtle,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                if ((args.State & DrawItemState.Focus) != 0) args.DrawFocusRectangle();
            };
        }

        public static void StyleListBox(ListBox list)
        {
            list.BackColor = Field;
            list.ForeColor = Text;
            list.DrawMode = DrawMode.OwnerDrawFixed;
            list.DrawItem += delegate(object sender, DrawItemEventArgs args)
            {
                bool selected = (args.State & DrawItemState.Selected) != 0;
                using (SolidBrush brush = new SolidBrush(selected ? Selection : Field))
                    args.Graphics.FillRectangle(brush, args.Bounds);
                if (args.Index >= 0 && args.Index < list.Items.Count)
                {
                    Rectangle bounds = args.Bounds;
                    bounds.X += 8;
                    bounds.Width = Math.Max(0, bounds.Width - 10);
                    TextRenderer.DrawText(args.Graphics, list.GetItemText(list.Items[args.Index]), args.Font,
                        bounds, list.Enabled ? Text : Subtle, TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                        TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoClipping);
                }
                if ((args.State & DrawItemState.Focus) != 0) args.DrawFocusRectangle();
            };
        }

        public static void StyleCheckBox(CheckBox check)
        {
            check.ForeColor = Text;
            check.BackColor = Background;
            check.UseVisualStyleBackColor = false;
            check.FlatStyle = FlatStyle.Flat;
            check.FlatAppearance.BorderColor = Border;
            check.FlatAppearance.CheckedBackColor = Field;
            check.FlatAppearance.MouseOverBackColor = Surface;
            check.FlatAppearance.MouseDownBackColor = Selection;
        }

        public static void StyleMenu(ContextMenuStrip menu)
        {
            menu.BackColor = Surface;
            menu.ForeColor = Text;
            menu.Renderer = new DarkMenuRenderer();
            menu.ShowImageMargin = false;
        }

        public static void ApplyTitleBar(Form form)
        {
            form.HandleCreated += delegate { SetTitleBar(form); };
            if (form.IsHandleCreated) SetTitleBar(form);
        }

        private static void SetTitleBar(Form form)
        {
            try
            {
                int enabled = 1;
                if (DwmSetWindowAttribute(form.Handle, 20, ref enabled, sizeof(int)) != 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref enabled, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

        private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
        {
            public DarkMenuRenderer() : base(new DarkMenuColors()) { RoundedEdges = false; }
            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs args)
            {
                args.TextColor = args.Item.Enabled ? Text : Subtle;
                base.OnRenderItemText(args);
            }
        }

        private sealed class DarkMenuColors : ProfessionalColorTable
        {
            public DarkMenuColors() { UseSystemColors = false; }
            public override Color ToolStripDropDownBackground { get { return Surface; } }
            public override Color MenuBorder { get { return Border; } }
            public override Color MenuItemBorder { get { return Accent; } }
            public override Color MenuItemSelected { get { return Selection; } }
            public override Color MenuItemSelectedGradientBegin { get { return Selection; } }
            public override Color MenuItemSelectedGradientEnd { get { return Selection; } }
            public override Color SeparatorDark { get { return Border; } }
            public override Color SeparatorLight { get { return Surface; } }
            public override Color ImageMarginGradientBegin { get { return Surface; } }
            public override Color ImageMarginGradientMiddle { get { return Surface; } }
            public override Color ImageMarginGradientEnd { get { return Surface; } }
        }
    }
}
