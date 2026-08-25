using System.Drawing;

namespace ImageStitchMaster;

public sealed class MainForm : Form
{
    private static readonly string[] SupportedExts = { ".jpg", ".jpeg", ".png", ".bmp" };

    private readonly List<ImageItem> _items = new();
    private readonly List<ImageItem> _trash = new();

    private readonly MenuStrip _menu = new();
    private readonly Button _btnAdd = new();
    private readonly Button _btnSave = new();
    private readonly RadioButton _rbVertical = new();
    private readonly RadioButton _rbHorizontal = new();
    private readonly TextBox _txtLimit = new();
    private readonly SplitContainer _split = new();
    private readonly ListView _listView = new();
    private readonly ImageList _thumbList = new();
    private readonly Button _btnUp = new();
    private readonly Button _btnDown = new();
    private readonly Button _btnRemove = new();
    private readonly Button _btnClear = new();
    private readonly PictureBox _picPreview = new();
    private readonly Label _lblHint = new();
    private readonly ToolStripStatusLabel _lblStatus = new();
    private readonly ToolStripProgressBar _progress = new();

    private int _refreshVersion;
    private Task _renderTask = Task.CompletedTask;

    public MainForm()
    {
        // 从 exe 提取自身图标作为窗口图标（与文件图标保持一致）
        Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
        BuildUi();
        UpdateButtons();
    }

    private void BuildUi()
    {
        Text = "X图片拼接";
        Font = new Font("Microsoft YaHei UI", 9F);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(900, 620);
        ClientSize = new Size(1080, 700);
        AllowDrop = true;
        DragEnter += OnFileDragEnter;
        DragDrop += OnFileDragDrop;

        // ---- 菜单（.NET 8 无经典 MainMenu，用 System 渲染模拟经典外观）----
        _menu.RenderMode = ToolStripRenderMode.System;
        var miFile = new ToolStripMenuItem("文件(&F)");
        var miAdd = new ToolStripMenuItem("添加图片(&O)…", null, (_, _) => AddViaDialog()) { ShortcutKeys = Keys.Control | Keys.O };
        var miSave = new ToolStripMenuItem("保存拼接图(&S)…", null, (_, _) => SaveStitched()) { ShortcutKeys = Keys.Control | Keys.S };
        var miExit = new ToolStripMenuItem("退出(&X)", null, (_, _) => Close());
        miFile.DropDownItems.AddRange(new ToolStripItem[] { miAdd, miSave, new ToolStripSeparator(), miExit });
        var miHelp = new ToolStripMenuItem("帮助(&H)");
        miHelp.DropDownItems.Add(new ToolStripMenuItem("尺寸限制说明(&L)", null, (_, _) =>
            MessageBox.Show(this,
                "拼接输出的像素限制：\n\n" +
                "1. 单边尺寸上限约 65500 像素（GDI+ 与 JPEG 格式的限制）。\n" +
                "    拼接总长超过时会自动整体等比缩小，并在状态栏提示。\n\n" +
                "2. 内存占用约为 宽 × 高 × 3 字节。\n" +
                "    例如 1080×60000 的长图约占 190MB；画布过大可能导致内存不足。\n\n" +
                "合成时原图逐张加载、绘制后立即释放，内存峰值主要取决于画布本身。\n" +
                "常见场景（手机截图拼长图）远达不到上述限制，可放心使用。",
                "尺寸限制说明", MessageBoxButtons.OK, MessageBoxIcon.Information)));
        miHelp.DropDownItems.Add(new ToolStripMenuItem("关于(&A)", null, (_, _) =>
            MessageBox.Show(this, "X图片拼接 v1.0\n\n支持横向/竖向拼接多张图片，可限制输出体积。", "关于",
                MessageBoxButtons.OK, MessageBoxIcon.Information)));
        _menu.Items.AddRange(new ToolStripItem[] { miFile, miHelp });
        MainMenuStrip = _menu;

        // ---- 顶部工具区 ----
        var top = new Panel { Dock = DockStyle.Top, Height = 64, Padding = new Padding(10, 8, 10, 8) };

        _btnAdd.Text = "添加图片";
        _btnAdd.Size = new Size(130, 46);
        _btnAdd.Location = new Point(10, 9);
        _btnAdd.Font = new Font("Microsoft YaHei UI", 10F);
        _btnAdd.TextAlign = ContentAlignment.MiddleCenter;
        _btnAdd.Click += (_, _) => AddViaDialog();

        _btnSave.Text = "保存拼接图";
        _btnSave.Size = new Size(130, 46);
        _btnSave.Location = new Point(150, 9);
        _btnSave.Font = new Font("Microsoft YaHei UI", 10F);
        _btnSave.TextAlign = ContentAlignment.MiddleCenter;
        _btnSave.Click += (_, _) => SaveStitched();

        var grpDir = new GroupBox { Text = "拼接方向", Location = new Point(300, 2), Size = new Size(190, 58) };
        _rbVertical.Text = "竖向";
        _rbVertical.Checked = true;
        _rbVertical.Location = new Point(15, 24);
        _rbVertical.AutoSize = true;
        // 切换方向时 _rbVertical 的 Checked 状态必然翻转一次，两个方向都会触发刷新
        _rbVertical.CheckedChanged += (_, _) => RefreshPreview();
        _rbHorizontal.Text = "横向";
        _rbHorizontal.Location = new Point(100, 24);
        _rbHorizontal.AutoSize = true;
        grpDir.Controls.AddRange(new Control[] { _rbVertical, _rbHorizontal });

        var lblLimit = new Label { Text = "输出体积上限：", AutoSize = true, Margin = new Padding(0, 5, 8, 0) };
        var lblMb = new Label { Text = "MB（留空不限）", AutoSize = true, Margin = new Padding(0, 5, 0, 0) };
        _txtLimit.Width = 70;
        _txtLimit.TextAlign = HorizontalAlignment.Right;
        _txtLimit.Margin = new Padding(0, 0, 8, 0);

        var flowLimit = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Location = new Point(500, 18),
            Padding = new Padding(0)
        };
        flowLimit.Controls.AddRange(new Control[] { lblLimit, _txtLimit, lblMb });

        top.Controls.AddRange(new Control[] { _btnAdd, _btnSave, grpDir, flowLimit });

        // ---- 中部：列表 + 预览 ----
        _split.Dock = DockStyle.Fill;
        _split.FixedPanel = FixedPanel.Panel1;
        // 构造时 SplitterDistance 会被 clamp 失效，Load 后按实际宽度各占一半
        Load += (_, _) => _split.SplitterDistance = _split.Width / 2;

        _thumbList.ImageSize = new Size(48, 48);
        _thumbList.ColorDepth = ColorDepth.Depth32Bit;

        _listView.Dock = DockStyle.Fill;
        _listView.View = View.Details;
        _listView.FullRowSelect = true;
        _listView.HideSelection = false;
        _listView.MultiSelect = false;
        _listView.SmallImageList = _thumbList;
        _listView.Columns.Add("#", 40);
        _listView.Columns.Add("文件名", 150);
        _listView.Columns.Add("尺寸", 100);
        // 列宽跟随左面板宽度，文件名/尺寸各占剩余一半
        _listView.Resize += (_, _) => UpdateListColumnWidths();
        _listView.SelectedIndexChanged += (_, _) => UpdateButtons();
        _listView.AllowDrop = true;
        _listView.DragEnter += OnFileDragEnter;
        _listView.DragDrop += OnFileDragDrop;

        var listBtns = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, Padding = new Padding(4, 5, 0, 0) };
        foreach (var (btn, text) in new[] { (_btnUp, "上移"), (_btnDown, "下移"), (_btnRemove, "删除"), (_btnClear, "清空") })
        {
            btn.Text = text;
            btn.Size = new Size(72, 32);
            listBtns.Controls.Add(btn);
        }
        _btnUp.Click += (_, _) => MoveSelected(-1);
        _btnDown.Click += (_, _) => MoveSelected(1);
        _btnRemove.Click += (_, _) => RemoveSelected();
        _btnClear.Click += (_, _) => ClearAll();

        _split.Panel1.Controls.Add(_listView);
        _split.Panel1.Controls.Add(listBtns);

        _picPreview.Dock = DockStyle.Fill;
        _picPreview.SizeMode = PictureBoxSizeMode.Zoom;
        _picPreview.BackColor = Color.FromArgb(245, 245, 245);

        _lblHint.Dock = DockStyle.Fill;
        _lblHint.TextAlign = ContentAlignment.MiddleCenter;
        _lblHint.Text = "将图片文件拖拽到窗口任意位置\n\n或点击「添加图片」按钮";
        _lblHint.Font = new Font("Microsoft YaHei UI", 13F);
        _lblHint.ForeColor = Color.Gray;
        _lblHint.BackColor = Color.FromArgb(245, 245, 245);
        _lblHint.AllowDrop = true;
        _lblHint.DragEnter += OnFileDragEnter;
        _lblHint.DragDrop += OnFileDragDrop;

        _split.Panel2.Controls.Add(_picPreview);
        _split.Panel2.Controls.Add(_lblHint);
        _lblHint.BringToFront();

        // ---- 状态栏 ----
        var status = new StatusStrip();
        _lblStatus.Text = "就绪";
        _lblStatus.Spring = true;
        _lblStatus.TextAlign = ContentAlignment.MiddleLeft;
        _progress.Visible = false;
        _progress.Width = 200;
        status.Items.AddRange(new ToolStripItem[] { _lblStatus, _progress });

        Controls.Add(_split);
        Controls.Add(top);
        Controls.Add(status);
        Controls.Add(_menu);
    }

    // ================= 导入 =================

    private void AddViaDialog()
    {
        using var dlg = new OpenFileDialog
        {
            Title = "选择要拼接的图片",
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*",
            Multiselect = true
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            AddFiles(dlg.FileNames);
    }

    private void OnFileDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files && files.Any(IsSupported))
            e.Effect = DragDropEffects.Copy;
        else
            e.Effect = DragDropEffects.None;
    }

    private void OnFileDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] files)
            AddFiles(files.Where(IsSupported).ToArray());
    }

    private static bool IsSupported(string path) =>
        SupportedExts.Contains(Path.GetExtension(path).ToLowerInvariant());

    private async void AddFiles(string[] paths)
    {
        if (paths.Length == 0) return;
        SetBusy(true, "正在导入图片…");
        var loaded = new List<ImageItem>();
        var failed = new List<string>();
        try
        {
            await Task.Run(() =>
            {
                foreach (var p in paths)
                {
                    try { loaded.Add(ImageItem.Load(p)); }
                    catch { failed.Add(Path.GetFileName(p)); }
                }
            });
            _items.AddRange(loaded);
            RefreshList();
            RefreshPreview();
        }
        finally
        {
            SetBusy(false);
        }
        if (failed.Count > 0)
            MessageBox.Show(this, "以下文件无法识别为图片，已跳过：\n" + string.Join("\n", failed),
                "导入提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    // ================= 列表管理 =================

    private void RefreshList()
    {
        _listView.BeginUpdate();
        _listView.Items.Clear();
        _thumbList.Images.Clear();
        for (int i = 0; i < _items.Count; i++)
        {
            var it = _items[i];
            _thumbList.Images.Add(it.Thumbnail);
            var lvi = new ListViewItem((i + 1).ToString()) { ImageIndex = i };
            lvi.SubItems.Add(it.FileName);
            lvi.SubItems.Add($"{it.Width}×{it.Height}");
            _listView.Items.Add(lvi);
        }
        _listView.EndUpdate();
        UpdateButtons();
    }

    private void MoveSelected(int delta)
    {
        if (_listView.SelectedIndices.Count == 0) return;
        int i = _listView.SelectedIndices[0];
        int j = i + delta;
        if (j < 0 || j >= _items.Count) return;
        (_items[i], _items[j]) = (_items[j], _items[i]);
        RefreshList();
        _listView.Items[j].Selected = true;
        _listView.EnsureVisible(j);
        RefreshPreview();
    }

    private void RemoveSelected()
    {
        if (_listView.SelectedIndices.Count == 0) return;
        int i = _listView.SelectedIndices[0];
        _trash.Add(_items[i]);
        _items.RemoveAt(i);
        RefreshList();
        if (_items.Count > 0)
        {
            int sel = Math.Min(i, _items.Count - 1);
            _listView.Items[sel].Selected = true;
        }
        RefreshPreview();
    }

    private void ClearAll()
    {
        if (_items.Count == 0) return;
        _trash.AddRange(_items);
        _items.Clear();
        RefreshList();
        RefreshPreview();
    }

    private void UpdateListColumnWidths()
    {
        if (_listView.Columns.Count < 3) return;
        int total = Math.Max(0, _listView.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
        int seq = Math.Min(40, total);
        int rest = Math.Max(0, total - seq);
        _listView.Columns[1].Width = rest / 2;
        _listView.Columns[2].Width = rest - rest / 2;
    }

    private void UpdateButtons()
    {
        bool has = _items.Count > 0;
        bool sel = _listView.SelectedIndices.Count > 0;
        _btnSave.Enabled = has;
        _btnClear.Enabled = has;
        _btnUp.Enabled = sel && _listView.SelectedIndices[0] > 0;
        _btnDown.Enabled = sel && _listView.SelectedIndices[0] < _items.Count - 1;
        _btnRemove.Enabled = sel;
    }

    // ================= 预览 =================

    private async void RefreshPreview()
    {
        int ver = ++_refreshVersion;
        try { await _renderTask; } catch { /* 上一次预览的异常已单独处理 */ }
        if (ver != _refreshVersion || IsDisposed) return;

        // 此刻无渲染在跑，可安全释放已移除项
        foreach (var t in _trash) t.Dispose();
        _trash.Clear();

        if (_items.Count == 0)
        {
            var old0 = _picPreview.Image;
            _picPreview.Image = null;
            old0?.Dispose();
            _lblHint.Visible = true;
            _lblStatus.Text = "就绪";
            return;
        }
        _lblHint.Visible = false;

        var items = _items.ToList();
        bool vertical = _rbVertical.Checked;
        var task = Task.Run(() => StitchEngine.RenderPreview(items, vertical));
        _renderTask = task;
        Bitmap bmp;
        try { bmp = await task; }
        catch (Exception ex)
        {
            if (ver == _refreshVersion) _lblStatus.Text = "预览失败：" + ex.Message;
            return;
        }
        if (ver != _refreshVersion || IsDisposed) { bmp.Dispose(); return; }

        var old = _picPreview.Image;
        _picPreview.Image = bmp;
        old?.Dispose();

        var layout = StitchEngine.ComputeLayout(items, vertical);
        _lblStatus.Text = $"共 {items.Count} 张图片，输出尺寸 {layout.Canvas.Width}×{layout.Canvas.Height}" +
                          (layout.Clamped ? "（已因超出尺寸上限自动缩小）" : "");
    }

    // ================= 保存 =================

    private long? ParseLimitBytes()
    {
        var text = _txtLimit.Text.Trim();
        if (text.Length == 0) return null;
        if (!double.TryParse(text, out var mb) || mb <= 0)
            throw new FormatException("体积上限请输入正数（单位 MB），或留空表示不限制。");
        return (long)(mb * 1024 * 1024);
    }

    private async void SaveStitched()
    {
        if (_items.Count == 0) return;

        long? limit;
        try { limit = ParseLimitBytes(); }
        catch (FormatException ex)
        {
            MessageBox.Show(this, ex.Message, "输入有误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _txtLimit.Focus();
            return;
        }

        using var dlg = new SaveFileDialog
        {
            Title = "保存拼接图",
            Filter = "JPG 图片|*.jpg|PNG 图片|*.png",
            FileName = "拼接_" + DateTime.Now.ToString("yyyyMMdd_HHmmss")
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        bool asPng = dlg.FilterIndex == 2;
        string path = dlg.FileName;

        var items = _items.ToList();
        bool vertical = _rbVertical.Checked;

        SetBusy(true, "正在合成…");
        _progress.Visible = true;
        _progress.Maximum = items.Count;
        _progress.Value = 0;
        var progress = new Progress<(int done, int total)>(p =>
        {
            _progress.Value = Math.Min(p.done, _progress.Maximum);
            _lblStatus.Text = $"正在合成 {p.done}/{p.total}…";
        });
        void Status(string s) => BeginInvoke(() => _lblStatus.Text = s);

        try
        {
            var result = await Task.Run(() =>
            {
                using var bmp = StitchEngine.RenderFull(items, vertical, progress);
                return StitchEngine.Save(bmp, path, asPng, limit, Status);
            });

            var notes = new List<string>();
            if (result.ConvertedToJpg) notes.Add("PNG 超出体积上限，已自动改存为 JPG");
            if (result.Downscaled) notes.Add("已自动缩小分辨率以满足体积上限");
            string msg = $"保存成功！\n\n文件：{result.FinalPath}\n格式：{result.Format}" +
                         (result.Format == "JPG" ? $"（质量 {result.Quality}）" : "") +
                         $"\n尺寸：{result.FinalSize.Width}×{result.FinalSize.Height}" +
                         $"\n大小：{result.Bytes / 1024.0 / 1024.0:F2} MB" +
                         (notes.Count > 0 ? "\n\n" + string.Join("\n", notes) : "");
            _lblStatus.Text = "保存完成";
            MessageBox.Show(this, msg, "完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            _lblStatus.Text = "保存失败";
            MessageBox.Show(this, "保存失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            _progress.Visible = false;
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy, string? statusText = null)
    {
        _btnAdd.Enabled = !busy;
        _menu.Enabled = !busy;
        _rbVertical.Enabled = _rbHorizontal.Enabled = !busy;
        _txtLimit.Enabled = !busy;
        _listView.Enabled = !busy;
        AllowDrop = !busy;
        if (busy)
        {
            _btnSave.Enabled = _btnUp.Enabled = _btnDown.Enabled = _btnRemove.Enabled = _btnClear.Enabled = false;
            UseWaitCursor = true;
            if (statusText != null) _lblStatus.Text = statusText;
        }
        else
        {
            UseWaitCursor = false;
            UpdateButtons();
        }
    }
}
