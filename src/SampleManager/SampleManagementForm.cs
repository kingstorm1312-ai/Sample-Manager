using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace SampleManager
{
    internal sealed class SampleManagementForm : Form
    {
        private readonly GoogleSheetsSampleRepository repository;
        private readonly SampleManagerCache cache;
        private readonly ComboBox qaFilter;
        private readonly TextBox contractFilterTextBox;
        private readonly TextBox materialFilterTextBox;
        private readonly DateTimePicker requestFromPicker;
        private readonly DateTimePicker requestToPicker;
        private readonly DateTimePicker deadlineFromPicker;
        private readonly DateTimePicker deadlineToPicker;
        private readonly TextBox searchTextBox;
        private readonly Button filterButton;
        private readonly Panel filterPanel;
        private readonly ToolStripDropDown filterPopup;
        private readonly DataGridView queueGrid;
        private readonly DataGridView completedGrid;
        private readonly Label queueEmptyLabel;
        private readonly Label completedEmptyLabel;
        private readonly Label statusLabel;
        private readonly Label waitingCountLabel;
        private readonly Label completedCountLabel;
        private readonly Label totalCountLabel;
        private readonly Button detailButton;
        private readonly TabControl sampleTabs;
        private SampleManagementRecord selectedSample;
        private bool updatingFilters;

        public SampleManagementForm(
            GoogleSheetsSampleRepository repository,
            SampleManagerCache cache)
        {
            this.repository = repository;
            this.cache = cache;
            AppTheme.ApplyForm(this);
            Text = "Quản lý mẫu";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ClientSize = new Size(1400, 800);
            MinimumSize = new Size(1100, 600);
            WindowState = FormWindowState.Maximized;

            Panel contentPanel = new Panel();
            contentPanel.Dock = DockStyle.Fill;
            contentPanel.Padding = new Padding(16, 8, 16, 16);
            contentPanel.BackColor = AppTheme.Background;
            Controls.Add(contentPanel);

            filterPanel = new Panel();
            filterPanel.Size = new Size(620, 208);
            filterPanel.BackColor = AppTheme.Card;
            filterPanel.BorderStyle = BorderStyle.FixedSingle;

            filterPopup = new ToolStripDropDown();
            filterPopup.AutoSize = false;
            filterPopup.Size = filterPanel.Size;
            filterPopup.Padding = Padding.Empty;
            filterPopup.AutoClose = true;
            filterPopup.DropShadowEnabled = true;
            ToolStripControlHost filterHost = new ToolStripControlHost(filterPanel);
            filterHost.AutoSize = false;
            filterHost.Size = filterPanel.Size;
            filterHost.Margin = Padding.Empty;
            filterHost.Padding = Padding.Empty;
            filterPopup.Items.Add(filterHost);

            Panel toolbar = new Panel();
            toolbar.Dock = DockStyle.Top;
            toolbar.Height = 60;
            toolbar.BackColor = AppTheme.Background;
            Controls.Add(toolbar);

            FlowLayoutPanel headerPanel = new FlowLayoutPanel();
            headerPanel.Dock = DockStyle.Fill;
            headerPanel.Height = 60;
            headerPanel.FlowDirection = FlowDirection.LeftToRight;
            headerPanel.WrapContents = false;
            headerPanel.Padding = new Padding(24, 11, 0, 0);
            headerPanel.BackColor = AppTheme.Background;
            toolbar.Controls.Add(headerPanel);

            Label title = new Label();
            title.Text = "Quản lý mẫu";
            title.AutoSize = false;
            title.Size = new Size(176, 38);
            title.Margin = new Padding(0, 0, 8, 0);
            title.TextAlign = ContentAlignment.MiddleLeft;
            title.Font = AppTheme.TitleFont;
            title.ForeColor = AppTheme.Text;
            headerPanel.Controls.Add(title);

            waitingCountLabel = CreateCounter("Chờ may: 0", 90);
            completedCountLabel = CreateCounter("Đã may: 0", 80);
            totalCountLabel = CreateCounter("Tổng: 0", 60);
            headerPanel.Controls.Add(waitingCountLabel);
            headerPanel.Controls.Add(completedCountLabel);
            headerPanel.Controls.Add(totalCountLabel);

            statusLabel = new Label();
            statusLabel.Location = new Point(500, 11);
            AppTheme.SetMutedStatus(statusLabel, String.Empty);
            toolbar.Controls.Add(statusLabel);

            FlowLayoutPanel actionPanel = new FlowLayoutPanel();
            actionPanel.Dock = DockStyle.Right;
            actionPanel.Width = 630;
            actionPanel.Height = 60;
            actionPanel.FlowDirection = FlowDirection.LeftToRight;
            actionPanel.WrapContents = false;
            actionPanel.Padding = new Padding(0, 12, 12, 0);
            actionPanel.BackColor = AppTheme.Background;
            toolbar.Controls.Add(actionPanel);

            Label searchLabel = new Label();
            searchLabel.Text = "Tìm nhanh";
            searchLabel.AutoSize = false;
            searchLabel.Size = new Size(64, 36);
            searchLabel.TextAlign = ContentAlignment.MiddleLeft;
            searchLabel.Margin = new Padding(0, 0, 4, 0);
            AppTheme.StyleLabel(searchLabel, false);
            actionPanel.Controls.Add(searchLabel);

            searchTextBox = new TextBox();
            searchTextBox.AutoSize = false;
            searchTextBox.Size = new Size(150, 38);
            searchTextBox.Margin = new Padding(0, 0, 8, 0);
            AppTheme.StyleTextBox(searchTextBox, false);
            actionPanel.Controls.Add(searchTextBox);

            filterButton = new Button();
            filterButton.Text = "Lọc";
            filterButton.Size = new Size(76, 36);
            filterButton.Margin = new Padding(0, 0, 8, 0);
            AppTheme.StyleSecondaryButton(filterButton);
            filterButton.Height = 36;
            filterButton.Click += ToggleFilterPanel;
            actionPanel.Controls.Add(filterButton);

            Button refreshButton = new Button();
            refreshButton.Text = "Làm mới dữ liệu";
            refreshButton.Size = new Size(124, 36);
            refreshButton.Margin = new Padding(0, 0, 8, 0);
            AppTheme.StyleSecondaryButton(refreshButton);
            refreshButton.Height = 36;
            refreshButton.Click += RefreshLiveData;
            actionPanel.Controls.Add(refreshButton);

            detailButton = new Button();
            detailButton.Text = "Chi tiết";
            detailButton.Size = new Size(78, 36);
            detailButton.Margin = new Padding(0, 0, 8, 0);
            detailButton.Enabled = false;
            AppTheme.StyleSecondaryButton(detailButton);
            detailButton.Height = 36;
            detailButton.Click += OpenSelectedSample;
            actionPanel.Controls.Add(detailButton);

            Button closeButton = new Button();
            closeButton.Text = "Đóng";
            closeButton.Size = new Size(72, 36);
            closeButton.Margin = new Padding(0);
            AppTheme.StyleSecondaryButton(closeButton);
            closeButton.Height = 36;
            closeButton.Click += delegate { Close(); };
            actionPanel.Controls.Add(closeButton);

            TableLayoutPanel filterTable = new TableLayoutPanel();
            filterTable.Dock = DockStyle.Fill;
            filterTable.Padding = new Padding(12, 8, 12, 8);
            filterTable.ColumnCount = 3;
            filterTable.RowCount = 3;
            for (int column = 0; column < 3; column++)
            {
                filterTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333F));
            }
            for (int row = 0; row < 3; row++)
            {
                filterTable.RowStyles.Add(new RowStyle(SizeType.Percent, 33.333F));
            }
            filterPanel.Controls.Add(filterTable);

            contractFilterTextBox = CreateFilterTextBox();
            materialFilterTextBox = CreateFilterTextBox();
            qaFilter = CreateFilterComboBox();
            requestFromPicker = CreateFilterDatePicker();
            requestToPicker = CreateFilterDatePicker();
            deadlineFromPicker = CreateFilterDatePicker();
            deadlineToPicker = CreateFilterDatePicker();

            filterTable.Controls.Add(CreateFilterField("Hợp đồng", contractFilterTextBox), 0, 0);
            filterTable.Controls.Add(CreateFilterField("Mã vật tư", materialFilterTextBox), 1, 0);
            filterTable.Controls.Add(CreateFilterField("QA", qaFilter), 2, 0);
            filterTable.Controls.Add(CreateFilterField("Từ ngày yêu cầu", requestFromPicker), 0, 1);
            filterTable.Controls.Add(CreateFilterField("Đến ngày yêu cầu", requestToPicker), 1, 1);
            filterTable.Controls.Add(CreateFilterField("Từ deadline", deadlineFromPicker), 2, 1);
            filterTable.Controls.Add(CreateFilterField("Đến deadline", deadlineToPicker), 0, 2);

            FlowLayoutPanel filterActions = new FlowLayoutPanel();
            filterActions.Dock = DockStyle.Fill;
            filterActions.FlowDirection = FlowDirection.LeftToRight;
            filterActions.WrapContents = false;
            filterActions.Padding = new Padding(4, 18, 0, 0);
            filterActions.Margin = new Padding(0, 0, 8, 6);
            Button applyButton = new Button();
            applyButton.Text = "Áp dụng";
            applyButton.Size = new Size(96, 38);
            AppTheme.StylePrimaryButton(applyButton);
            applyButton.Click += ApplyFiltersAndClose;
            filterActions.Controls.Add(applyButton);
            Button clearButton = new Button();
            clearButton.Text = "Xóa lọc";
            clearButton.Size = new Size(88, 38);
            clearButton.Margin = new Padding(8, 0, 0, 0);
            AppTheme.StyleSecondaryButton(clearButton);
            clearButton.Click += ClearFilters;
            filterActions.Controls.Add(clearButton);
            filterTable.Controls.Add(filterActions, 2, 2);

            sampleTabs = new TabControl();
            sampleTabs.Dock = DockStyle.Fill;
            TabPage queuePage = new TabPage("Hàng chờ");
            TabPage completedPage = new TabPage("Đã may");
            queuePage.Padding = new Padding(8);
            completedPage.Padding = new Padding(8);
            sampleTabs.TabPages.Add(queuePage);
            sampleTabs.TabPages.Add(completedPage);
            queueGrid = CreateGrid();
            completedGrid = CreateGrid();
            AddSampleColumns(queueGrid);
            AddSampleColumns(completedGrid);
            queueGrid.SelectionChanged += SampleSelectionChanged;
            completedGrid.SelectionChanged += SampleSelectionChanged;
            queueGrid.CellDoubleClick += SampleDoubleClick;
            completedGrid.CellDoubleClick += SampleDoubleClick;
            queuePage.Controls.Add(queueGrid);
            completedPage.Controls.Add(completedGrid);
            queueEmptyLabel = CreateEmptyLabel("Không có mẫu");
            completedEmptyLabel = CreateEmptyLabel("Không có mẫu");
            queuePage.Controls.Add(queueEmptyLabel);
            completedPage.Controls.Add(completedEmptyLabel);
            contentPanel.Controls.Add(sampleTabs);

            searchTextBox.TextChanged += QuickSearchChanged;
            cache.Changed += CacheChanged;
            FormClosed += delegate { cache.Changed -= CacheChanged; };
            Shown += LoadCachedData;
        }

        private Label CreateCounter(string text, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Size = new Size(width, 38);
            label.Margin = new Padding(0, 0, 8, 0);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.Font = AppTheme.SectionFont;
            label.ForeColor = AppTheme.Text;
            return label;
        }

        private static DataGridView CreateGrid()
        {
            DataGridView grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.ReadOnly = true;
            grid.AutoGenerateColumns = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            grid.RowTemplate.Height = 30;
            grid.ScrollBars = ScrollBars.Both;
            AppTheme.StyleGrid(grid);
            return grid;
        }

        private static Label CreateEmptyLabel(string text)
        {
            Label label = new Label();
            label.Text = text;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.Dock = DockStyle.Fill;
            label.Font = AppTheme.BodyFont;
            label.ForeColor = AppTheme.Muted;
            label.BackColor = AppTheme.Card;
            label.Visible = false;
            return label;
        }

        private static TextBox CreateFilterTextBox()
        {
            TextBox control = new TextBox();
            control.Dock = DockStyle.Top;
            control.Height = AppTheme.InputHeight;
            AppTheme.StyleTextBox(control, false);
            return control;
        }

        private static ComboBox CreateFilterComboBox()
        {
            ComboBox control = new ComboBox();
            control.Dock = DockStyle.Top;
            control.Height = AppTheme.InputHeight;
            AppTheme.StyleComboBox(control);
            return control;
        }

        private static DateTimePicker CreateFilterDatePicker()
        {
            DateTimePicker control = new DateTimePicker();
            control.Dock = DockStyle.Top;
            control.Height = AppTheme.InputHeight;
            control.ShowCheckBox = true;
            control.Checked = false;
            AppTheme.StyleDatePicker(control);
            return control;
        }

        private static Panel CreateFilterField(string labelText, Control control)
        {
            Panel field = new Panel();
            field.Dock = DockStyle.Fill;
            field.Margin = new Padding(4, 0, 8, 6);
            Label label = new Label();
            label.Text = labelText;
            label.Dock = DockStyle.Top;
            label.Height = 20;
            AppTheme.StyleLabel(label, false);
            field.Controls.Add(control);
            field.Controls.Add(label);
            return field;
        }

        private static void AddSampleColumns(DataGridView grid)
        {
            AddColumn(grid, "Sample ID", 210);
            AddColumn(grid, "Ngày yêu cầu", 95);
            AddColumn(grid, "Hợp đồng", 125);
            AddColumn(grid, "Mã vật tư", 115);
            AddColumn(grid, "Tên túi", 180);
            AddColumn(grid, "QA", 140);
            AddColumn(grid, "Deadline", 95);
            AddColumn(grid, "Trạng thái", 100);
        }

        private static void AddColumn(DataGridView grid, string title, float weight)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.HeaderText = title;
            column.Name = title;
            column.FillWeight = weight;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(column);
        }

        private void LoadCachedData(object sender, EventArgs e)
        {
            SampleManagerCacheSnapshot snapshot = cache.Snapshot();
            if (!snapshot.IsLoaded)
            {
                queueEmptyLabel.Text = "Đang tải dữ liệu";
                completedEmptyLabel.Text = "Đang tải dữ liệu";
                queueEmptyLabel.Visible = true;
                completedEmptyLabel.Visible = true;
                AppTheme.SetMutedStatus(statusLabel, "Đang tải dữ liệu");
                return;
            }
            QaOption selectedQa = qaFilter.SelectedItem as QaOption;
            LoadQaFilter(snapshot.QaOptions, selectedQa == null ? String.Empty : selectedQa.Id);
            RenderFromCache();
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
                    BeginInvoke((MethodInvoker)delegate { LoadCachedData(null, EventArgs.Empty); });
                }
                else
                {
                    LoadCachedData(null, EventArgs.Empty);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }

        private void LoadQaFilter(IList<QaOption> options, string selectedId)
        {
            IList<QaOption> items = new List<QaOption>();
            items.Add(new QaOption { Id = String.Empty, DisplayName = "Tất cả" });
            for (int index = 0; index < options.Count; index++)
            {
                items.Add(options[index]);
            }
            qaFilter.DataSource = null;
            qaFilter.Items.Clear();
            for (int itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                qaFilter.Items.Add(items[itemIndex]);
            }
            if (qaFilter.Items.Count > 0)
            {
                qaFilter.SelectedIndex = 0;
            }
            if (!String.IsNullOrWhiteSpace(selectedId))
            {
                for (int index = 0; index < items.Count; index++)
                {
                    if (String.Equals(items[index].Id, selectedId, StringComparison.Ordinal))
                    {
                        qaFilter.SelectedIndex = index;
                        break;
                    }
                }
            }
        }

        private void ToggleFilterPanel(object sender, EventArgs e)
        {
            if (filterPopup.Visible)
            {
                filterPopup.Close();
                filterButton.Focus();
                return;
            }

            Point buttonRight = filterButton.PointToScreen(new Point(filterButton.Width, filterButton.Height + 6));
            Rectangle workingArea = Screen.FromControl(this).WorkingArea;
            int x = buttonRight.X - filterPopup.Width;
            x = Math.Max(workingArea.Left, Math.Min(x, workingArea.Right - filterPopup.Width));
            int y = buttonRight.Y;
            filterPopup.Show(new Point(x, y));
        }

        private void ApplyFiltersAndClose(object sender, EventArgs e)
        {
            RenderFromCache();
            filterPopup.Close();
            filterButton.Focus();
        }

        private void ClearFilters(object sender, EventArgs e)
        {
            updatingFilters = true;
            try
            {
                contractFilterTextBox.Clear();
                materialFilterTextBox.Clear();
                searchTextBox.Clear();
                qaFilter.SelectedIndex = 0;
                requestFromPicker.Checked = false;
                requestToPicker.Checked = false;
                deadlineFromPicker.Checked = false;
                deadlineToPicker.Checked = false;
            }
            finally
            {
                updatingFilters = false;
            }
            RenderFromCache();
            filterPopup.Close();
            filterButton.Focus();
        }

        private void QuickSearchChanged(object sender, EventArgs e)
        {
            if (!updatingFilters)
            {
                RenderFromCache();
            }
        }

        private void RenderFromCache()
        {
            SampleManagerCacheSnapshot snapshot = cache.Snapshot();
            selectedSample = null;
            detailButton.Enabled = false;
            QaOption qa = qaFilter.SelectedItem as QaOption;
            string qaId = qa == null ? String.Empty : qa.Id;
            string search = searchTextBox.Text.Trim();
            IList<SampleManagementRecord> waiting = new List<SampleManagementRecord>();
            IList<SampleManagementRecord> completed = new List<SampleManagementRecord>();

            for (int sampleIndex = 0; sampleIndex < snapshot.Samples.Count; sampleIndex++)
            {
                SampleManagementRecord sample = snapshot.Samples[sampleIndex];
                SampleRequestRecord request = FindRequest(snapshot.Requests, sample.YeuCauId);
                if (request == null
                    || SampleNormalization.IsCanceledRequest(request)
                    || !MatchesFilters(request, sample, qaId, search))
                {
                    continue;
                }
                if (String.Equals(NormalizeSampleStatus(sample.TrangThaiMay), "Đã may", StringComparison.Ordinal))
                {
                    completed.Add(sample);
                }
                else
                {
                    waiting.Add(sample);
                }
            }

            waiting = SortSamples(waiting, snapshot.Requests);
            completed = SortSamples(completed, snapshot.Requests);
            RenderSamples(queueGrid, queueEmptyLabel, waiting, snapshot);
            RenderSamples(completedGrid, completedEmptyLabel, completed, snapshot);
            waitingCountLabel.Text = "Chờ may: " + waiting.Count;
            completedCountLabel.Text = "Đã may: " + completed.Count;
            totalCountLabel.Text = "Tổng: " + (waiting.Count + completed.Count);
            UpdateFilterButtonText();
            SampleSelectionChanged(null, EventArgs.Empty);
        }

        private bool MatchesFilters(
            SampleRequestRecord request,
            SampleManagementRecord sample,
            string qaId,
            string search)
        {
            if (!String.IsNullOrWhiteSpace(qaId)
                && !String.Equals(request.QaId, qaId, StringComparison.Ordinal))
            {
                return false;
            }
            if (!Contains(request.SoHopDong, contractFilterTextBox.Text.Trim())
                || !Contains(request.MaVatTu, materialFilterTextBox.Text.Trim()))
            {
                return false;
            }
            if (!MatchesDateRange(request.NgayTaoYeuCau, requestFromPicker, requestToPicker)
                || !MatchesDateRange(request.Deadline, deadlineFromPicker, deadlineToPicker))
            {
                return false;
            }
            if (!String.IsNullOrWhiteSpace(search)
                && !Contains(sample.SampleId, search)
                && !Contains(request.SoHopDong, search)
                && !Contains(request.MaVatTu, search)
                && !Contains(request.TenTui, search))
            {
                return false;
            }
            return true;
        }

        private static bool MatchesDateRange(
            string value,
            DateTimePicker fromPicker,
            DateTimePicker toPicker)
        {
            DateTime date;
            if (!TryParseDate(value, out date))
            {
                return !fromPicker.Checked && !toPicker.Checked;
            }
            if (fromPicker.Checked && date.Date < fromPicker.Value.Date)
            {
                return false;
            }
            if (toPicker.Checked && date.Date > toPicker.Value.Date)
            {
                return false;
            }
            return true;
        }

        private void RenderSamples(
            DataGridView grid,
            Label emptyLabel,
            IList<SampleManagementRecord> records,
            SampleManagerCacheSnapshot snapshot)
        {
            grid.Rows.Clear();
            if (records.Count == 0)
            {
                grid.Visible = false;
                emptyLabel.Visible = true;
                return;
            }
            for (int index = 0; index < records.Count; index++)
            {
                SampleManagementRecord sample = records[index];
                SampleRequestRecord request = FindRequest(snapshot.Requests, sample.YeuCauId);
                string status = NormalizeSampleStatus(sample.TrangThaiMay);
                int rowIndex = grid.Rows.Add(
                    sample.SampleId,
                    request == null ? String.Empty : FormatDate(request.NgayTaoYeuCau),
                    request == null ? String.Empty : request.SoHopDong,
                    request == null ? String.Empty : request.MaVatTu,
                    request == null ? String.Empty : request.TenTui,
                    request == null ? String.Empty : GetQaDisplayName(snapshot, request.QaId),
                    request == null ? String.Empty : FormatDate(request.Deadline),
                    status);
                grid.Rows[rowIndex].Tag = sample;
                StyleStatusCell(grid.Rows[rowIndex], status);
            }
            emptyLabel.Visible = false;
            grid.Visible = true;
            grid.Rows[0].Selected = true;
        }

        private static void StyleStatusCell(DataGridViewRow row, string status)
        {
            DataGridViewCell cell = row.Cells[row.Cells.Count - 1];
            if (String.Equals(status, "Đã may", StringComparison.Ordinal))
            {
                cell.Style.BackColor = AppTheme.SuccessBackground;
                cell.Style.ForeColor = AppTheme.Success;
                cell.Style.SelectionBackColor = AppTheme.SuccessBackground;
                cell.Style.SelectionForeColor = AppTheme.Success;
                return;
            }
            cell.Style.BackColor = Color.FromArgb(255, 247, 237);
            cell.Style.ForeColor = Color.FromArgb(154, 52, 18);
            cell.Style.SelectionBackColor = Color.FromArgb(255, 237, 213);
            cell.Style.SelectionForeColor = Color.FromArgb(154, 52, 18);
        }

        private void SampleSelectionChanged(object sender, EventArgs e)
        {
            DataGridView grid = sampleTabs.SelectedIndex == 1 ? completedGrid : queueGrid;
            selectedSample = grid.SelectedRows.Count == 0
                ? null
                : grid.SelectedRows[0].Tag as SampleManagementRecord;
            detailButton.Enabled = selectedSample != null;
        }

        private void SampleDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0)
            {
                OpenSelectedSample(sender, EventArgs.Empty);
            }
        }

        private void OpenSelectedSample(object sender, EventArgs e)
        {
            if (selectedSample == null)
            {
                return;
            }
            using (SampleDetailForm form = new SampleDetailForm(repository, cache, selectedSample))
            {
                form.ShowDialog(this);
                if (form.LastReadBack != null)
                {
                    RenderFromCache();
                }
            }
        }

        private void RefreshLiveData(object sender, EventArgs e)
        {
            try
            {
                Cursor = Cursors.WaitCursor;
                string selectedQaId = qaFilter.SelectedItem == null
                    ? null
                    : ((QaOption)qaFilter.SelectedItem).Id;
                IList<QaOption> liveQaOptions = repository.ReadActiveQaOptions();
                IList<SampleRequestRecord> liveRequests = repository.ReadAllRequests();
                IList<SampleManagementRecord> liveSamples = repository.ReadAllSamples();
                cache.ReplaceLive(liveQaOptions, liveRequests, liveSamples);
                LoadQaFilter(cache.Snapshot().QaOptions, selectedQaId);
                RenderFromCache();
                AppTheme.SetSuccessStatus(statusLabel, "Đã cập nhật");
            }
            catch (Exception exception)
            {
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

        private int GetActiveFilterCount()
        {
            int count = 0;
            if (!String.IsNullOrWhiteSpace(searchTextBox.Text)) count++;
            if (!String.IsNullOrWhiteSpace(contractFilterTextBox.Text)) count++;
            if (!String.IsNullOrWhiteSpace(materialFilterTextBox.Text)) count++;
            if (qaFilter.SelectedItem is QaOption && !String.IsNullOrWhiteSpace(((QaOption)qaFilter.SelectedItem).Id)) count++;
            if (requestFromPicker.Checked) count++;
            if (requestToPicker.Checked) count++;
            if (deadlineFromPicker.Checked) count++;
            if (deadlineToPicker.Checked) count++;
            return count;
        }

        private void UpdateFilterButtonText()
        {
            int count = GetActiveFilterCount();
            filterButton.Text = count == 0 ? "Lọc" : "Lọc (" + count + ")";
        }

        private static SampleRequestRecord FindRequest(
            IList<SampleRequestRecord> requests,
            string requestId)
        {
            for (int index = 0; index < requests.Count; index++)
            {
                if (String.Equals(requests[index].YeuCauId, requestId, StringComparison.Ordinal))
                {
                    return requests[index];
                }
            }
            return null;
        }

        private static string GetQaDisplayName(
            SampleManagerCacheSnapshot snapshot,
            string qaId)
        {
            for (int index = 0; index < snapshot.QaOptions.Count; index++)
            {
                if (String.Equals(snapshot.QaOptions[index].Id, qaId, StringComparison.Ordinal))
                {
                    return snapshot.QaOptions[index].ToString();
                }
            }
            return String.Empty;
        }

        private static bool Contains(string value, string search)
        {
            return String.IsNullOrWhiteSpace(search)
                || (!String.IsNullOrEmpty(value)
                    && value.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private static IList<SampleManagementRecord> SortSamples(
            IList<SampleManagementRecord> source,
            IList<SampleRequestRecord> requests)
        {
            List<SampleManagementRecord> result = new List<SampleManagementRecord>(source);
            result.Sort(delegate(SampleManagementRecord left, SampleManagementRecord right)
            {
                SampleRequestRecord leftRequest = FindRequest(requests, left.YeuCauId);
                SampleRequestRecord rightRequest = FindRequest(requests, right.YeuCauId);
                DateTime leftDeadline = ParseDateOrMax(leftRequest == null ? String.Empty : leftRequest.Deadline);
                DateTime rightDeadline = ParseDateOrMax(rightRequest == null ? String.Empty : rightRequest.Deadline);
                int deadlineCompare = leftDeadline.CompareTo(rightDeadline);
                if (deadlineCompare != 0)
                {
                    return deadlineCompare;
                }
                DateTime leftCreated = ParseDateOrMax(leftRequest == null ? String.Empty : leftRequest.NgayTaoYeuCau);
                DateTime rightCreated = ParseDateOrMax(rightRequest == null ? String.Empty : rightRequest.NgayTaoYeuCau);
                int createdCompare = leftCreated.CompareTo(rightCreated);
                if (createdCompare != 0)
                {
                    return createdCompare;
                }
                return ParseIntOrMax(left.SttMau).CompareTo(ParseIntOrMax(right.SttMau));
            });
            return result;
        }

        private static DateTime ParseDateOrMax(string value)
        {
            DateTime parsed;
            return TryParseDate(value, out parsed) ? parsed : DateTime.MaxValue;
        }

        private static int ParseIntOrMax(string value)
        {
            int parsed;
            return Int32.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : Int32.MaxValue;
        }

        private static string NormalizeSampleStatus(string value)
        {
            return SampleNormalization.NormalizeSampleStatus(value);
        }

        private static string FormatDate(string value)
        {
            DateTime date;
            return TryParseDate(value, out date)
                ? date.ToString("dd/MM/yy", CultureInfo.InvariantCulture)
                : value;
        }

        private static bool TryParseDate(string value, out DateTime date)
        {
            string[] formats = { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd", "dd/MM/yy", "dd/MM/yyyy" };
            return DateTime.TryParseExact(value, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
                || DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.None, out date);
        }
    }
}
