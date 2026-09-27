using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SampleManager
{
    internal sealed class SampleDetailForm : Form
    {
        private readonly GoogleSheetsSampleRepository repository;
        private readonly SampleManagerCache cache;
        private readonly SampleManagementRecord source;
        private readonly DateTimePicker approvedDatePicker;
        private readonly DateTimePicker expiryDatePicker;
        private readonly DateTimePicker deliveredDatePicker;
        private readonly TextBox approverTextBox;
        private readonly TextBox storageTextBox;
        private readonly TextBox noteTextBox;
        private readonly ComboBox phienBanComboBox;
        private readonly ComboBox sampleStatusComboBox;
        private readonly Label statusLabel;
        private readonly Panel contentScroll;
        private readonly Panel contentHost;
        private readonly Panel headerCard;
        private readonly Panel sourceCard;
        private readonly Panel updateCard;

        public SampleManagementRecord LastReadBack { get; private set; }

        public SampleDetailForm(
            GoogleSheetsSampleRepository repository,
            SampleManagerCache cache,
            SampleManagementRecord source)
        {
            this.repository = repository;
            this.cache = cache;
            this.source = source;
            AppTheme.ApplyForm(this);
            Text = "Chi tiết mẫu";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MaximizeBox = true;
            ClientSize = new Size(920, 700);
            MinimumSize = new Size(720, 460);

            contentScroll = new Panel();
            contentScroll.Dock = DockStyle.Fill;
            contentScroll.AutoScroll = true;
            contentScroll.BackColor = AppTheme.Background;
            Controls.Add(contentScroll);

            Panel footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 64;
            footer.BackColor = AppTheme.Background;
            Controls.Add(footer);

            contentHost = new Panel();
            contentHost.Location = new Point(24, 24);
            contentHost.BackColor = AppTheme.Background;
            contentScroll.Controls.Add(contentHost);

            headerCard = AppTheme.CreateCard();
            headerCard.Location = new Point(0, 0);
            headerCard.Size = new Size(872, 76);
            contentHost.Controls.Add(headerCard);

            Label title = new Label();
            title.Text = "Chi tiết mẫu";
            title.AutoSize = true;
            title.Location = new Point(24, 23);
            title.Font = AppTheme.TitleFont;
            title.ForeColor = AppTheme.Text;
            headerCard.Controls.Add(title);

            sourceCard = AppTheme.CreateCard();
            sourceCard.Location = new Point(0, 92);
            sourceCard.Size = new Size(872, 244);
            contentHost.Controls.Add(sourceCard);

            Label sourceTitle = new Label();
            sourceTitle.Text = "Thông tin mẫu";
            sourceTitle.Dock = DockStyle.Top;
            sourceTitle.Height = 44;
            sourceTitle.Padding = new Padding(24, 16, 0, 0);
            AppTheme.StyleLabel(sourceTitle, true);

            TableLayoutPanel sourceFields = CreateFieldTable(3, 3);
            sourceFields.Controls.Add(CreateReadOnlyField("Sample_ID", source.SampleId), 0, 0);
            sourceFields.Controls.Add(CreateReadOnlyField("Hợp đồng", GetRequestValue("SoHopDong")), 1, 0);
            sourceFields.Controls.Add(CreateReadOnlyField("Mã VT", GetRequestValue("MaVatTu")), 2, 0);
            sourceFields.Controls.Add(CreateReadOnlyField("Tên túi", GetRequestValue("TenTui")), 0, 1);
            sourceFields.Controls.Add(CreateReadOnlyField("QA", GetQaDisplayName()), 1, 1);
            sourceFields.Controls.Add(CreateReadOnlyField("Phiên bản", SampleNormalization.NormalizePhienBan(source.PhienBan)), 2, 1);
            sourceFields.Controls.Add(CreateReadOnlyField("STT mẫu", source.SttMau), 0, 2);
            sourceCard.Controls.Add(sourceFields);
            sourceCard.Controls.Add(sourceTitle);

            updateCard = AppTheme.CreateCard();
            updateCard.Location = new Point(0, 352);
            updateCard.Size = new Size(872, 268);
            contentHost.Controls.Add(updateCard);

            Label updateTitle = new Label();
            updateTitle.Text = "Quản lý mẫu";
            updateTitle.Dock = DockStyle.Top;
            updateTitle.Height = 44;
            updateTitle.Padding = new Padding(24, 16, 0, 0);
            AppTheme.StyleLabel(updateTitle, true);

            TableLayoutPanel updateFields = CreateFieldTable(4, 3);
            DateTimePicker approved = null;
            DateTimePicker expiry = null;
            DateTimePicker delivered = null;
            TextBox approver = null;
            TextBox storage = null;
            TextBox note = null;
            ComboBox phienBan = null;
            ComboBox sampleStatus = null;
            updateFields.Controls.Add(CreateDateField("Ngày duyệt", out approved), 0, 0);
            updateFields.Controls.Add(CreateDateField("Ngày hết hạn", out expiry), 1, 0);
            updateFields.Controls.Add(CreateTextField("Người duyệt", out approver), 2, 0);
            updateFields.Controls.Add(CreateTextField("Nơi lưu", out storage), 3, 0);
            updateFields.Controls.Add(CreateDateField("Ngày giao mẫu", out delivered), 0, 1);
            Panel noteField = CreateTextField("Ghi chú", out note);
            updateFields.Controls.Add(noteField, 1, 1);
            updateFields.SetColumnSpan(noteField, 2);
            updateFields.Controls.Add(CreateStatusField("Trạng thái may", out sampleStatus), 3, 1);
            updateFields.Controls.Add(CreateVersionField(out phienBan), 0, 2);
            updateCard.Controls.Add(updateFields);
            updateCard.Controls.Add(updateTitle);
            approvedDatePicker = approved;
            expiryDatePicker = expiry;
            deliveredDatePicker = delivered;
            approverTextBox = approver;
            storageTextBox = storage;
            noteTextBox = note;
            phienBanComboBox = phienBan;
            sampleStatusComboBox = sampleStatus;

            SetDatePicker(approvedDatePicker, source.NgayDuyet);
            SetDatePicker(expiryDatePicker, source.NgayHetHan);
            SetDatePicker(deliveredDatePicker, source.NgayGiaoMau);
            approverTextBox.Text = source.NguoiDuyet;
            storageTextBox.Text = source.NoiLuu;
            noteTextBox.Text = source.GhiChu;
            SetVersion(phienBanComboBox, source.PhienBan);
            phienBanComboBox.Enabled = CanConfirmVersionChange();
            if (!phienBanComboBox.Enabled)
            {
                phienBanComboBox.BackColor = AppTheme.NeutralBackground;
            }
            sampleStatusComboBox.SelectedItem = NormalizeSampleStatus(source.TrangThaiMay);

            Button saveButton = new Button();
            saveButton.Text = "Lưu";
            saveButton.Size = new Size(120, 38);
            saveButton.Location = new Point(24, 13);
            AppTheme.StylePrimaryButton(saveButton);
            saveButton.Click += SaveSample;
            footer.Controls.Add(saveButton);

            Button closeButton = new Button();
            closeButton.Text = "Đóng";
            closeButton.Size = new Size(120, 38);
            closeButton.Location = new Point(footer.ClientSize.Width - 144, 13);
            closeButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            AppTheme.StyleSecondaryButton(closeButton);
            closeButton.Click += delegate { Close(); };
            footer.Controls.Add(closeButton);

            statusLabel = new Label();
            statusLabel.Location = new Point(166, 18);
            AppTheme.SetMutedStatus(statusLabel, "Chưa lưu");
            footer.Controls.Add(statusLabel);

            contentScroll.Resize += delegate { UpdateContentLayout(); };
            UpdateContentLayout();
        }

        private TableLayoutPanel CreateFieldTable(int columns, int rows)
        {
            TableLayoutPanel table = new TableLayoutPanel();
            table.Dock = DockStyle.Fill;
            table.ColumnCount = columns;
            table.RowCount = rows;
            table.Padding = new Padding(24, 0, 24, 12);
            for (int column = 0; column < columns; column++)
            {
                table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F / columns));
            }
            for (int row = 0; row < rows; row++)
            {
                table.RowStyles.Add(new RowStyle(SizeType.Percent, 100F / rows));
            }
            return table;
        }

        private Panel CreateReadOnlyField(string labelText, string value)
        {
            TextBox control = CreateFieldTextBox(value, true);
            return CreateFieldPanel(labelText, control);
        }

        private Panel CreateTextField(string labelText, out TextBox control)
        {
            control = CreateFieldTextBox(String.Empty, false);
            return CreateFieldPanel(labelText, control);
        }

        private Panel CreateDateField(string labelText, out DateTimePicker control)
        {
            control = new DateTimePicker();
            control.Dock = DockStyle.Top;
            control.Height = AppTheme.InputHeight;
            control.ShowCheckBox = true;
            AppTheme.StyleDatePicker(control);
            return CreateFieldPanel(labelText, control);
        }

        private Panel CreateStatusField(string labelText, out ComboBox control)
        {
            control = new ComboBox();
            control.Dock = DockStyle.Top;
            control.Height = AppTheme.InputHeight;
            AppTheme.StyleComboBox(control);
            control.Items.AddRange(new object[] { "Chờ may", "Đang chờ NPL", "Đang chờ nhãn xanh", "Đã may" });
            return CreateFieldPanel(labelText, control);
        }

        private Panel CreateVersionField(out ComboBox control)
        {
            control = new ComboBox();
            control.Dock = DockStyle.Top;
            control.Height = AppTheme.InputHeight;
            AppTheme.StyleComboBox(control);
            for (int version = 1; version <= 100; version++)
            {
                control.Items.Add("V" + version.ToString(CultureInfo.InvariantCulture));
            }
            return CreateFieldPanel("Phiên bản", control);
        }

        private TextBox CreateFieldTextBox(string value, bool readOnly)
        {
            TextBox control = new TextBox();
            control.Dock = DockStyle.Top;
            control.Height = AppTheme.InputHeight;
            AppTheme.StyleTextBox(control, false);
            control.ReadOnly = readOnly;
            if (readOnly)
            {
                control.BackColor = AppTheme.NeutralBackground;
                control.TabStop = false;
            }
            control.Text = value;
            return control;
        }

        private Panel CreateFieldPanel(string labelText, Control control)
        {
            Panel field = new Panel();
            field.Dock = DockStyle.Fill;
            field.Margin = new Padding(0, 0, 12, 8);
            Label label = new Label();
            label.Text = labelText;
            label.Dock = DockStyle.Top;
            label.Height = 20;
            AppTheme.StyleLabel(label, false);
            field.Controls.Add(control);
            field.Controls.Add(label);
            return field;
        }

        private void UpdateContentLayout()
        {
            int width = Math.Max(0, contentScroll.ClientSize.Width - 48);
            contentHost.Width = width;
            contentHost.Height = updateCard.Bottom + 24;
            headerCard.Width = width;
            sourceCard.Width = width;
            updateCard.Width = width;
            contentScroll.AutoScrollMinSize = new Size(0, contentHost.Height + 24);
        }

        private void SetDatePicker(DateTimePicker picker, string value)
        {
            DateTime parsed;
            if (TryParseDate(value, out parsed))
            {
                picker.Value = parsed;
                picker.Checked = true;
            }
            else
            {
                picker.Value = DateTime.Today;
                picker.Checked = false;
            }
        }

        private void SaveSample(object sender, EventArgs e)
        {
            try
            {
                string selectedVersion = SampleNormalization.NormalizePhienBan(
                    phienBanComboBox.SelectedItem == null
                        ? String.Empty
                        : phienBanComboBox.SelectedItem.ToString());
                string currentVersion = SampleNormalization.NormalizePhienBan(source.PhienBan);
                bool versionChanged = !String.Equals(selectedVersion, currentVersion, StringComparison.OrdinalIgnoreCase);
                if (versionChanged)
                {
                    if (!CanConfirmVersionChange())
                    {
                        throw new InvalidOperationException("Phiên bản đã được xác nhận; không thể đổi lại.");
                    }

                    DialogResult confirmation = MessageBox.Show(
                        this,
                        "Xác nhận " + selectedVersion + "? Sample_ID sẽ đổi một lần.",
                        "Xác nhận phiên bản",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question);
                    if (confirmation != DialogResult.Yes)
                    {
                        return;
                    }
                }

                string contract = GetRequestValue("SoHopDong");
                SampleManagementRecord record = new SampleManagementRecord
                {
                    MauId = source.MauId,
                    YeuCauId = source.YeuCauId,
                    SampleId = versionChanged
                        ? SampleNormalization.BuildSampleId(contract, selectedVersion, source.SttMau)
                        : source.SampleId,
                    PhienBan = selectedVersion,
                    SttMau = source.SttMau,
                    NgayDuyet = GetDateValue(approvedDatePicker),
                    NgayHetHan = GetDateValue(expiryDatePicker),
                    NguoiDuyet = approverTextBox.Text.Trim(),
                    NoiLuu = storageTextBox.Text.Trim(),
                    NgayGiaoMau = GetDateValue(deliveredDatePicker),
                    GhiChu = noteTextBox.Text.Trim(),
                    TrangThaiMay = sampleStatusComboBox.SelectedItem == null
                        ? "Chờ may"
                        : sampleStatusComboBox.SelectedItem.ToString()
                };
                SampleManagementRecord readBack = repository.UpdateSampleAndReadBack(
                    record,
                    GetRequestValue("PhienBan"),
                    contract);
                cache.AddOrReplaceSample(readBack);
                LastReadBack = readBack;
                AppTheme.SetSuccessStatus(statusLabel, "Đã lưu");
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception exception)
            {
                AppTheme.SetErrorStatus(statusLabel, "Không thể lưu");
                MessageBox.Show(this, exception.Message, "Không thể lưu", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private string GetRequestValue(string propertyName)
        {
            SampleManagerCacheSnapshot snapshot = cache.Snapshot();
            for (int index = 0; index < snapshot.Requests.Count; index++)
            {
                if (!String.Equals(snapshot.Requests[index].YeuCauId, source.YeuCauId, StringComparison.Ordinal))
                {
                    continue;
                }
                if (propertyName == "SoHopDong") return snapshot.Requests[index].SoHopDong;
                if (propertyName == "MaVatTu") return snapshot.Requests[index].MaVatTu;
                if (propertyName == "TenTui") return snapshot.Requests[index].TenTui;
                if (propertyName == "PhienBan") return snapshot.Requests[index].PhienBan;
            }
            return String.Empty;
        }

        private bool CanConfirmVersionChange()
        {
            string requestVersion = SampleNormalization.NormalizePhienBan(GetRequestValue("PhienBan"));
            string currentVersion = SampleNormalization.NormalizePhienBan(source.PhienBan);
            return String.Equals(requestVersion, currentVersion, StringComparison.OrdinalIgnoreCase);
        }

        private static void SetVersion(ComboBox control, string value)
        {
            string normalized = SampleNormalization.NormalizePhienBan(value);
            control.SelectedItem = normalized;
            if (control.SelectedIndex < 0)
            {
                control.SelectedIndex = 0;
            }
        }

        private string GetQaDisplayName()
        {
            SampleManagerCacheSnapshot snapshot = cache.Snapshot();
            string qaId = String.Empty;
            for (int index = 0; index < snapshot.Requests.Count; index++)
            {
                if (String.Equals(snapshot.Requests[index].YeuCauId, source.YeuCauId, StringComparison.Ordinal))
                {
                    qaId = snapshot.Requests[index].QaId;
                    break;
                }
            }
            for (int index = 0; index < snapshot.QaOptions.Count; index++)
            {
                if (String.Equals(snapshot.QaOptions[index].Id, qaId, StringComparison.Ordinal))
                {
                    return snapshot.QaOptions[index].ToString();
                }
            }
            return "Chưa có tên QA";
        }

        private static string GetDateValue(DateTimePicker picker)
        {
            return picker.Checked
                ? picker.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : String.Empty;
        }

        private static bool TryParseDate(string value, out DateTime date)
        {
            string[] formats = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "dd/MM/yy", "dd/MM/yyyy" };
            return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                || DateTime.TryParse(value, out date);
        }

        private static string NormalizeSampleStatus(string value)
        {
            return SampleNormalization.NormalizeSampleStatus(value);
        }
    }
}
