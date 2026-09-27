using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SampleManager
{
    internal sealed class SampleRequestForm : Form
    {
        private readonly GoogleSheetsSampleRepository repository;
        private readonly SampleManagerCache cache;
        private readonly ComboBox qaComboBox;
        private readonly TextBox soHopDongTextBox;
        private readonly TextBox maVatTuTextBox;
        private readonly TextBox tenTuiTextBox;
        private readonly TextBox noiYeuCauTextBox;
        private readonly NumericUpDown soLuongMauNumeric;
        private readonly ComboBox phienBanComboBox;
        private readonly DateTimePicker deadlinePicker;
        private readonly TextBox ghiChuTextBox;
        private readonly Button saveButton;
        private readonly Button newRequestButton;
        private readonly Button managementButton;
        private readonly Label normalizedLabel;
        private readonly Label resultLabel;
        private readonly SampleRequestCreationService creationService;
        private SampleRequestRecord pendingSaveRequest;
        private string pendingOperationId;

        public SampleRequestRecord LastReadBack { get; private set; }

        public SampleRequestForm(
            GoogleSheetsSampleRepository repository,
            SampleManagerCache cache)
        {
            this.repository = repository;
            this.cache = cache;
            creationService = new SampleRequestCreationService(repository, cache);
            AppTheme.ApplyForm(this);
            Text = "Yêu cầu mẫu";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1120, 600);
            MinimumSize = new Size(1120, 600);

            Panel headerCard = AppTheme.CreateCard();
            headerCard.Location = new Point(24, 24);
            headerCard.Size = new Size(1072, 82);
            Controls.Add(headerCard);

            Label eyebrow = new Label();
            eyebrow.Text = "SAMPLE MANAGER  /  REQUEST";
            eyebrow.AutoSize = true;
            eyebrow.Location = new Point(24, 14);
            eyebrow.Font = AppTheme.SmallFont;
            eyebrow.ForeColor = AppTheme.Accent;
            headerCard.Controls.Add(eyebrow);

            Label header = new Label();
            header.Text = "Yêu cầu mẫu";
            header.AutoSize = true;
            header.Location = new Point(22, 32);
            header.Font = AppTheme.TitleFont;
            header.ForeColor = AppTheme.Text;
            headerCard.Controls.Add(header);

            Panel formCard = AppTheme.CreateCard();
            formCard.Location = new Point(24, 122);
            formCard.Size = new Size(1072, 402);
            Controls.Add(formCard);

            Label formTitle = new Label();
            formTitle.Text = "Thông tin yêu cầu";
            formTitle.AutoSize = true;
            formTitle.Location = new Point(24, 18);
            AppTheme.StyleLabel(formTitle, true);
            formCard.Controls.Add(formTitle);

            qaComboBox = AddCombo(formCard, "QA", 24, 56, 450, 0);
            soHopDongTextBox = AddTextBox(formCard, "Số hợp đồng", 556, 56, 450, 1, false);
            maVatTuTextBox = AddTextBox(formCard, "Mã vật tư", 24, 126, 450, 2, false);
            tenTuiTextBox = AddTextBox(formCard, "Tên túi", 556, 126, 450, 3, false);
            noiYeuCauTextBox = AddTextBox(formCard, "Nơi yêu cầu", 24, 196, 450, 4, true);
            ghiChuTextBox = AddTextBox(formCard, "Ghi chú", 556, 196, 450, 8, true);
            soLuongMauNumeric = AddNumeric(formCard, "Số lượng mẫu", 24, 266, 220, 5);
            phienBanComboBox = AddVersionCombo(formCard, "Phiên bản", 258, 266, 220, 6);
            phienBanComboBox.Enabled = false;
            phienBanComboBox.BackColor = AppTheme.NeutralBackground;
            phienBanComboBox.TabStop = false;
            deadlinePicker = AddDatePicker(formCard, "Deadline", 556, 266, 220, 7);

            normalizedLabel = new Label();
            normalizedLabel.Text = String.Empty;
            normalizedLabel.AutoSize = true;
            normalizedLabel.Location = new Point(24, 324);
            normalizedLabel.Font = AppTheme.SmallFont;
            normalizedLabel.ForeColor = AppTheme.Muted;
            formCard.Controls.Add(normalizedLabel);

            saveButton = new Button();
            saveButton.Text = "Lưu yêu cầu";
            saveButton.Size = new Size(150, 38);
            saveButton.Location = new Point(24, 348);
            saveButton.TabIndex = 9;
            saveButton.Enabled = false;
            AppTheme.StylePrimaryButton(saveButton);
            saveButton.Click += SaveRequest;
            formCard.Controls.Add(saveButton);
            AcceptButton = saveButton;

            newRequestButton = new Button();
            newRequestButton.Text = "Tạo yêu cầu mới";
            newRequestButton.Size = new Size(174, 38);
            newRequestButton.Location = new Point(190, 348);
            newRequestButton.TabIndex = 10;
            newRequestButton.Enabled = false;
            AppTheme.StyleSecondaryButton(newRequestButton);
            newRequestButton.Click += StartNewRequest;
            formCard.Controls.Add(newRequestButton);

            managementButton = new Button();
            managementButton.Text = "Quản lý yêu cầu";
            managementButton.Size = new Size(174, 38);
            managementButton.Location = new Point(382, 348);
            managementButton.TabIndex = 11;
            managementButton.Enabled = false;
            AppTheme.StyleSecondaryButton(managementButton);
            managementButton.Click += OpenManagementForm;
            formCard.Controls.Add(managementButton);

            resultLabel = new Label();
            resultLabel.Location = new Point(574, 353);
            AppTheme.SetMutedStatus(resultLabel, "Chưa lưu");
            formCard.Controls.Add(resultLabel);

            cache.Changed += CacheChanged;
            FormClosed += delegate { cache.Changed -= CacheChanged; };
            Shown += LoadQaOptions;
        }

        private ComboBox AddCombo(Panel parent, string labelText, int x, int y, int width, int tabIndex)
        {
            AddFieldLabel(parent, labelText, x, y, width);
            ComboBox control = new ComboBox();
            control.Location = new Point(x, y + 22);
            control.Size = new Size(width, AppTheme.InputHeight);
            control.TabIndex = tabIndex;
            AppTheme.StyleComboBox(control);
            parent.Controls.Add(control);
            return control;
        }

        private TextBox AddTextBox(Panel parent, string labelText, int x, int y, int width, int tabIndex, bool multiline)
        {
            AddFieldLabel(parent, labelText, x, y, width);
            TextBox control = new TextBox();
            control.Location = new Point(x, y + 22);
            control.Size = new Size(width, multiline ? 48 : AppTheme.InputHeight);
            control.TabIndex = tabIndex;
            AppTheme.StyleTextBox(control, multiline);
            parent.Controls.Add(control);
            return control;
        }

        private NumericUpDown AddNumeric(Panel parent, string labelText, int x, int y, int width, int tabIndex)
        {
            AddFieldLabel(parent, labelText, x, y, width);
            NumericUpDown control = new NumericUpDown();
            control.Location = new Point(x, y + 22);
            control.Size = new Size(width, AppTheme.InputHeight);
            control.Minimum = 1;
            control.Maximum = 100000;
            control.Value = 1;
            control.TabIndex = tabIndex;
            AppTheme.StyleNumeric(control);
            parent.Controls.Add(control);
            return control;
        }

        private DateTimePicker AddDatePicker(Panel parent, string labelText, int x, int y, int width, int tabIndex)
        {
            AddFieldLabel(parent, labelText, x, y, width);
            DateTimePicker control = new DateTimePicker();
            control.Location = new Point(x, y + 22);
            control.Size = new Size(width, AppTheme.InputHeight);
            control.TabIndex = tabIndex;
            AppTheme.StyleDatePicker(control);
            parent.Controls.Add(control);
            return control;
        }

        private ComboBox AddVersionCombo(Panel parent, string labelText, int x, int y, int width, int tabIndex)
        {
            ComboBox control = AddCombo(parent, labelText, x, y, width, tabIndex);
            for (int version = 1; version <= 100; version++)
            {
                control.Items.Add("V" + version.ToString(CultureInfo.InvariantCulture));
            }
            control.SelectedIndex = 0;
            return control;
        }

        private void AddFieldLabel(Panel parent, string text, int x, int y, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Size = new Size(width, 20);
            label.Location = new Point(x, y);
            AppTheme.StyleLabel(label, false);
            parent.Controls.Add(label);
        }

        private void LoadQaOptions(object sender, EventArgs e)
        {
            ApplyQaOptions(true);
        }

        private void CacheChanged(object sender, EventArgs e)
        {
            if (IsDisposed || !IsHandleCreated)
            {
                return;
            }

            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke((MethodInvoker)delegate { ApplyQaOptions(false); });
                }
                else
                {
                    ApplyQaOptions(false);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void ApplyQaOptions(bool focus)
        {
            try
            {
                SampleManagerCacheSnapshot snapshot = cache.Snapshot();
                if (!snapshot.IsLoaded)
                {
                    managementButton.Enabled = false;
                    saveButton.Enabled = false;
                    AppTheme.SetMutedStatus(resultLabel, "Đang tải dữ liệu");
                    return;
                }
                IList<QaOption> options = snapshot.QaOptions;
                QaOption selected = qaComboBox.SelectedItem as QaOption;
                string selectedId = selected == null ? String.Empty : selected.Id;
                qaComboBox.DataSource = options;
                qaComboBox.SelectedIndex = options.Count > 0 ? 0 : -1;
                for (int index = 0; index < options.Count; index++)
                {
                    if (String.Equals(options[index].Id, selectedId, StringComparison.Ordinal))
                    {
                        qaComboBox.SelectedIndex = index;
                        break;
                    }
                }
                managementButton.Enabled = options.Count > 0;
                saveButton.Enabled = options.Count > 0
                    && (LastReadBack == null || pendingSaveRequest != null);
                if (options.Count == 0)
                {
                    AppTheme.SetErrorStatus(resultLabel, "Không có QA");
                }
                else if (LastReadBack == null && pendingSaveRequest == null)
                {
                    AppTheme.SetMutedStatus(resultLabel, "Chưa lưu");
                }
                if (focus && options.Count > 0)
                {
                    qaComboBox.Focus();
                }
            }
            catch (Exception exception)
            {
                AppTheme.SetErrorStatus(resultLabel, "Không đọc được DM_QA: " + exception.Message);
                saveButton.Enabled = false;
            }
        }

        private async void SaveRequest(object sender, EventArgs e)
        {
            try
            {
                QaOption qa = qaComboBox.SelectedItem as QaOption;
                if (qa == null) throw new InvalidOperationException("Vui lòng chọn QA.");
                string soHopDong = SampleNormalization.NormalizeSoHopDong(soHopDongTextBox.Text);
                string maVatTu = SampleNormalization.NormalizeMaVatTu(maVatTuTextBox.Text);
                string phienBan = SampleNormalization.NormalizePhienBan(
                    phienBanComboBox.SelectedItem == null ? String.Empty : phienBanComboBox.SelectedItem.ToString());
                if (String.IsNullOrWhiteSpace(soHopDong)) throw new InvalidOperationException("Số hợp đồng không được trống.");
                if (String.IsNullOrWhiteSpace(maVatTu)) throw new InvalidOperationException("Mã vật tư không được trống.");

                soHopDongTextBox.Text = soHopDong;
                maVatTuTextBox.Text = maVatTu;
                string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
                SampleRequestRecord record = pendingSaveRequest;
                if (record == null)
                {
                    record = new SampleRequestRecord
                    {
                        YeuCauId = "YCM-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture),
                        NgayTaoYeuCau = now,
                        SoHopDong = soHopDong,
                        MaVatTu = maVatTu,
                        TenTui = tenTuiTextBox.Text.Trim(),
                        QaId = qa.Id,
                        NoiYeuCau = noiYeuCauTextBox.Text.Trim(),
                        SoLuongMau = soLuongMauNumeric.Value.ToString(CultureInfo.InvariantCulture),
                        PhienBan = phienBan,
                        Deadline = deadlinePicker.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        TrangThai = "Mới",
                        GhiChu = ghiChuTextBox.Text.Trim()
                    };
                    pendingSaveRequest = record;
                    pendingOperationId = Guid.NewGuid().ToString("D");
                }

                saveButton.Enabled = false;
                SampleRequestCreationResult result = await creationService.CreateAsync(record, pendingOperationId);
                LastReadBack = result.Request;
                pendingSaveRequest = null;
                pendingOperationId = null;
                AppTheme.SetSuccessStatus(resultLabel, "Đã lưu");
                normalizedLabel.Text = "Đã chuẩn hóa: " + LastReadBack.SoHopDong + " | " + LastReadBack.MaVatTu;
                normalizedLabel.ForeColor = AppTheme.Success;
                newRequestButton.Enabled = true;
                MessageBox.Show(this, "Đã lưu", "Đã lưu", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (SampleRequestCreationException exception)
            {
                saveButton.Enabled = true;
                if (exception.Request != null)
                {
                    pendingSaveRequest = exception.Request;
                    AppTheme.SetErrorStatus(resultLabel, "Chưa hoàn tất; có thể thử lại");
                    MessageBox.Show(this, exception.Message + " Thử lại sẽ dùng cùng mã thao tác.", "Không thể lưu", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                else
                {
                    AppTheme.SetErrorStatus(resultLabel, "Không thể lưu");
                    MessageBox.Show(this, exception.Message, "Không thể lưu", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            catch (Exception exception)
            {
                saveButton.Enabled = true;
                AppTheme.SetErrorStatus(resultLabel, "Không thể lưu yêu cầu mẫu");
                MessageBox.Show(this, exception.Message, "Không thể lưu yêu cầu mẫu", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StartNewRequest(object sender, EventArgs e)
        {
            soHopDongTextBox.Clear();
            maVatTuTextBox.Clear();
            tenTuiTextBox.Clear();
            noiYeuCauTextBox.Clear();
            ghiChuTextBox.Clear();
            soLuongMauNumeric.Value = 1;
            phienBanComboBox.SelectedIndex = 0;
            deadlinePicker.Value = DateTime.Today;
            LastReadBack = null;
            pendingSaveRequest = null;
            pendingOperationId = null;
            normalizedLabel.Text = String.Empty;
            normalizedLabel.ForeColor = AppTheme.Muted;
            AppTheme.SetMutedStatus(resultLabel, "Chưa lưu");
            newRequestButton.Enabled = false;
            saveButton.Enabled = true;
            soHopDongTextBox.Focus();
        }

        private void OpenManagementForm(object sender, EventArgs e)
        {
            QaOption qa = qaComboBox.SelectedItem as QaOption;
            if (qa == null)
            {
                return;
            }
            using (RequestManagementForm form = new RequestManagementForm(repository, cache, qa))
            {
                form.ShowDialog(this);
            }
        }
    }
}
