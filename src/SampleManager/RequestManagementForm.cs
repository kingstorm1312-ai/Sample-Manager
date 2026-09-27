using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SampleManager
{
    internal sealed class RequestManagementForm : Form
    {
        private readonly GoogleSheetsSampleRepository repository;
        private readonly SampleManagerCache cache;
        private readonly QaOption qa;
        private readonly ComboBox periodComboBox;
        private readonly DateTimePicker fromPicker;
        private readonly DateTimePicker toPicker;
        private readonly ComboBox statusComboBox;
        private readonly TextBox searchTextBox;
        private readonly Label statusLabel;
        private readonly TabControl requestTabs;
        private readonly DataGridView waitingGrid;
        private readonly DataGridView completedGrid;
        private readonly Label waitingEmptyLabel;
        private readonly Label completedEmptyLabel;
        private readonly Button refreshButton;
        private readonly Button distributeButton;
        private readonly Button cancelButton;
        private bool settingThisMonth;
        private IList<SampleRequestRecord> records;

        public RequestManagementForm(
            GoogleSheetsSampleRepository repository,
            SampleManagerCache cache,
            QaOption qa)
        {
            this.repository = repository;
            this.cache = cache;
            this.qa = qa;
            AppTheme.ApplyForm(this);
            Text = "Quản lý yêu cầu mẫu";
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(1180, 760);
            MinimumSize = new Size(1180, 760);

            Panel headerCard = AppTheme.CreateCard();
            headerCard.Location = new Point(24, 24);
            headerCard.Size = new Size(1132, 88);
            Controls.Add(headerCard);

            Label eyebrow = new Label();
            eyebrow.Text = "SAMPLE MANAGER  /  REQUESTS";
            eyebrow.AutoSize = true;
            eyebrow.Location = new Point(24, 14);
            eyebrow.Font = AppTheme.SmallFont;
            eyebrow.ForeColor = AppTheme.Accent;
            headerCard.Controls.Add(eyebrow);

            Label title = new Label();
            title.Text = "Quản lý yêu cầu mẫu";
            title.AutoSize = true;
            title.Location = new Point(22, 34);
            title.Font = AppTheme.TitleFont;
            title.ForeColor = AppTheme.Text;
            headerCard.Controls.Add(title);

            Label qaLabel = new Label();
            qaLabel.Text = "QA: " + qa.ToString();
            qaLabel.AutoSize = true;
            qaLabel.Location = new Point(470, 40);
            qaLabel.Font = AppTheme.BodyFont;
            qaLabel.ForeColor = AppTheme.Muted;
            headerCard.Controls.Add(qaLabel);

            statusLabel = new Label();
            statusLabel.Location = new Point(860, 30);
            AppTheme.SetMutedStatus(statusLabel, "Đang tải...");
            headerCard.Controls.Add(statusLabel);

            Panel filterCard = AppTheme.CreateCard();
            filterCard.Location = new Point(24, 128);
            filterCard.Size = new Size(1132, 176);
            Controls.Add(filterCard);

            Label filterTitle = new Label();
            filterTitle.Text = "Bộ lọc";
            filterTitle.AutoSize = true;
            filterTitle.Location = new Point(24, 16);
            AppTheme.StyleLabel(filterTitle, true);
            filterCard.Controls.Add(filterTitle);

            periodComboBox = AddCombo(filterCard, "Thời gian", 24, 48, 180);
            periodComboBox.Items.Add("Tháng này");
            periodComboBox.Items.Add("Tùy chọn");
            periodComboBox.SelectedIndex = 0;

            fromPicker = AddDatePicker(filterCard, "Từ ngày", 228, 48, 150);
            toPicker = AddDatePicker(filterCard, "Đến ngày", 402, 48, 150);

            statusComboBox = AddCombo(filterCard, "Trạng thái", 576, 48, 180);
            statusComboBox.Items.AddRange(new object[]
            {
                "Tất cả",
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
            statusComboBox.SelectedIndex = 0;

            searchTextBox = AddTextBox(filterCard, "Tìm nhanh", 780, 48, 328);

            Button resetButton = new Button();
            resetButton.Text = "Xóa lọc / Tháng này";
            resetButton.Size = new Size(180, 38);
            resetButton.Location = new Point(24, 122);
            AppTheme.StyleSecondaryButton(resetButton);
            resetButton.Click += ResetFilters;
            filterCard.Controls.Add(resetButton);

            refreshButton = new Button();
            refreshButton.Text = "Làm mới dữ liệu";
            refreshButton.Size = new Size(160, 38);
            refreshButton.Location = new Point(220, 122);
            AppTheme.StyleSecondaryButton(refreshButton);
            refreshButton.Click += RefreshLiveData;
            filterCard.Controls.Add(refreshButton);

            Panel gridCard = AppTheme.CreateCard();
            gridCard.Location = new Point(24, 324);
            gridCard.Size = new Size(1132, 410);
            Controls.Add(gridCard);

            Label gridTitle = new Label();
            gridTitle.Text = "Danh sách yêu cầu";
            gridTitle.AutoSize = true;
            gridTitle.Location = new Point(24, 18);
            AppTheme.StyleLabel(gridTitle, true);
            gridCard.Controls.Add(gridTitle);

            requestTabs = new TabControl();
            requestTabs.Location = new Point(24, 52);
            requestTabs.Size = new Size(1084, 286);
            requestTabs.SelectedIndexChanged += delegate
            {
                UpdateCancelButton();
                UpdateDistributeButton();
            };

            TabPage waitingPage = new TabPage("Mẫu đang đợi may");
            TabPage completedPage = new TabPage("Mẫu đã may xong");
            waitingPage.Padding = new Padding(4);
            completedPage.Padding = new Padding(4);

            waitingGrid = CreateRequestGrid();
            completedGrid = CreateRequestGrid();
            waitingEmptyLabel = CreateEmptyLabel();
            completedEmptyLabel = CreateEmptyLabel();
            waitingPage.Controls.Add(waitingGrid);
            waitingPage.Controls.Add(waitingEmptyLabel);
            completedPage.Controls.Add(completedGrid);
            completedPage.Controls.Add(completedEmptyLabel);
            requestTabs.TabPages.Add(waitingPage);
            requestTabs.TabPages.Add(completedPage);
            gridCard.Controls.Add(requestTabs);

            distributeButton = new Button();
            distributeButton.Text = "Đã phân phối";
            distributeButton.Size = new Size(160, 38);
            distributeButton.Location = new Point(596, 354);
            AppTheme.StylePrimaryButton(distributeButton);
            distributeButton.Enabled = false;
            distributeButton.Click += MarkSelectedAsDistributed;
            gridCard.Controls.Add(distributeButton);

            cancelButton = new Button();
            cancelButton.Text = "Hủy yêu cầu";
            cancelButton.Size = new Size(160, 38);
            cancelButton.Location = new Point(772, 354);
            AppTheme.StyleSecondaryButton(cancelButton);
            cancelButton.Enabled = false;
            cancelButton.Click += CancelSelectedRequest;
            gridCard.Controls.Add(cancelButton);

            Button closeButton = new Button();
            closeButton.Text = "Đóng / Quay lại";
            closeButton.Size = new Size(160, 38);
            closeButton.Location = new Point(948, 354);
            AppTheme.StyleSecondaryButton(closeButton);
            closeButton.Click += delegate { Close(); };
            gridCard.Controls.Add(closeButton);

            periodComboBox.SelectedIndexChanged += FiltersChanged;
            fromPicker.ValueChanged += FiltersChanged;
            toPicker.ValueChanged += FiltersChanged;
            statusComboBox.SelectedIndexChanged += FiltersChanged;
            searchTextBox.TextChanged += FiltersChanged;
            cache.Changed += CacheChanged;
            FormClosed += delegate { cache.Changed -= CacheChanged; };
            Shown += LoadCachedRequests;

            SetThisMonth();
        }

        private ComboBox AddCombo(Panel parent, string labelText, int x, int y, int width)
        {
            AddFieldLabel(parent, labelText, x, y, width);
            ComboBox control = new ComboBox();
            control.Location = new Point(x, y + 22);
            control.Size = new Size(width, AppTheme.InputHeight);
            AppTheme.StyleComboBox(control);
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

        private TextBox AddTextBox(Panel parent, string labelText, int x, int y, int width)
        {
            AddFieldLabel(parent, labelText, x, y, width);
            TextBox control = new TextBox();
            control.Location = new Point(x, y + 22);
            control.Size = new Size(width, AppTheme.InputHeight);
            AppTheme.StyleTextBox(control, false);
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

        private DataGridView CreateRequestGrid()
        {
            DataGridView targetGrid = new DataGridView();
            targetGrid.Dock = DockStyle.Fill;
            targetGrid.AllowUserToAddRows = false;
            targetGrid.AllowUserToDeleteRows = false;
            targetGrid.ReadOnly = true;
            targetGrid.AutoGenerateColumns = false;
            targetGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            targetGrid.MultiSelect = false;
            AppTheme.StyleGrid(targetGrid);
            AddGridColumns(targetGrid);
            targetGrid.CellDoubleClick += OpenRequestDetail;
            targetGrid.SelectionChanged += RequestSelectionChanged;
            return targetGrid;
        }

        private Label CreateEmptyLabel()
        {
            Label label = new Label();
            label.Text = "Không có dữ liệu";
            label.Dock = DockStyle.Fill;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Font = AppTheme.BodyFont;
            label.ForeColor = AppTheme.Muted;
            label.BackColor = AppTheme.Card;
            label.Visible = false;
            return label;
        }

        private static void ShowLoadingState(DataGridView targetGrid, Label targetEmptyLabel)
        {
            targetGrid.Rows.Clear();
            targetGrid.Visible = false;
            targetEmptyLabel.Text = "Đang tải dữ liệu";
            targetEmptyLabel.Visible = true;
        }

        private void AddGridColumns(DataGridView targetGrid)
        {
            AddGridColumn(targetGrid, "Ngày YC", 90);
            AddGridColumn(targetGrid, "Hợp đồng", 120);
            AddGridColumn(targetGrid, "Mã VT", 110);
            AddGridColumn(targetGrid, "Tên túi", 150);
            AddGridColumn(targetGrid, "Nơi YC", 180);
            AddGridColumn(targetGrid, "SL", 60);
            AddGridColumn(targetGrid, "Deadline", 95);
            AddGridColumn(targetGrid, "Trạng thái", 100);
        }

        private void AddGridColumn(DataGridView targetGrid, string title, float fillWeight)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.HeaderText = title;
            column.Name = title;
            column.FillWeight = fillWeight;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            targetGrid.Columns.Add(column);
        }

        private void SetThisMonth()
        {
            DateTime first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            DateTime last = first.AddMonths(1).AddDays(-1);
            settingThisMonth = true;
            try
            {
                fromPicker.Value = first;
                toPicker.Value = last;
                periodComboBox.SelectedIndex = 0;
            }
            finally
            {
                settingThisMonth = false;
            }
        }

        private void LoadCachedRequests(object sender, EventArgs e)
        {
            SampleManagerCacheSnapshot snapshot = cache.Snapshot();
            if (!snapshot.IsLoaded)
            {
                records = new List<SampleRequestRecord>();
                ShowLoadingState(waitingGrid, waitingEmptyLabel);
                ShowLoadingState(completedGrid, completedEmptyLabel);
                UpdateCancelButton();
                UpdateDistributeButton();
                AppTheme.SetMutedStatus(statusLabel, "Đang tải dữ liệu");
                return;
            }
            records = FilterByQa(snapshot.Requests);
            ApplyFilters();
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
                    BeginInvoke((MethodInvoker)delegate { LoadCachedRequests(null, EventArgs.Empty); });
                }
                else
                {
                    LoadCachedRequests(null, EventArgs.Empty);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        private IList<SampleRequestRecord> FilterByQa(IList<SampleRequestRecord> source)
        {
            IList<SampleRequestRecord> filtered = new List<SampleRequestRecord>();
            for (int index = 0; index < source.Count; index++)
            {
                if (String.Equals(source[index].QaId, qa.Id, StringComparison.Ordinal))
                {
                    filtered.Add(source[index]);
                }
            }
            return filtered;
        }

        private void RefreshLiveData(object sender, EventArgs e)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                IList<QaOption> liveQaOptions = repository.ReadActiveQaOptions();
                IList<SampleRequestRecord> liveRequests = repository.ReadAllRequests();
                IList<SampleManagementRecord> liveSamples = repository.ReadAllSamples();
                cache.ReplaceLive(liveQaOptions, liveRequests, liveSamples);
                records = FilterByQa(cache.Snapshot().Requests);
                ApplyFilters();
            }
            catch (Exception exception)
            {
                records = FilterByQa(cache.Snapshot().Requests);
                ApplyFilters();
                AppTheme.SetErrorStatus(statusLabel, "Làm mới thất bại");
                MessageBox.Show(
                    this,
                    exception.Message,
                    "Không thể làm mới dữ liệu",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void FiltersChanged(object sender, EventArgs e)
        {
            if (settingThisMonth)
            {
                return;
            }
            if (periodComboBox.SelectedIndex == 0 && sender == periodComboBox)
            {
                SetThisMonth();
            }
            else if (sender == fromPicker || sender == toPicker)
            {
                if (periodComboBox.SelectedIndex != 1)
                {
                    periodComboBox.SelectedIndex = 1;
                }
            }
            ApplyFilters();
        }

        private void ResetFilters(object sender, EventArgs e)
        {
            searchTextBox.Clear();
            statusComboBox.SelectedIndex = 0;
            SetThisMonth();
            ApplyFilters();
        }

        private void ApplyFilters()
        {
            if (waitingGrid == null || completedGrid == null)
            {
                return;
            }

            DateTime from = fromPicker.Value.Date;
            DateTime to = toPicker.Value.Date;
            if (periodComboBox.SelectedIndex == 0)
            {
                DateTime first = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
                from = first;
                to = first.AddMonths(1).AddDays(-1);
            }

            string status = statusComboBox.SelectedItem == null ? "Tất cả" : statusComboBox.SelectedItem.ToString();
            string search = searchTextBox.Text.Trim();
            SampleManagerCacheSnapshot snapshot = cache.Snapshot();
            IList<SampleRequestRecord> filtered = new List<SampleRequestRecord>();
            if (records != null)
            {
                for (int index = 0; index < records.Count; index++)
                {
                    SampleRequestRecord record = records[index];
                    DateTime created;
                    if (!TryParseDate(record.NgayTaoYeuCau, out created) || created.Date < from || created.Date > to)
                    {
                        continue;
                    }
                    string displayStatus = SampleNormalization.GetRequestDisplayStatus(record, snapshot.Samples);
                    if (status != "Tất cả" && !String.Equals(displayStatus, status, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
                    if (!String.IsNullOrWhiteSpace(search)
                        && !Contains(record.SoHopDong, search)
                        && !Contains(record.MaVatTu, search)
                        && !Contains(record.TenTui, search))
                    {
                        continue;
                    }
                    filtered.Add(record);
                }
            }

            filtered = SortNewestFirst(filtered);
            IList<SampleRequestRecord> waiting = new List<SampleRequestRecord>();
            IList<SampleRequestRecord> completed = new List<SampleRequestRecord>();
            for (int index = 0; index < filtered.Count; index++)
            {
                if (IsCompletedRequest(filtered[index], snapshot.Samples))
                {
                    completed.Add(filtered[index]);
                }
                else
                {
                    waiting.Add(filtered[index]);
                }
            }

            RenderRows(waitingGrid, waitingEmptyLabel, waiting, snapshot.Samples);
            RenderRows(completedGrid, completedEmptyLabel, completed, snapshot.Samples);
            SetStatusSummary(filtered.Count);
            UpdateCancelButton();
            UpdateDistributeButton();
        }

        private static IList<SampleRequestRecord> SortNewestFirst(IList<SampleRequestRecord> source)
        {
            List<SampleRequestRecord> sorted = new List<SampleRequestRecord>();
            for (int index = 0; index < source.Count; index++)
            {
                sorted.Add(source[index]);
            }
            sorted.Sort(delegate(SampleRequestRecord left, SampleRequestRecord right)
            {
                DateTime leftDate;
                DateTime rightDate;
                TryParseDate(left.NgayTaoYeuCau, out leftDate);
                TryParseDate(right.NgayTaoYeuCau, out rightDate);
                return rightDate.CompareTo(leftDate);
            });
            return sorted;
        }

        private static bool IsCompletedRequest(
            SampleRequestRecord record,
            IList<SampleManagementRecord> samples)
        {
            string status = SampleNormalization.GetRequestDisplayStatus(record, samples);
            return status == "Đã may"
                || status == "Đã phân phối"
                || status == "Đã giao"
                || status == "Hoàn thành";
        }

        private void RenderRows(
            DataGridView targetGrid,
            Label targetEmptyLabel,
            IList<SampleRequestRecord> filtered,
            IList<SampleManagementRecord> samples)
        {
            targetGrid.Rows.Clear();
            if (filtered.Count == 0)
            {
                targetGrid.Visible = false;
                targetEmptyLabel.Text = "Không có dữ liệu";
                targetEmptyLabel.Visible = true;
                return;
            }

            for (int index = 0; index < filtered.Count; index++)
            {
                SampleRequestRecord record = filtered[index];
                int rowIndex = targetGrid.Rows.Add(
                    FormatDate(record.NgayTaoYeuCau),
                    record.SoHopDong,
                    record.MaVatTu,
                    record.TenTui,
                    record.NoiYeuCau,
                    record.SoLuongMau,
                    FormatDate(record.Deadline),
                    SampleNormalization.GetRequestDisplayStatus(record, samples));
                targetGrid.Rows[rowIndex].Tag = record;
            }
            targetEmptyLabel.Visible = false;
            targetGrid.Visible = true;
            if (targetGrid.Rows.Count > 0)
            {
                targetGrid.Rows[0].Selected = true;
            }
        }

        private void RequestSelectionChanged(object sender, EventArgs e)
        {
            UpdateCancelButton();
            UpdateDistributeButton();
        }

        private DataGridView GetActiveGrid()
        {
            return requestTabs.SelectedIndex == 1 ? completedGrid : waitingGrid;
        }

        private void UpdateCancelButton()
        {
            if (cancelButton == null)
            {
                return;
            }

            SampleRequestRecord selected = GetSelectedRequest();
            cancelButton.Enabled = selected != null
                && !String.Equals(selected.TrangThai, "Hủy", StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateDistributeButton()
        {
            if (distributeButton == null)
            {
                return;
            }

            SampleRequestRecord selected = GetSelectedRequest();
            SampleManagerCacheSnapshot snapshot = cache.Snapshot();
            distributeButton.Enabled = selected != null
                && requestTabs.SelectedIndex == 1
                && SampleNormalization.GetRequestDisplayStatus(selected, snapshot.Samples) == "Đã may";
        }

        private SampleRequestRecord GetSelectedRequest()
        {
            DataGridView activeGrid = GetActiveGrid();
            if (activeGrid == null || activeGrid.SelectedRows.Count == 0)
            {
                return null;
            }
            return activeGrid.SelectedRows[0].Tag as SampleRequestRecord;
        }

        private void CancelSelectedRequest(object sender, EventArgs e)
        {
            SampleRequestRecord selected = GetSelectedRequest();
            if (selected == null)
            {
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                this,
                "Hủy yêu cầu " + selected.SoHopDong + "?",
                "Hủy yêu cầu",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirmation != DialogResult.Yes)
            {
                return;
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                SampleRequestRecord canceled = new SampleRequestRecord
                {
                    YeuCauId = selected.YeuCauId,
                    NgayTaoYeuCau = selected.NgayTaoYeuCau,
                    SoHopDong = selected.SoHopDong,
                    MaVatTu = selected.MaVatTu,
                    TenTui = selected.TenTui,
                    QaId = selected.QaId,
                    NoiYeuCau = selected.NoiYeuCau,
                    SoLuongMau = selected.SoLuongMau,
                    PhienBan = selected.PhienBan,
                    Deadline = selected.Deadline,
                    TrangThai = "Hủy",
                    GhiChu = selected.GhiChu
                };
                SampleRequestRecord readBack = repository.UpdateRequestAndReadBack(canceled);
                cache.AddOrReplace(readBack);
                records = FilterByQa(cache.Snapshot().Requests);
                ApplyFilters();
                AppTheme.SetSuccessStatus(statusLabel, "Đã hủy");
            }
            catch (Exception exception)
            {
                AppTheme.SetErrorStatus(statusLabel, "Hủy thất bại");
                MessageBox.Show(
                    this,
                    exception.Message,
                    "Không thể hủy yêu cầu",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void MarkSelectedAsDistributed(object sender, EventArgs e)
        {
            SampleRequestRecord selected = GetSelectedRequest();
            if (selected == null)
            {
                return;
            }

            SampleManagerCacheSnapshot snapshot = cache.Snapshot();
            if (SampleNormalization.GetRequestDisplayStatus(selected, snapshot.Samples) != "Đã may")
            {
                return;
            }

            string distributionPlace = (selected.NoiYeuCau ?? String.Empty).Trim();
            if (String.IsNullOrWhiteSpace(distributionPlace))
            {
                AppTheme.SetErrorStatus(statusLabel, "Thiếu nơi yêu cầu");
                MessageBox.Show(
                    this,
                    "Chưa có nơi yêu cầu để phân phối.",
                    "Không thể phân phối",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                this,
                "Xác nhận phân phối cho " + distributionPlace + "?",
                "Đã phân phối",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirmation != DialogResult.Yes)
            {
                return;
            }

            try
            {
                Cursor = Cursors.WaitCursor;
                SampleRequestRecord distributed = new SampleRequestRecord
                {
                    YeuCauId = selected.YeuCauId,
                    NgayTaoYeuCau = selected.NgayTaoYeuCau,
                    SoHopDong = selected.SoHopDong,
                    MaVatTu = selected.MaVatTu,
                    TenTui = selected.TenTui,
                    QaId = selected.QaId,
                    NoiYeuCau = selected.NoiYeuCau,
                    SoLuongMau = selected.SoLuongMau,
                    PhienBan = selected.PhienBan,
                    Deadline = selected.Deadline,
                    TrangThai = "Đã phân phối",
                    GhiChu = selected.GhiChu
                };
                SampleRequestRecord readBack = repository.UpdateRequestAndReadBack(distributed);
                cache.AddOrReplace(readBack);
                records = FilterByQa(cache.Snapshot().Requests);
                ApplyFilters();
                AppTheme.SetSuccessStatus(statusLabel, "Đã phân phối");
            }
            catch (Exception exception)
            {
                AppTheme.SetErrorStatus(statusLabel, "Phân phối thất bại");
                MessageBox.Show(
                    this,
                    exception.Message,
                    "Không thể phân phối",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private void OpenRequestDetail(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
            {
                return;
            }
            DataGridView sourceGrid = sender as DataGridView;
            if (sourceGrid == null)
            {
                return;
            }
            SampleRequestRecord record = sourceGrid.Rows[e.RowIndex].Tag as SampleRequestRecord;
            if (record == null)
            {
                return;
            }
            using (RequestDetailForm form = new RequestDetailForm(repository, cache, record))
            {
                form.ShowDialog(this);
                if (form.LastReadBack != null)
                {
                    records = FilterByQa(cache.Snapshot().Requests);
                    ApplyFilters();
                }
            }
        }

        private void SetStatusSummary(int count)
        {
            SampleManagerCacheSnapshot snapshot = cache.Snapshot();
            string updated = snapshot.IsLoaded
                ? " · Cập nhật " + snapshot.UpdatedAtLocal.ToString("dd/MM/yy HH:mm:ss", CultureInfo.InvariantCulture)
                : String.Empty;
            AppTheme.SetMutedStatus(statusLabel, count + " yêu cầu" + updated);
        }

        private static bool Contains(string value, string search)
        {
            return !String.IsNullOrEmpty(value)
                && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string FormatDate(string value)
        {
            DateTime date;
            if (TryParseDate(value, out date))
            {
                return date.ToString("dd/MM/yy", CultureInfo.InvariantCulture);
            }
            return value;
        }

        private static bool TryParseDate(string value, out DateTime date)
        {
            string[] formats = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "dd/MM/yy", "dd/MM/yyyy" };
            return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);
        }
    }
}
