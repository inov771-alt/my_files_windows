using System.Diagnostics;
using MyFiles.Upload;

namespace MyFiles;

internal sealed class MainForm : Form
{
    private readonly List<UploadItem> _items = new();
    private readonly Dictionary<UploadItem, ListViewItem> _rows = new();
    private readonly UploadClient _client = new();
    private CancellationTokenSource? _cts;
    private bool _uploading;
    private bool _closeRequested;
    private bool _autoStart;

    private readonly Stopwatch _speedWatch = new();
    private long _speedLastMs;
    private long _speedLastBytes;
    private string _speedText = "";

    private readonly Button _btnAdd = new() { Text = "Добавить файлы…", Width = 150, Height = 30 };
    private readonly Button _btnClear = new() { Text = "Очистить", Width = 110, Height = 30 };
    private readonly Label _lblDir = new() { Text = "Папка на сервере:", AutoSize = true, Margin = new Padding(14, 9, 3, 0) };
    private readonly TextBox _txtDir = new() { Width = 240, Margin = new Padding(3, 6, 3, 3), PlaceholderText = "пусто — корневая папка" };
    private readonly FilesListView _lv = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false, OwnerDraw = true, AllowDrop = true, ShowItemToolTips = true };
    private readonly ProgressBar _overallBar = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1000, Height = 18, Margin = new Padding(3, 6, 3, 2) };
    private readonly Label _lblTotal = new() { AutoSize = true, Margin = new Padding(3, 0, 3, 0), Text = "" };
    private readonly Label _lblStatus = new() { AutoSize = true, Margin = new Padding(3, 2, 3, 0), Text = "Добавьте файлы и нажмите «Загрузить»." };
    private readonly Label _lblHint = new() { AutoSize = true, ForeColor = Color.Gray, Font = new Font("Segoe UI", 8.25f), Margin = new Padding(3, 2, 3, 0), Text = "Файлы можно перетаскивать в список. Двойной щелчок по строке с ошибкой — подробности." };
    private readonly Button _btnUpload = new() { Text = "Загрузить", Width = 130, Height = 32 };
    private readonly Button _btnCancel = new() { Text = "Отмена", Width = 110, Height = 32, Enabled = false };

    public MainForm()
    {
        Text = "Мои файлы — загрузка на files.spkmd.ru";
        Font = new Font("Segoe UI", 9f);
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(660, 430);
        Size = new Size(800, 560);
        KeyPreview = true;
        Padding = new Padding(8);

        _lv.Columns.Add("Файл", 300);
        _lv.Columns.Add("Размер", 90, HorizontalAlignment.Right);
        _lv.Columns.Add("Прогресс", 150);
        _lv.Columns.Add("Статус", 110);
        _lv.DrawColumnHeader += (_, e) => e.DrawDefault = true;
        _lv.DrawItem += (_, e) => e.DrawDefault = true;
        _lv.DrawSubItem += LvDrawSubItem;
        _lv.SizeChanged += (_, _) => AdjustColumns();
        _lv.DoubleClick += LvDoubleClick;
        _lv.DragEnter += LvDragEnter;
        _lv.DragDrop += LvDragDrop;

        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false, Margin = new Padding(0, 0, 0, 6) };
        top.Controls.AddRange(new Control[] { _btnAdd, _btnClear, _lblDir, _txtDir });

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
        bottom.Controls.Add(_btnCancel);   // при RightToLeft первый элемент — правый
        bottom.Controls.Add(_btnUpload);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7 };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(top, 0, 0);
        root.Controls.Add(_lv, 0, 1);
        root.Controls.Add(_overallBar, 0, 2);
        root.Controls.Add(_lblTotal, 0, 3);
        root.Controls.Add(_lblStatus, 0, 4);
        root.Controls.Add(_lblHint, 0, 5);
        root.Controls.Add(bottom, 0, 6);
        Controls.Add(root);

        _btnAdd.Click += (_, _) => PickFiles();
        _btnClear.Click += (_, _) => ClearList();
        _btnUpload.Click += (_, _) => { _ = RunUploadsAsync(); };
        _btnCancel.Click += (_, _) => CancelUploads();
        KeyDown += (_, e) => { if (e.KeyCode == Keys.Escape && _uploading) CancelUploads(); };
        FormClosing += OnFormClosing;
        Shown += (_, _) => { AdjustColumns(); if (_autoStart && HasPending()) _ = RunUploadsAsync(); };

        UpdateButtons();
    }

    // ---------- Добавление файлов ----------

    /// <summary>Добавляет файлы (папки раскрываются, без подпапок). Возвращает число добавленных файлов.</summary>
    public int AddPaths(IEnumerable<string> paths)
    {
        var existing = new HashSet<string>(_items.Select(i => i.FullPath), StringComparer.OrdinalIgnoreCase);
        int added = 0;
        foreach (string path in paths)
        {
            IEnumerable<string> files;
            try
            {
                if (Directory.Exists(path))
                {
                    files = Directory.EnumerateFiles(path).ToList();
                }
                else if (File.Exists(path))
                {
                    files = new[] { path };
                }
                else
                {
                    continue;
                }
            }
            catch
            {
                continue;
            }

            foreach (string file in files)
            {
                string full;
                long size;
                try
                {
                    full = Path.GetFullPath(file);
                    size = new FileInfo(full).Length;
                }
                catch
                {
                    continue;
                }
                if (!existing.Add(full))
                {
                    continue;
                }
                var item = new UploadItem { FullPath = full, FileName = Path.GetFileName(full), Size = size };
                var row = new ListViewItem(item.FileName) { Tag = item, ToolTipText = full };
                row.SubItems.Add(Utils.FormatSize(size));
                row.SubItems.Add("");
                row.SubItems.Add(StateText(item.State));
                _rows[item] = row;
                _items.Add(item);
                _lv.Items.Add(row);
                added++;
            }
        }
        if (added > 0)
        {
            UpdateButtons();
            UpdateOverall();
        }
        return added;
    }

    public void EnableAutoStart()
    {
        _autoStart = true;
    }

    /// <summary>Файлы от повторного запуска программы (Проводник мог разбить выборку на несколько запусков).</summary>
    public void OnExternalArgs(string[] args)
    {
        if (WindowState == FormWindowState.Minimized)
        {
            WindowState = FormWindowState.Normal;
        }
        Activate();
        int added = AddPaths(args);
        if (added == 0)
        {
            return;
        }
        if (_uploading)
        {
            SetStatus("Добавлено в очередь: " + added);
        }
        else
        {
            _ = RunUploadsAsync();
        }
    }

    private void PickFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Выберите файлы для загрузки",
            Filter = "Все файлы (*.*)|*.*",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            AddPaths(dialog.FileNames);
        }
    }

    private void ClearList()
    {
        if (_uploading) return;
        _items.Clear();
        _rows.Clear();
        _lv.Items.Clear();
        _overallBar.Value = 0;
        _lblTotal.Text = "";
        SetStatus("Список очищен. Добавьте файлы и нажмите «Загрузить».");
        UpdateButtons();
    }

    private void LvDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data is not null && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effect = DragDropEffects.Copy;
        }
    }

    private void LvDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data is not null && e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            AddPaths(files);
        }
    }

    private void LvDoubleClick(object? sender, EventArgs e)
    {
        if (_lv.SelectedItems.Count == 0) return;
        if (_lv.SelectedItems[0].Tag is UploadItem item && !string.IsNullOrEmpty(item.Error))
        {
            MessageBox.Show(this, item.FileName + "\n\n" + item.Error, "Ошибка загрузки",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ---------- Загрузка ----------

    private bool HasPending()
    {
        return _items.Any(i => i.State is UploadState.Waiting or UploadState.Error or UploadState.Cancelled);
    }

    private async Task RunUploadsAsync()
    {
        if (_uploading || !HasPending())
        {
            return;
        }

        // Повторная попытка для ошибок и отменённых; «Готово» пропускается.
        foreach (UploadItem item in _items)
        {
            if (item.State is UploadState.Error or UploadState.Cancelled)
            {
                item.State = UploadState.Waiting;
                item.Sent = 0;
                item.Error = null;
                UpdateRow(item);
            }
        }

        _uploading = true;
        _autoStart = false;
        _cts = new CancellationTokenSource();
        _speedWatch.Restart();
        _speedLastMs = 0;
        _speedLastBytes = 0;
        _speedText = "";
        _btnCancel.Text = "Отмена";
        UpdateButtons();
        UpdateOverall();

        int done = 0, failed = 0, cancelled = 0;
        string dir = _txtDir.Text.Trim();
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                UploadItem? item = _items.FirstOrDefault(i => i.State == UploadState.Waiting);
                if (item is null)
                {
                    break;
                }

                try { item.Size = new FileInfo(item.FullPath).Length; } catch { }
                item.State = UploadState.Uploading;
                item.Sent = 0;
                UpdateRow(item);
                SetStatus("Загружается: " + item.FileName);

                var progress = new Progress<long>(sent => OnFileProgress(item, sent));
                try
                {
                    UploadResult result = await _client.UploadAsync(item.FullPath, dir, progress, _cts.Token);
                    if (result.Success)
                    {
                        item.State = UploadState.Done;
                        item.Sent = item.Size;
                        item.Error = null;
                        done++;
                    }
                    else
                    {
                        item.State = UploadState.Error;
                        item.Error = result.Message;
                        failed++;
                    }
                }
                catch (OperationCanceledException)
                {
                    item.State = UploadState.Cancelled;
                    cancelled++;
                    UpdateRow(item);
                    break;
                }
                catch (Exception ex)
                {
                    item.State = UploadState.Error;
                    item.Error = ex.Message;
                    failed++;
                }
                UpdateRow(item);
                UpdateOverall();
            }
        }
        finally
        {
            _uploading = false;
            _cts.Dispose();
            _cts = null;
            _speedWatch.Stop();
            UpdateButtons();
            UpdateOverall();

            int queued = _items.Count(i => i.State == UploadState.Waiting);
            string summary = $"Итог: загружено — {done}, ошибок — {failed}, отменено — {cancelled}";
            if (queued > 0)
            {
                summary += $", в очереди — {queued}";
            }
            if (failed > 0)
            {
                summary += ". Двойной щелчок по ошибке — подробности.";
            }
            SetStatus(summary);

            if (_closeRequested)
            {
                BeginInvoke(new Action(Close));
            }
        }
    }

    private void CancelUploads()
    {
        if (!_uploading) return;
        _btnCancel.Enabled = false;
        _btnCancel.Text = "Отмена…";
        SetStatus("Отмена загрузки…");
        _cts?.Cancel();
    }

    private void OnFileProgress(UploadItem item, long sent)
    {
        if (IsDisposed) return;
        item.Sent = sent;
        UpdateRow(item);
        UpdateOverall();
    }

    // ---------- Отображение ----------

    private static string StateText(UploadState state) => state switch
    {
        UploadState.Waiting => "Ожидание",
        UploadState.Uploading => "Загрузка",
        UploadState.Done => "Готово",
        UploadState.Error => "Ошибка",
        UploadState.Cancelled => "Отменено",
        _ => ""
    };

    private static Color StateColor(UploadState state) => state switch
    {
        UploadState.Uploading => Color.FromArgb(0, 90, 180),
        UploadState.Done => Color.FromArgb(0, 128, 0),
        UploadState.Error => Color.Firebrick,
        UploadState.Cancelled => Color.Gray,
        _ => SystemColors.WindowText
    };

    private void UpdateRow(UploadItem item)
    {
        ListViewItem row = _rows[item];
        row.SubItems[1].Text = Utils.FormatSize(item.Size);
        row.SubItems[2].Text = item.Percent + "%";
        row.SubItems[3].Text = StateText(item.State);
        row.ForeColor = StateColor(item.State);
        row.ToolTipText = string.IsNullOrEmpty(item.Error) ? item.FullPath : item.Error;
        _lv.Invalidate(row.Bounds); // перерисовать индикатор прогресса
    }

    private void UpdateOverall()
    {
        long total = _items.Sum(i => i.Size);
        long sent = _items.Sum(i => i.Sent);
        _overallBar.Value = total > 0 ? (int)Math.Min(1000, sent * 1000 / total) : 0;

        string speed = "";
        if (_uploading)
        {
            long now = _speedWatch.ElapsedMilliseconds;
            if (now - _speedLastMs >= 700)
            {
                double bps = (sent - _speedLastBytes) * 1000.0 / Math.Max(1, now - _speedLastMs);
                _speedText = Utils.FormatSpeed(bps);
                _speedLastMs = now;
                _speedLastBytes = sent;
            }
            if (_speedText.Length > 0)
            {
                speed = " · " + _speedText;
            }
        }
        _lblTotal.Text = total > 0 ? $"Всего: {Utils.FormatSize(sent)} из {Utils.FormatSize(total)}{speed}" : "";
    }

    private void UpdateButtons()
    {
        _btnAdd.Enabled = true;                 // добавление в очередь доступно и во время загрузки
        _btnClear.Enabled = !_uploading && _items.Count > 0;
        _btnUpload.Enabled = !_uploading && HasPending();
        _btnCancel.Enabled = _uploading;
        _txtDir.Enabled = !_uploading;
    }

    private void SetStatus(string text)
    {
        _lblStatus.Text = text;
    }

    private void AdjustColumns()
    {
        int other = _lv.Columns[1].Width + _lv.Columns[2].Width + _lv.Columns[3].Width;
        _lv.Columns[0].Width = Math.Max(160, _lv.ClientSize.Width - other - 4);
    }

    private void LvDrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.ColumnIndex != 2 || e.Item.Tag is not UploadItem item)
        {
            e.DrawDefault = true;
            return;
        }

        Graphics g = e.Graphics;
        bool selected = e.Item.Selected;
        using (var bg = new SolidBrush(selected ? SystemColors.Highlight : _lv.BackColor))
        {
            g.FillRectangle(bg, e.Bounds);
        }

        Rectangle r = Rectangle.Inflate(e.Bounds, -3, -4);
        if (r.Width <= 4 || r.Height <= 4) return;

        using (var back = new SolidBrush(selected ? Color.FromArgb(70, 255, 255, 255) : Color.FromArgb(232, 232, 232)))
        {
            g.FillRectangle(back, r);
        }

        int percent = item.State == UploadState.Waiting ? 0 : item.Percent;
        int width = (int)((long)r.Width * percent / 100);
        Color fill = item.State switch
        {
            UploadState.Done => Color.FromArgb(76, 175, 80),
            UploadState.Error => Color.FromArgb(229, 115, 115),
            UploadState.Cancelled => Color.FromArgb(189, 189, 189),
            _ => Color.FromArgb(66, 133, 244)
        };
        if (width > 0)
        {
            using var brush = new SolidBrush(fill);
            g.FillRectangle(brush, new Rectangle(r.X, r.Y, width, r.Height));
        }
        using (var pen = new Pen(Color.FromArgb(160, 160, 160)))
        {
            g.DrawRectangle(pen, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        string text = item.State == UploadState.Waiting ? "" : percent + "%";
        TextRenderer.DrawText(g, text, _lv.Font, r,
            selected ? Color.White : Color.FromArgb(32, 32, 32),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (!_uploading)
        {
            return;
        }
        DialogResult answer = MessageBox.Show(this,
            "Загрузка ещё не завершена. Отменить загрузку и выйти?",
            "Мои файлы", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer == DialogResult.No)
        {
            e.Cancel = true;
            return;
        }
        _closeRequested = true;
        e.Cancel = true; // окно закроется само, когда загрузка корректно остановится
        _cts?.Cancel();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts?.Dispose();
            _client.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed class FilesListView : ListView
    {
        public FilesListView()
        {
            DoubleBuffered = true;
        }
    }
}
