using System.Drawing;
using System.Numerics;
using System.Runtime.InteropServices;

namespace ImageStitchMaster;

public sealed class MainForm : Form
{
    private static readonly string[] SupportedExts =
    {
        ".jpg", ".jpeg", ".png", ".bmp",
        ".gif", ".tif", ".tiff", ".ico", ".emf", ".wmf",
        ".webp", ".heic", ".heif"
    };

    private readonly List<ImageItem> _items = new();
    private readonly List<ImageItem> _trash = new();

    private readonly MenuStrip _menu = new();
    private readonly Button _btnAdd = new();
    private readonly Button _btnSave = new();
    private readonly RadioButton _rbVertical = new();
    private readonly RadioButton _rbHorizontal = new();
    private readonly TextBox _txtLimit = new();
    private readonly ComboBox _cbxMode = new();
    private readonly SplitContainer _split = new();
    private readonly ListView _listView = new();
    private readonly ImageList _thumbList = new();
    private readonly Button _btnUp = new();
    private readonly Button _btnDown = new();
    private readonly Button _btnRemove = new();
    private readonly Button _btnClear = new();
    private readonly ZoomablePictureBox _picPreview = new();
    private readonly Label _lblHint = new();
    private readonly ToolStripStatusLabel _lblStatus = new();
    private readonly ToolStripProgressBar _progress = new();
    private readonly ToolStripStatusLabel _lblCpu = new();
    private readonly ToolStripStatusLabel _lblMem = new();
    private readonly System.Windows.Forms.Timer _memTimer = new();

    private int _refreshVersion;
    private Task _renderTask = Task.CompletedTask;
    private bool _previewDragging;
    private Point _lastMouse;
    private CancellationTokenSource? _limitCts;
    private ImageItem? _baseItem;

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
        miHelp.DropDownItems.Add(new ToolStripMenuItem("格式支持说明(&F)", null, (_, _) =>
            MessageBox.Show(this,
                "支持的图片格式：\n" +
                "· JPG / JPEG、PNG、BMP（直接使用）\n" +
                "· GIF、TIFF、ICO、EMF、WMF、WebP、HEIC / HEIF（自动转换）\n\n" +
                "转换规则：\n" +
                "1. 导入时，非 JPG 格式会自动转成 JPG 再用于拼接。\n" +
                "2. 多帧格式（如 GIF）只取第一帧。\n" +
                "3. 透明区域会填充为白色。\n" +
                "4. 转换只在本软件内部进行，不会修改你的原图。\n" +
                "5. WebP / HEIC 依赖系统解码器，无法解码的文件导入时会被跳过并提示。",
                "格式支持说明", MessageBoxButtons.OK, MessageBoxIcon.Information)));
        miHelp.DropDownItems.Add(new ToolStripMenuItem("关于(&A)", null, (_, _) =>
        {
            var ver = typeof(MainForm).Assembly.GetName().Version;
            string v = ver == null ? "" : $"v{ver.Major}.{ver.Minor}.{ver.Build}";
            MessageBox.Show(this,
                $"X图片拼接 {v}\n\n" +
                "支持横向/竖向拼接多张图片，可限制输出体积。\n\n" +
                "开发者：明灯花月夜\n网址：mdhyy.cn",
                "关于", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }));
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

        var lblMode = new Label { Text = "渲染方式：", AutoSize = true, Margin = new Padding(0, 6, 8, 0) };
        _cbxMode.DropDownStyle = ComboBoxStyle.DropDownList;
        _cbxMode.Width = 70;
        _cbxMode.Margin = new Padding(0, 2, 10, 0);
        _cbxMode.Items.AddRange(new object[] { "普通", "并行" });
        _cbxMode.SelectedIndex = 0;
        _cbxMode.SelectedIndexChanged += (_, _) => UpdateModeIndicator();

        var lblLimit = new Label { Text = "输出体积上限：", AutoSize = true, Margin = new Padding(0, 5, 8, 0) };
        var lblMb = new Label { Text = "MB（留空不限）", AutoSize = true, Margin = new Padding(0, 5, 0, 0) };
        _txtLimit.Width = 70;
        _txtLimit.TextAlign = HorizontalAlignment.Right;
        _txtLimit.Margin = new Padding(0, 0, 8, 0);
        _txtLimit.TextChanged += OnLimitTextChanged;

        var flowLimit = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Location = new Point(500, 18),
            Padding = new Padding(0)
        };
        flowLimit.Controls.AddRange(new Control[] { lblMode, _cbxMode, lblLimit, _txtLimit, lblMb });

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
        // 自绘第一列实现「序号在缩略图前面」；开双缓冲消除拖动闪烁
        _listView.OwnerDraw = true;
        typeof(ListView).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(_listView, true);
        _listView.DrawColumnHeader += (_, e) => e.DrawDefault = true;
        _listView.DrawItem += OnListDrawItem;
        _listView.Columns.Add("#", 90);
        _listView.Columns.Add("文件名", 150);
        _listView.Columns.Add("尺寸", 100);
        _listView.Columns.Add("基准", 50);
        _listView.MouseUp += OnListMouseUp;
        var miSetBase = new ToolStripMenuItem("设为基准");
        var miAutoBase = new ToolStripMenuItem("恢复自动（取最大）");
        miSetBase.Click += (_, _) => SetBaseFromSelection();
        miAutoBase.Click += (_, _) => { _baseItem = null; RefreshList(); RefreshPreview(); };
        var listMenu = new ContextMenuStrip();
        listMenu.Items.Add(miSetBase);
        listMenu.Items.Add(miAutoBase);
        _listView.ContextMenuStrip = listMenu;
        // 列宽跟随左面板宽度，文件名/尺寸各占剩余一半
        _listView.Resize += (_, _) => UpdateListColumnWidths();
        _listView.SelectedIndexChanged += (_, _) => UpdateButtons();
        _listView.AllowDrop = true;
        _listView.InsertionMark.Color = Color.FromArgb(0, 120, 215);
        _listView.ItemDrag += OnListItemDrag;
        _listView.DragEnter += OnListDragEnter;
        _listView.DragOver += OnListDragOver;
        _listView.DragDrop += OnListDragDrop;
        _listView.DragLeave += OnListDragLeave;

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
        _picPreview.BackColor = Color.FromArgb(245, 245, 245);
        _picPreview.Resize += (_, _) => _picPreview.HandleResize();
        _picPreview.MouseWheel += OnPreviewMouseWheel;
        _picPreview.MouseDown += OnPreviewMouseDown;
        _picPreview.MouseMove += OnPreviewMouseMove;
        _picPreview.MouseUp += OnPreviewMouseUp;
        _picPreview.MouseDoubleClick += (_, _) => _picPreview.ResetView();

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
        _lblCpu.Alignment = ToolStripItemAlignment.Right;
        _lblCpu.TextAlign = ContentAlignment.MiddleRight;
        _lblMem.Alignment = ToolStripItemAlignment.Right;
        _lblMem.TextAlign = ContentAlignment.MiddleRight;
        status.Items.AddRange(new ToolStripItem[] { _lblStatus, _progress, _lblMem, _lblCpu });
        UpdateModeIndicator();

        _memTimer.Interval = 1000;
        _memTimer.Tick += (_, _) => UpdateMemText();
        _memTimer.Start();

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
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tif;*.tiff;*.ico;*.emf;*.wmf;*.webp;*.heic;*.heif|所有文件|*.*",
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
        _progress.Visible = true;
        _progress.Maximum = paths.Length;
        _progress.Value = 0;
        IProgress<(int done, int total)> progress = new Progress<(int done, int total)>(p =>
        {
            _progress.Value = Math.Min(p.done, _progress.Maximum);
            _lblStatus.Text = $"正在导入 {p.done}/{p.total}…";
        });

        var loaded = new List<ImageItem>();
        var failed = new List<string>();
        try
        {
            await Task.Run(() =>
            {
                for (int i = 0; i < paths.Length; i++)
                {
                    try { loaded.Add(ImageItem.Load(paths[i])); }
                    catch { failed.Add(Path.GetFileName(paths[i])); }
                    progress.Report((i + 1, paths.Length));
                }
            });
            _items.AddRange(loaded);
            RefreshList();
            RefreshPreview();
        }
        finally
        {
            _progress.Visible = false;
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
            var lvi = new ListViewItem((i + 1).ToString()) { ImageIndex = i, Tag = it };
            lvi.SubItems.Add(it.FileName);
            lvi.SubItems.Add($"{it.Width}×{it.Height}");
            lvi.SubItems.Add(it == _baseItem ? "基准" : "");
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

    // ---- 拖放排序 ----

    private void OnListItemDrag(object? sender, ItemDragEventArgs e)
    {
        if (e.Item is not ListViewItem item) return;
        item.Selected = true;
        _listView.DoDragDrop(item, DragDropEffects.Move);
    }

    private void OnListDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(typeof(ListViewItem)) == true)
            e.Effect = DragDropEffects.Move;
        else
            OnFileDragEnter(sender, e);
    }

    private void OnListDragOver(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(typeof(ListViewItem)) == true)
        {
            var p = _listView.PointToClient(new Point(e.X, e.Y));
            var target = _listView.GetItemAt(p.X, p.Y);
            if (target != null)
            {
                bool after = p.Y > target.Bounds.Top + target.Bounds.Height / 2;
                SetInsertionMark(target.Index, after);
            }
            else if (p.Y > _listView.ClientSize.Height - 8)
            {
                // 拖到列表底部空白：插到末尾
                SetInsertionMark(_items.Count - 1, true);
            }
            else
            {
                SetInsertionMark(-1, false);
            }
            e.Effect = DragDropEffects.Move;
        }
        else if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
        {
            e.Effect = DragDropEffects.Copy;
        }
        else
        {
            e.Effect = DragDropEffects.None;
        }
    }

    private void OnListDragDrop(object? sender, DragEventArgs e)
    {
        try
        {
            if (e.Data?.GetData(typeof(ListViewItem)) is ListViewItem dragLvi && dragLvi.Tag is ImageItem dragItem)
            {
                int from = _items.IndexOf(dragItem);
                if (from < 0) return;

                var p = _listView.PointToClient(new Point(e.X, e.Y));
                int to = _items.Count;
                var target = _listView.GetItemAt(p.X, p.Y);
                if (target?.Tag is ImageItem targetItem)
                {
                    int ti = _items.IndexOf(targetItem);
                    bool after = p.Y > target.Bounds.Top + target.Bounds.Height / 2;
                    to = after ? ti + 1 : ti;
                }
                if (from < to) to--;
                if (to < 0) to = 0;

                if (from == to) return;

                _items.RemoveAt(from);
                _items.Insert(Math.Min(to, _items.Count), dragItem);
                RefreshList();
                int sel = Math.Max(0, Math.Min(to, _items.Count - 1));
                _listView.Items[sel].Selected = true;
                _listView.EnsureVisible(sel);
                RefreshPreview();
            }
            else
            {
                OnFileDragDrop(sender, e);
            }
        }
        finally
        {
            ClearInsertionMark();
        }
    }

    private void OnListDragLeave(object? sender, EventArgs e) => ClearInsertionMark();

    private void ClearInsertionMark()
    {
        _listView.InsertionMark.Index = -1;
        _listView.InsertionMark.AppearsAfterItem = false;
        _listView.Invalidate();
    }

    private void SetInsertionMark(int index, bool after)
    {
        if (_listView.InsertionMark.Index != index || _listView.InsertionMark.AppearsAfterItem != after)
        {
            _listView.InsertionMark.Index = index;
            _listView.InsertionMark.AppearsAfterItem = after;
            _listView.Invalidate();
        }
    }

    // ---- 自绘列表：序号在缩略图前面 ----

    private void OnListDrawItem(object? sender, DrawListViewItemEventArgs e)
    {
        // 一次性自绘整行，避免 OwnerDraw 下背景与文字分属两个事件、部分重绘时漏画文字
        var g = e.Graphics;
        bool sel = e.Item.Selected;
        var bg = sel ? SystemColors.Highlight : _listView.BackColor;
        using (var brush = new SolidBrush(bg))
            g.FillRectangle(brush, e.Bounds);

        var textColor = sel ? SystemColors.HighlightText : _listView.ForeColor;
        int left = e.Bounds.Left;

        // 第 1 列：序号（左）+ 缩略图（右）
        const int numWidth = 36;
        var numRect = new Rectangle(left, e.Bounds.Top, numWidth, e.Bounds.Height);
        TextRenderer.DrawText(g, e.Item.Text, _listView.Font, numRect, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        if (e.Item.ImageIndex >= 0 && e.Item.ImageIndex < _thumbList.Images.Count)
        {
            // Images[索引] 返回的是 HIMAGELIST 的副本，必须用完即释放，否则每次重绘都泄漏一个位图
            using var img = _thumbList.Images[e.Item.ImageIndex];
            int x = left + numWidth + (_listView.Columns[0].Width - numWidth - img.Width) / 2;
            int y = e.Bounds.Top + (e.Bounds.Height - img.Height) / 2;
            g.DrawImage(img, x, y);
        }
        left += _listView.Columns[0].Width;

        // 第 2 列：文件名
        TextRenderer.DrawText(g, e.Item.SubItems[1].Text, _listView.Font,
            new Rectangle(left, e.Bounds.Top, _listView.Columns[1].Width, e.Bounds.Height), textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        left += _listView.Columns[1].Width;

        // 第 3 列：尺寸
        TextRenderer.DrawText(g, e.Item.SubItems[2].Text, _listView.Font,
            new Rectangle(left, e.Bounds.Top, _listView.Columns[2].Width, e.Bounds.Height), textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        left += _listView.Columns[2].Width;

        // 第 4 列：基准（高亮显示当前以哪张图为准）
        if (e.Item.SubItems.Count > 3 && e.Item.SubItems[3].Text.Length > 0)
        {
            TextRenderer.DrawText(g, "基准", _listView.Font,
                new Rectangle(left, e.Bounds.Top, _listView.Columns[3].Width, e.Bounds.Height),
                Color.FromArgb(0, 120, 215),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
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

    // ---- 基准设置 ----

    private void OnListMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Right) return;
        // 右键时选中被点中的行，使菜单作用于该行
        var item = _listView.GetItemAt(e.X, e.Y);
        if (item != null) item.Selected = true;
    }

    private void SetBaseFromSelection()
    {
        if (_listView.SelectedItems.Count == 0) return;
        if (_listView.SelectedItems[0].Tag is ImageItem it)
        {
            _baseItem = it;
            RefreshList();
            RefreshPreview();
        }
    }

    private void UpdateListColumnWidths()
    {
        if (_listView.Columns.Count < 4) return;
        int total = Math.Max(0, _listView.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - 4);
        int seq = Math.Min(90, total);
        int baseW = Math.Min(60, Math.Max(0, total - seq));
        int rest = Math.Max(0, total - seq - baseW);
        _listView.Columns[1].Width = rest / 2;
        _listView.Columns[2].Width = rest - rest / 2;
        _listView.Columns[3].Width = baseW;
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
            _picPreview.ResetView();
            _lblHint.Visible = true;
            _lblStatus.Text = "就绪";
            // 已标记删除的缩略图此时都已 Dispose，再触发终结器回收历史泄漏的位图，让内存回落
            ReclaimMemory();
            return;
        }
        _lblHint.Visible = false;

        var items = _items.ToList();
        if (_baseItem != null && !_items.Contains(_baseItem)) _baseItem = null;
        bool vertical = _rbVertical.Checked;
        var layout = StitchEngine.ComputeLayout(items, vertical, _baseItem);
        var task = Task.Run(() => StitchEngine.RenderPreviewWithEstimate(items, vertical, layout.Canvas, baseItem: _baseItem));
        _renderTask = task;
        Bitmap bmp;
        long estimatedBytes;
        try { (bmp, estimatedBytes) = await task; }
        catch (Exception ex)
        {
            if (ver == _refreshVersion) _lblStatus.Text = "预览失败：" + ex.Message;
            return;
        }
        if (ver != _refreshVersion || IsDisposed) { bmp.Dispose(); return; }

        var old = _picPreview.Image;
        _picPreview.Image = bmp;
        old?.Dispose();
        _picPreview.ResetView();

        long? limit = null;
        try { limit = ParseLimitBytes(); } catch (FormatException) { /* 输入无效时按不限制处理 */ }

        string volumeText = limit is { } lim && estimatedBytes > lim
            ? $"预计体积约 {lim / 1024.0 / 1024.0:F2} MB（已达体积上限，将自动压缩）"
            : $"预计体积约 {estimatedBytes / 1024.0 / 1024.0:F2} MB（JPG）";

        _lblStatus.Text = $"共 {items.Count} 张图片，输出尺寸 {layout.Canvas.Width}×{layout.Canvas.Height}" +
                          (layout.Clamped ? "（已因超出尺寸上限自动缩小）" : "") +
                          $" | {volumeText}";
    }

    // ---- 预览缩放/平移 ----

    private void OnPreviewMouseWheel(object? sender, MouseEventArgs e)
    {
        if (_picPreview.Image == null) return;
        _picPreview.ZoomAt(e.Location, e.Delta > 0 ? 1.25f : 1f / 1.25f);
    }

    private void OnPreviewMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || _picPreview.Image == null) return;
        _previewDragging = true;
        _lastMouse = e.Location;
        _picPreview.Cursor = Cursors.SizeAll;
    }

    private void OnPreviewMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_previewDragging) return;
        _picPreview.PanBy(new PointF(e.X - _lastMouse.X, e.Y - _lastMouse.Y));
        _lastMouse = e.Location;
    }

    private void OnPreviewMouseUp(object? sender, MouseEventArgs e)
    {
        if (!_previewDragging) return;
        _previewDragging = false;
        _picPreview.Cursor = Cursors.Default;
    }

    // 体积上限输入变化后防抖刷新，让预测跟随限制更新
    private async void OnLimitTextChanged(object? sender, EventArgs e)
    {
        _limitCts?.Cancel();
        var cts = new CancellationTokenSource();
        _limitCts = cts;
        try
        {
            await Task.Delay(500, cts.Token);
            if (!cts.IsCancellationRequested && !IsDisposed)
                RefreshPreview();
        }
        catch (TaskCanceledException) { }
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
        string path = dlg.FileName;
        // 格式来源：保存类型下拉框（默认 JPG）；文件名直接写 .png 扩展名时也按 PNG 处理，避免格式与扩展名不符
        bool asPng = dlg.FilterIndex == 2 || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase);

        var items = _items.ToList();
        bool vertical = _rbVertical.Checked;
        var mode = _cbxMode.SelectedIndex == 1 ? StitchEngine.RenderMode.Parallel : StitchEngine.RenderMode.Normal;

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

        SavePlan? plan = null;
        try
        {
            plan = await Task.Run(() =>
            {
                using var bmp = StitchEngine.RenderFull(items, vertical, progress, mode, _baseItem);
                return StitchEngine.CreatePlan(bmp, path, asPng, limit, Status);
            });
            if (IsDisposed) return;

            var notes = new List<string>();
            if (plan.Meta.ConvertedToJpg) notes.Add("PNG 超出体积上限，将自动改存为 JPG");
            if (plan.Meta.Downscaled) notes.Add("将自动缩小分辨率以满足体积上限");
            string est = $"预计体积：{plan.Meta.Bytes / 1024.0 / 1024.0:F2} MB\n" +
                         $"格式：{plan.Meta.Format}" +
                         (plan.Meta.Format == "JPG" ? $"（质量 {plan.Meta.Quality}）" : "") +
                         $"\n尺寸：{plan.Meta.FinalSize.Width}×{plan.Meta.FinalSize.Height}" +
                         (notes.Count > 0 ? "\n\n" + string.Join("\n", notes) : "") +
                         "\n\n是否保存？";
            if (MessageBox.Show(this, est, "确认保存", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                _lblStatus.Text = "取消保存，合成文件已丢弃";
                return;
            }

            // 先把消息内容取出来；写完文件后立即释放数百 MB 的字节数组，再弹“保存成功”框
            string msg = $"保存成功！\n\n文件：{plan.Meta.FinalPath}\n格式：{plan.Meta.Format}" +
                         (plan.Meta.Format == "JPG" ? $"（质量 {plan.Meta.Quality}）" : "") +
                         $"\n尺寸：{plan.Meta.FinalSize.Width}×{plan.Meta.FinalSize.Height}" +
                         $"\n大小：{plan.Meta.Bytes / 1024.0 / 1024.0:F2} MB" +
                         (notes.Count > 0 ? "\n\n" + string.Join("\n", notes) : "");
            File.WriteAllBytes(plan.Meta.FinalPath, plan.Data);
            plan = null; // 大 byte[] 尽早失去引用，用户停留在提示框时也能被回收
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
            // 强制回收合成产生的画布与大字节数组（可能数百 MB），让内存峰值尽快回落
            ReclaimMemory();
        }
    }

    private void SetBusy(bool busy, string? statusText = null)
    {
        _btnAdd.Enabled = !busy;
        _menu.Enabled = !busy;
        _rbVertical.Enabled = _rbHorizontal.Enabled = !busy;
        _txtLimit.Enabled = !busy;
        _cbxMode.Enabled = !busy;
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

    // ================= 渲染模式指示 =================

    private void UpdateModeIndicator()
    {
        if (_cbxMode.SelectedIndex == 1)
        {
            var (cores, threads) = CpuInfo.Value;
            _lblCpu.Text = $"并行：{cores} 核 / {threads} 线程";
        }
        else
        {
            _lblCpu.Text = "普通：串行";
        }
    }

    /// <summary>每秒刷新一次，显示当前进程占用内存。</summary>
    private void UpdateMemText()
    {
        long bytes = Environment.WorkingSet;
        _lblMem.Text = $"内存 {bytes / 1024.0 / 1024.0:F0} MB";
    }

    /// <summary>强制执行终结器并回收，释放带终结器的 GDI+ 位图、LOH 大数组等，让进程内存尽快回落。</summary>
    private void ReclaimMemory()
    {
        GC.Collect(2, GCCollectionMode.Forced, true); // 全代收集 + 压缩 LOH，回收几百 MB 的大数组
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true);
    }

    private static readonly Lazy<(int Cores, int Threads)> CpuInfo = new(QueryCpuInfo);

    /// <summary>通过 GetLogicalProcessorInformation 统计物理核心与逻辑线程；失败时回退到 Environment.ProcessorCount。</summary>
    private static (int Cores, int Threads) QueryCpuInfo()
    {
        try
        {
            int len = 0;
            GetLogicalProcessorInformation(IntPtr.Zero, ref len); // 失败返回 ERROR_INSUFFICIENT_BUFFER，len 为所需大小
            var buffer = Marshal.AllocHGlobal(len);
            try
            {
                GetLogicalProcessorInformation(buffer, ref len);
                int size = Marshal.SizeOf<SystemLogicalProcessorInformation>();
                int count = len / size;
                int cores = 0, threads = 0;
                for (int i = 0; i < count; i++)
                {
                    var info = Marshal.PtrToStructure<SystemLogicalProcessorInformation>(buffer + i * size);
                    int bits = BitOperations.PopCount(info.ProcessorMask);
                    if (info.Relationship == RelationProcessorCore)
                    {
                        cores++;
                        threads += bits;
                    }
                }
                if (cores > 0 && threads > 0) return (cores, threads);
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
        catch { }
        int logical = Environment.ProcessorCount;
        return (logical, logical);
    }

    private const int RelationProcessorCore = 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemLogicalProcessorInformation
    {
        public nuint ProcessorMask;
        public int Relationship;
        private readonly int _pad;
        private readonly ulong _reserved0;
        private readonly ulong _reserved1;
    }

    [DllImport("kernel32.dll")]
    private static extern bool GetLogicalProcessorInformation(IntPtr buffer, ref int returnLength);
}
