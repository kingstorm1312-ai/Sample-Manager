using System.Drawing;
using System.Windows.Forms;

namespace SampleManager
{
    internal sealed class PasswordDialog : Form
    {
        private readonly TextBox passwordBox;

        internal PasswordDialog(string roleTitle)
        {
            AppTheme.ApplyForm(this);
            Text = roleTitle;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            ClientSize = new Size(360, 150);

            Label passwordLabel = new Label();
            passwordLabel.Text = "Mật khẩu";
            passwordLabel.Location = new Point(24, 22);
            passwordLabel.AutoSize = true;
            AppTheme.StyleLabel(passwordLabel, false);
            Controls.Add(passwordLabel);

            passwordBox = new TextBox();
            passwordBox.Location = new Point(24, 46);
            passwordBox.Width = 312;
            passwordBox.UseSystemPasswordChar = true;
            AppTheme.StyleTextBox(passwordBox, false);
            Controls.Add(passwordBox);

            Button confirmButton = new Button();
            confirmButton.Text = "Xác nhận";
            confirmButton.Location = new Point(152, 96);
            confirmButton.Size = new Size(92, 38);
            confirmButton.DialogResult = DialogResult.OK;
            AppTheme.StylePrimaryButton(confirmButton);
            Controls.Add(confirmButton);

            Button cancelButton = new Button();
            cancelButton.Text = "Hủy";
            cancelButton.Location = new Point(244, 96);
            cancelButton.Size = new Size(92, 38);
            cancelButton.DialogResult = DialogResult.Cancel;
            AppTheme.StyleSecondaryButton(cancelButton);
            Controls.Add(cancelButton);

            AcceptButton = confirmButton;
            CancelButton = cancelButton;
            Shown += delegate { passwordBox.Focus(); };
        }

        internal string Password
        {
            get { return passwordBox.Text; }
        }
    }
}
