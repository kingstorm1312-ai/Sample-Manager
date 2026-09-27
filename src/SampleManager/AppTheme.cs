using System;
using System.Drawing;
using System.Windows.Forms;

namespace SampleManager
{
    internal static class AppTheme
    {
        public static readonly Color Background = Color.FromArgb(245, 247, 250);
        public static readonly Color Card = Color.White;
        public static readonly Color Accent = Color.FromArgb(37, 99, 235);
        public static readonly Color AccentHover = Color.FromArgb(29, 78, 216);
        public static readonly Color Text = Color.FromArgb(17, 24, 39);
        public static readonly Color Muted = Color.FromArgb(107, 114, 128);
        public static readonly Color Border = Color.FromArgb(209, 213, 219);
        public static readonly Color Success = Color.FromArgb(4, 120, 87);
        public static readonly Color SuccessBackground = Color.FromArgb(236, 253, 245);
        public static readonly Color Error = Color.FromArgb(185, 28, 28);
        public static readonly Color ErrorBackground = Color.FromArgb(254, 242, 242);
        public static readonly Color NeutralBackground = Color.FromArgb(243, 244, 246);

        public const int SpaceSmall = 12;
        public const int SpaceMedium = 16;
        public const int SpaceLarge = 24;
        public const int InputHeight = 36;

        private static readonly Font BodyFontValue = new Font("Segoe UI", 10F, FontStyle.Regular);
        private static readonly Font SmallFontValue = new Font("Segoe UI", 9F, FontStyle.Regular);
        private static readonly Font SectionFontValue = new Font("Segoe UI", 11F, FontStyle.Bold);
        private static readonly Font TitleFontValue = new Font("Segoe UI", 20F, FontStyle.Bold);

        public static Font BodyFont { get { return BodyFontValue; } }
        public static Font SmallFont { get { return SmallFontValue; } }
        public static Font SectionFont { get { return SectionFontValue; } }
        public static Font TitleFont { get { return TitleFontValue; } }

        public static void ApplyForm(Form form)
        {
            form.BackColor = Background;
            form.ForeColor = Text;
            form.Font = BodyFont;
            form.FormBorderStyle = FormBorderStyle.FixedSingle;
            form.MaximizeBox = false;
            form.MinimizeBox = false;
        }

        public static Panel CreateCard()
        {
            Panel card = new Panel();
            card.BackColor = Card;
            card.BorderStyle = BorderStyle.FixedSingle;
            return card;
        }

        public static void StyleLabel(Label label, bool heading)
        {
            label.Font = heading ? SectionFont : BodyFont;
            label.ForeColor = heading ? Text : Muted;
            label.BackColor = Color.Transparent;
        }

        public static void StyleInput(Control control)
        {
            control.Font = BodyFont;
            control.ForeColor = Text;
            control.BackColor = Card;
            control.Height = InputHeight;
        }

        public static void StyleComboBox(ComboBox control)
        {
            StyleInput(control);
            control.FlatStyle = FlatStyle.Standard;
            control.DropDownWidth = Math.Max(control.Width, 320);
            control.DropDownStyle = ComboBoxStyle.DropDownList;
        }

        public static void StyleTextBox(TextBox control, bool multiline)
        {
            StyleInput(control);
            control.BorderStyle = BorderStyle.FixedSingle;
            control.Multiline = multiline;
            if (multiline)
            {
                control.Height = 48;
                control.ScrollBars = ScrollBars.Vertical;
            }
        }

        public static void StyleNumeric(NumericUpDown control)
        {
            StyleInput(control);
            control.BorderStyle = BorderStyle.FixedSingle;
        }

        public static void StyleDatePicker(DateTimePicker control)
        {
            StyleInput(control);
            control.Format = DateTimePickerFormat.Custom;
            control.CustomFormat = "dd/MM/yy";
        }

        public static void StylePrimaryButton(Button button)
        {
            button.Font = BodyFont;
            button.BackColor = Accent;
            button.ForeColor = Color.White;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = AccentHover;
            button.FlatAppearance.MouseDownBackColor = AccentHover;
            button.Height = 38;
            button.Cursor = Cursors.Hand;
        }

        public static void StyleSecondaryButton(Button button)
        {
            button.Font = BodyFont;
            button.BackColor = Card;
            button.ForeColor = Accent;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Border;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = NeutralBackground;
            button.Height = 38;
            button.Cursor = Cursors.Hand;
        }

        public static void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor = Card;
            grid.BorderStyle = BorderStyle.None;
            grid.GridColor = Border;
            grid.RowHeadersVisible = false;
            grid.EnableHeadersVisualStyles = false;
            grid.AllowUserToResizeRows = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
            grid.ColumnHeadersDefaultCellStyle.BackColor = NeutralBackground;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Text;
            grid.ColumnHeadersDefaultCellStyle.Font = SectionFont;
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(8, 0, 8, 0);
            grid.DefaultCellStyle.BackColor = Card;
            grid.DefaultCellStyle.ForeColor = Text;
            grid.DefaultCellStyle.Font = BodyFont;
            grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(219, 234, 254);
            grid.DefaultCellStyle.SelectionForeColor = Text;
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(249, 250, 251);
            grid.RowTemplate.Height = 34;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        }

        public static void SetMutedStatus(Label label, string text)
        {
            SetStatus(label, text, Muted, NeutralBackground);
        }

        public static void SetSuccessStatus(Label label, string text)
        {
            SetStatus(label, text, Success, SuccessBackground);
        }

        public static void SetErrorStatus(Label label, string text)
        {
            SetStatus(label, text, Error, ErrorBackground);
        }

        private static void SetStatus(Label label, string text, Color foreground, Color background)
        {
            label.Text = text;
            label.ForeColor = foreground;
            label.BackColor = background;
            label.Padding = new Padding(10, 6, 10, 6);
            label.AutoSize = true;
        }
    }
}
