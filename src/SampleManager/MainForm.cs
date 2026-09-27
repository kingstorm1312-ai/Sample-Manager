using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace SampleManager
{
    internal sealed class MainForm : Form
    {
        private readonly Button requestButton;
        private readonly Button sampleManagementButton;
        private readonly Button logoutButton;
        private readonly Button checkUpdatesButton;
        private readonly Label errorLabel;
        private readonly Label versionLabel;
        private readonly GoogleSheetsSampleRepository repository;
        private readonly SampleManagerCache cache;
        private readonly AuthenticationService authenticationService;
        private bool loadStarted;

        public MainForm(
            GoogleSheetsSampleRepository repository,
            SampleManagerCache cache)
        {
            this.repository = repository;
            this.cache = cache;
            authenticationService = new AuthenticationService();
            AppTheme.ApplyForm(this);
            Text = "Sample Manager";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(860, 520);
            MinimumSize = new Size(860, 520);

            Panel shell = AppTheme.CreateCard();
            shell.Location = new Point(32, 32);
            shell.Size = new Size(796, 456);
            Controls.Add(shell);

            Label title = new Label();
            title.Text = "Sample Manager";
            title.AutoSize = true;
            title.Location = new Point(32, 34);
            title.Font = AppTheme.TitleFont;
            title.ForeColor = AppTheme.Text;
            shell.Controls.Add(title);

            checkUpdatesButton = new Button();
            checkUpdatesButton.Text = "Kiểm tra cập nhật";
            checkUpdatesButton.Location = new Point(482, 32);
            checkUpdatesButton.Size = new Size(152, 38);
            AppTheme.StyleSecondaryButton(checkUpdatesButton);
            checkUpdatesButton.Click += CheckForUpdates;
            shell.Controls.Add(checkUpdatesButton);

            logoutButton = new Button();
            logoutButton.Text = "Đăng xuất";
            logoutButton.Location = new Point(650, 32);
            logoutButton.Size = new Size(112, 38);
            AppTheme.StyleSecondaryButton(logoutButton);
            logoutButton.Click += Logout;
            shell.Controls.Add(logoutButton);

            requestButton = CreateModuleButton("Yêu cầu mẫu", 32, 150);
            requestButton.TabIndex = 0;
            requestButton.Click += OpenRequestForm;
            shell.Controls.Add(requestButton);

            sampleManagementButton = CreateModuleButton("Quản lý mẫu", 408, 150);
            sampleManagementButton.TabIndex = 1;
            sampleManagementButton.Click += OpenSampleManagementForm;
            shell.Controls.Add(sampleManagementButton);

            errorLabel = new Label();
            errorLabel.Location = new Point(32, 370);
            AppTheme.SetMutedStatus(errorLabel, "Đang tải dữ liệu");
            shell.Controls.Add(errorLabel);

            versionLabel = new Label();
            versionLabel.Text = "v" + Application.ProductVersion;
            versionLabel.AutoSize = true;
            versionLabel.Font = AppTheme.SmallFont;
            versionLabel.ForeColor = AppTheme.Muted;
            versionLabel.BackColor = Color.Transparent;
            versionLabel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            versionLabel.Location = new Point(
                shell.ClientSize.Width - versionLabel.PreferredWidth - 32,
                shell.ClientSize.Height - versionLabel.PreferredHeight - 20);
            shell.Controls.Add(versionLabel);

            AcceptButton = requestButton;
            Shown += delegate { requestButton.Focus(); };
            Shown += StartLiveLoad;
        }

        private void StartLiveLoad(object sender, EventArgs e)
        {
            if (loadStarted)
            {
                return;
            }
            loadStarted = true;

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                Exception loadException = null;
                try
                {
                    cache.LoadLive(repository);
                }
                catch (Exception exception)
                {
                    loadException = exception;
                }

                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }

                try
                {
                    BeginInvoke((MethodInvoker)delegate
                    {
                        if (loadException == null)
                        {
                            requestButton.Enabled = true;
                            sampleManagementButton.Enabled = true;
                            AppTheme.SetSuccessStatus(errorLabel, "Sẵn sàng");
                            return;
                        }

                        AppTheme.SetErrorStatus(errorLabel, "Không thể tải dữ liệu");
                        MessageBox.Show(
                            this,
                            loadException.Message,
                            "Không thể tải dữ liệu",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Error);
                    });
                }
                catch (InvalidOperationException)
                {
                }
            });
        }

        private void CheckForUpdates(object sender, EventArgs e)
        {
            checkUpdatesButton.Enabled = false;
            AppTheme.SetMutedStatus(errorLabel, "Đang kiểm tra cập nhật");
            UpdateCoordinator.CheckNow(
                this,
                delegate(string status) { AppTheme.SetMutedStatus(errorLabel, status); },
                delegate { checkUpdatesButton.Enabled = true; });
        }

        private static Button CreateModuleButton(string text, int x, int y)
        {
            Button button = new Button();
            button.Text = text;
            button.Size = new Size(324, 170);
            button.Location = new Point(x, y);
            button.Font = new Font("Segoe UI", 14F, FontStyle.Bold);
            AppTheme.StyleSecondaryButton(button);
            return button;
        }

        private void OpenRequestForm(object sender, EventArgs e)
        {
            if (!authenticationService.RequireRole(this, AuthRole.QA))
            {
                return;
            }

            try
            {
                using (SampleRequestForm form = new SampleRequestForm(repository, cache))
                {
                    form.ShowDialog(this);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Không thể mở Yêu cầu mẫu", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OpenSampleManagementForm(object sender, EventArgs e)
        {
            if (!authenticationService.RequireRole(this, AuthRole.SampleManagement))
            {
                return;
            }

            try
            {
                using (SampleManagementForm form = new SampleManagementForm(repository, cache))
                {
                    form.ShowDialog(this);
                }
            }
            catch (Exception exception)
            {
                MessageBox.Show(this, exception.Message, "Không thể mở Quản lý mẫu", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Logout(object sender, EventArgs e)
        {
            authenticationService.Logout();
            AppTheme.SetSuccessStatus(errorLabel, "Đã đăng xuất");
        }
    }
}
