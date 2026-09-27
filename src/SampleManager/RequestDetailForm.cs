using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SampleManager
{
    internal sealed class RequestDetailForm : Form
    {
        private readonly GoogleSheetsSampleRepository repository;
        private readonly SampleManagerCache cache;
        private readonly SampleRequestRecord source;
        private readonly TextBox contractTextBox;
        private readonly TextBox materialTextBox;
        private readonly TextBox bagTextBox;
        private readonly TextBox locationTextBox;
        private readonly NumericUpDown quantityNumeric;
        private readonly ComboBox phienBanComboBox;
        private readonly DateTimePicker deadlinePicker;
        private readonly ComboBox statusComboBox;
        private readonly TextBox noteTextBox;
        private readonly Label statusLabel;

        public SampleRequestRecord LastReadBack { get; private set; }

        public RequestDetailForm(
            GoogleSheetsSampleRepository repository,
            SampleManagerCache cache,
            SampleRequestRecord source)
        {
            this.repository = repository;
            this.cache = cache;
            this.source = source;
            AppTheme.ApplyForm(this);
            Text = "Chi tiết yêu cầu";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1000, 700);
            MinimumSize = new Size(1000, 700);

            Panel headerCard = AppTheme.CreateCard();
            headerCard.Location = new Point(24, 24);
            headerCard.Size = new Size(952, 76);
            Controls.Add(headerCard);

            Label title = new Label();
            title.Text = "Chi tiết yêu cầu";
            title.AutoSize = true;
            title.Location = new Point(24, 22);
            title.Font = AppTheme.TitleFont;
            title.ForeColor = AppTheme.Text;
            headerCard.Controls.Add(title);

            Panel sourceCard = AppTheme.CreateCard();
            sourceCard.Location = new Point(24, 116);
            sourceCard.Size = new Size(952, 210);
            Controls.Add(sourceCard);

            Label sourceTitle = new Label();
            sourceTitle.Text = "Thông tin yêu cầu";
            sourceTitle.AutoSize = true;
            sourceTitle.Location = new Point(24, 16);
            AppTheme.StyleLabel(sourceTitle, true);
            sourceCard.Controls.Add(sourceTitle);

            AddReadOnlyField(sourceCard, "Ngày tạo", FormatDate(source.NgayTaoYeuCau), 24, 48, 210);
            AddReadOnlyField(sourceCard, "QA", GetQaDisplayName(), 258, 48, 210);
            contractTextBox = AddTextBox(sourceCard, "Số hợp đồng", 492, 48, 210, false);
            materialTextBox = AddTextBox(sourceCard, "Mã vật tư", 726, 48, 202, false);
            contractTextBox.Text = source.SoHopDong;
            materialTextBox.Text = source.MaVatTu;
            AddReadOnlyField(sourceCard, "Nơi yêu cầu", source.NoiYeuCau, 24, 116, 210);
            quantityNumeric = AddNumeric(sourceCard, "Số lượng mẫu", 258, 116, 210);
            SetQuantity(source.SoLuongMau);
            phienBanComboBox = AddVersionCombo(sourceCard, "Phiên bản", 492, 116, 210);
            SetVersion(source.PhienBan);

            bool hasSamples = HasSamples();
            contractTextBox.ReadOnly = hasSamples;
            materialTextBox.ReadOnly = hasSamples;
            quantityNumeric.Enabled = !hasSamples;
            phienBanComboBox.Enabled = false;
            phienBanComboBox.BackColor = AppTheme.NeutralBackground;
            phienBanComboBox.TabStop = false;
            if (hasSamples)
            {
                contractTextBox.BackColor = AppTheme.NeutralBackground;
                materialTextBox.BackColor = AppTheme.NeutralBackground;
            }

            Panel updateCard = AppTheme.CreateCard();
            updateCard.Location = new Point(24, 350);
            updateCard.Size = new Size(952, 230);
            Controls.Add(updateCard);

            Label updateTitle = new Label();
            updateTitle.Text = "Cập nhật";
            updateTitle.AutoSize = true;
            updateTitle.Location = new Point(24, 16);
            AppTheme.StyleLabel(updateTitle, true);
            updateCard.Controls.Add(updateTitle);

            bagTextBox = AddTextBox(updateCard, "Tên túi", 24, 48, 210, false);
            locationTextBox = AddTextBox(updateCard, "Nơi yêu cầu", 258, 48, 210, false);
            deadlinePicker = AddDatePicker(updateCard, "Deadline", 492, 48, 210);
            statusComboBox = AddStatusCombo(updateCard, "Trạng thái", 726, 48, 202);
            noteTextBox = AddTextBox(updateCard, "Ghi chú", 24, 116, 904, true);

            bagTextBox.Text = source.TenTui;
            locationTextBox.Text = source.NoiYeuCau;
            SetDate(deadlinePicker, source.Deadline);
            statusComboBox.SelectedItem = SampleNormalization.GetRequestDisplayStatus(
                source,
                cache.Snapshot().Samples);
            if (statusComboBox.SelectedIndex < 0) statusComboBox.SelectedIndex = 0;
            statusComboBox.Enabled = false;
            statusComboBox.BackColor = AppTheme.NeutralBackground;
            statusComboBox.TabStop = false;
            noteTextBox.Text = source.GhiChu;

            Button saveButton = new Button();
            saveButton.Text = "Lưu";
            saveButton.Size = new Size(120, 38);
            saveButton.Location = new Point(24, 616);
            AppTheme.StylePrimaryButton(saveButton);
            saveButton.Click += SaveRequest;
            Controls.Add(saveButton);

            Button closeButton = new Button();
            closeButton.Text = "Đóng";
            closeButton.Size = new Size(120, 38);
            closeButton.Location = new Point(856, 616);
            AppTheme.StyleSecondaryButton(closeButton);
            closeButton.Click += delegate { Close(); };
            Controls.Add(closeButton);

            statusLabel = new Label();
            statusLabel.Location = new Point(166, 621);
            AppTheme.SetMutedStatus(statusLabel, "Chưa lưu");
            Controls.Add(statusLabel);
        }

        private TextBox AddTextBox(Panel parent, string labelText, int x, int y, int width, bool multiline)
        {
            AddFieldLabel(parent, labelText, x, y, width);
            TextBox control = new TextBox();
            control.Location = new Point(x, y + 22);
            control.Size = new Size(width, multiline ? 48 : AppTheme.InputHeight);
            AppTheme.StyleTextBox(control, multiline);
            parent.Controls.Add(control);
            return control;
        }

        private void AddReadOnlyField(Panel parent, string labelText, string value, int x, int y, int width)
        {
            TextBox control = AddTextBox(parent, labelText, x, y, width, false);
            control.ReadOnly = true;
            control.BackColor = AppTheme.NeutralBackground;
            control.Text = value;
        }

        private NumericUpDown AddNumeric(Panel parent, string labelText, int x, int y, int width)
        {
            AddFieldLabel(parent, labelText, x, y, width);
            NumericUpDown control = new NumericUpDown();
            control.Location = new Point(x, y + 22);
            control.Size = new Size(width, AppTheme.InputHeight);
            control.Minimum = 1;
            control.Maximum = 100000;
            AppTheme.StyleNumeric(control);
            parent.Controls.Add(control);
            return control;
        }

        private ComboBox AddVersionCombo(Panel parent, string labelText, int x, int y, int width)
        {
            AddFieldLabel(parent, labelText, x, y, width);
            ComboBox control = new ComboBox();
            control.Location = new Point(x, y + 22);
            control.Size = new Size(width, AppTheme.InputHeight);
            AppTheme.StyleComboBox(control);
            for (int version = 1; version <= 100; version++)
            {
                control.Items.Add("V" + version.ToString(CultureInfo.InvariantCulture));
            }
            parent.Controls.Add(control);
            return control;
        }

        private DateTimePicker AddDatePicker(Panel parent, string labelText, int x, int y, int width)
        {
            AddFieldLabel(parent, labelText, x, y, width);
            DateTimePicker control = new DateTimePicker();
            control.Location = new Point(x, y + 22);
            control.Size = new Size(width, AppTheme.InputHeight);
            AppTheme.StyleDatePicker(control);
            parent.Controls.Add(control);
            return control;
        }

        private ComboBox AddStatusCombo(Panel parent, string labelText, int x, int y, int width)
        {
            AddFieldLabel(parent, labelText, x, y, width);
            ComboBox control = new ComboBox();
            control.Location = new Point(x, y + 22);
            control.Size = new Size(width, AppTheme.InputHeight);
            AppTheme.StyleComboBox(control);
            control.Items.AddRange(new object[]
            {
                "Mới",
                "Chờ may",
                "Đang chờ NPL",
                "Đang chờ nhãn xanh",
                "Đã may",
                "Đã phân phối",
                "Đang may",
                "Đã giao",
                "Hoàn thành",
                "Hủy"
            });
            parent.Controls.Add(control);
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

        private void SaveRequest(object sender, EventArgs e)
        {
            try
            {
                string contract = SampleNormalization.NormalizeSoHopDong(contractTextBox.Text);
                string material = SampleNormalization.NormalizeMaVatTu(materialTextBox.Text);
                string phienBan = SampleNormalization.NormalizePhienBan(
                    phienBanComboBox.SelectedItem == null ? String.Empty : phienBanComboBox.SelectedItem.ToString());
                if (String.IsNullOrWhiteSpace(contract)) throw new InvalidOperationException("Số hợp đồng không được trống.");
                if (String.IsNullOrWhiteSpace(material)) throw new InvalidOperationException("Mã vật tư không được trống.");

                SampleRequestRecord record = new SampleRequestRecord
                {
                    YeuCauId = source.YeuCauId,
                    NgayTaoYeuCau = source.NgayTaoYeuCau,
                    SoHopDong = contract,
                    MaVatTu = material,
                    TenTui = bagTextBox.Text.Trim(),
                    QaId = source.QaId,
                    NoiYeuCau = locationTextBox.Text.Trim(),
                    SoLuongMau = quantityNumeric.Value.ToString(CultureInfo.InvariantCulture),
                    PhienBan = phienBan,
                    Deadline = deadlinePicker.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    TrangThai = source.TrangThai,
                    GhiChu = noteTextBox.Text.Trim()
                };
                SampleRequestRecord readBack = repository.UpdateRequestAndReadBack(record);
                cache.AddOrReplace(readBack);
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

        private bool HasSamples()
        {
            SampleManagerCacheSnapshot snapshot = cache.Snapshot();
            for (int index = 0; index < snapshot.Samples.Count; index++)
            {
                if (String.Equals(snapshot.Samples[index].YeuCauId, source.YeuCauId, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        private string GetQaDisplayName()
        {
            SampleManagerCacheSnapshot snapshot = cache.Snapshot();
            for (int index = 0; index < snapshot.QaOptions.Count; index++)
            {
                if (String.Equals(snapshot.QaOptions[index].Id, source.QaId, StringComparison.Ordinal)) return snapshot.QaOptions[index].ToString();
            }
            return "Chưa có tên QA";
        }

        private void SetQuantity(string value)
        {
            int quantity;
            if (Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out quantity) && quantity >= 1)
            {
                quantityNumeric.Value = Math.Min(quantity, (int)quantityNumeric.Maximum);
            }
            else
            {
                quantityNumeric.Value = 1;
            }
        }

        private void SetVersion(string value)
        {
            string normalized = SampleNormalization.NormalizePhienBan(value);
            phienBanComboBox.SelectedItem = normalized;
            if (phienBanComboBox.SelectedIndex < 0)
            {
                phienBanComboBox.SelectedIndex = 0;
            }
        }

        private static void SetDate(DateTimePicker picker, string value)
        {
            DateTime parsed;
            if (TryParseDate(value, out parsed)) picker.Value = parsed;
        }

        private static string FormatDate(string value)
        {
            DateTime parsed;
            return TryParseDate(value, out parsed) ? parsed.ToString("dd/MM/yy", CultureInfo.InvariantCulture) : value;
        }

        private static bool TryParseDate(string value, out DateTime date)
        {
            string[] formats = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "dd/MM/yy", "dd/MM/yyyy" };
            return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);
        }
    }
}
