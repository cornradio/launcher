﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using System.Runtime.InteropServices;
using System.Drawing.Drawing2D;

namespace launcher
{
    public partial class Launcher : Form
    {
        private Dictionary<PictureBox, string> _savedFilePaths = new Dictionary<PictureBox, string>();
        private Dictionary<PictureBox, Size> _originalSizes = new Dictionary<PictureBox, Size>();
        private Timer _animationTimer;
        private PictureBox _currentAnimatingBox = null;
        private bool _isSmall = false;
        private Dictionary<PictureBox, bool> _isFolder = new Dictionary<PictureBox, bool>();
        private PictureBox _dragSourceBox = null;
        private Point? _dragStartPoint = null;
        private string currentconfig = "panel_dot1.txt";
        private const int DragThreshold = 5; // 拖动阈值，像素单位

        //窗口可以拖拽
        private const int WM_NCHITTEST = 0x84;
        private const int HTCLIENT = 0x1;
        private const int HTCAPTION = 0x2;
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);

            if (message.Msg == WM_NCHITTEST && (int)message.Result == HTCLIENT)
                message.Result = (IntPtr)HTCAPTION;
        }

        // 添加新的字典来缓存所有页面的数据
        private Dictionary<string, Dictionary<PictureBox, Image>> _pageImageCache = new Dictionary<string, Dictionary<PictureBox, Image>>();
        private Dictionary<string, Dictionary<PictureBox, string>> _pagePathCache = new Dictionary<string, Dictionary<PictureBox, string>>();
        private Dictionary<string, Dictionary<PictureBox, bool>> _pageFolderCache = new Dictionary<string, Dictionary<PictureBox, bool>>();

        public Launcher()
        {
            InitializeComponent();
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.None;
            this.MouseWheel += new MouseEventHandler(Form1_MouseWheel);

            // 初始化缓存
            InitializePageCaches();

            // 为所有PictureBox设置事件
            foreach (Control control in this.Controls)
            {
                if (control is PictureBox pictureBox)
                {
                    pictureBox.AllowDrop = true;
                    pictureBox.DragEnter += PictureBox_DragEnter;
                    pictureBox.DragDrop += PictureBox_DragDrop;
                    pictureBox.MouseClick += PictureBox_MouseClick;
                    pictureBox.MouseDown += PictureBox_MouseDown;
                    pictureBox.MouseMove += PictureBox_MouseMove;
                    pictureBox.MouseUp += PictureBox_MouseUp;
                    _originalSizes[pictureBox] = pictureBox.Size;
                    _savedFilePaths[pictureBox] = string.Empty;
                }
            }

            // 初始化定时器
            _animationTimer = new Timer();
            _animationTimer.Interval = 50;
            _animationTimer.Tick += AnimationTimer_Tick;

            LoadConfig(); // 加载配置
            SetupConfigWatcher(); //监听来自配置文件的修改

            // 预加载所有页面
            PreloadAllPages();
        }
        FileSystemWatcher configWatcher = new FileSystemWatcher();
        private void SetupConfigWatcher()
        {
            configWatcher.Path = AppDomain.CurrentDomain.BaseDirectory;
            configWatcher.Filter =currentconfig;
            configWatcher.NotifyFilter = NotifyFilters.LastWrite;
            configWatcher.Changed += OnConfigChanged;
            configWatcher.EnableRaisingEvents = true;
        }

        private void OnConfigChanged(object sender, FileSystemEventArgs e)
        {
            // 防止多次触发
            System.Threading.Thread.Sleep(100);

            // 确保是文件改动
            if (e.ChangeType == WatcherChangeTypes.Changed)
            {
                this.Invoke((MethodInvoker)delegate
                {
                    LoadConfig(currentconfig);  // 重新加载配置
                });
            }
        }


        private void PictureBox_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effect = DragDropEffects.Copy;
            }
            else if (e.Data.GetDataPresent(typeof(PictureBox)))
            {
                e.Effect = DragDropEffects.Move;
            }
        }

        private void PictureBox_DragDrop(object sender, DragEventArgs e)
        {
            if (sender is PictureBox targetBox)
            {
                if (e.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    // 处理文件拖放
                    string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                    if (files != null && files.Length > 0)
                    {
                        HandleFileDropped(targetBox, files[0]);
                    }
                }
                else if (e.Data.GetDataPresent(typeof(PictureBox)) && _dragSourceBox != null)
                {
                    // 处理图标移动
                    if (_dragSourceBox != targetBox)
                    {
                        SwapIcons(_dragSourceBox, targetBox);
                    }
                    _dragSourceBox = null;
                }
            }
        }

        private void PictureBox_MouseClick(object sender, MouseEventArgs e)
        {
            if (sender is PictureBox pictureBox && _savedFilePaths.ContainsKey(pictureBox))
            {
                string filePath = _savedFilePaths[pictureBox];
                if (!string.IsNullOrEmpty(filePath))
                {
                    if (e.Button == MouseButtons.Right)
                    {
                        // 右键点击 - 删除图标
                        pictureBox.Image = null;
                        _savedFilePaths[pictureBox] = string.Empty;
                        _isFolder[pictureBox] = false;
                        SaveConfig();
                    }
                    else if (e.Button == MouseButtons.Middle)
                    {
                        // 中键点击 - 打开文件位置
                        try
                        {
                            if (File.Exists(filePath))
                            {
                                string argument = "/select, \"" + filePath + "\"";
                                System.Diagnostics.Process.Start("explorer.exe", argument);
                            }
                            else if (Directory.Exists(filePath))
                            {
                                System.Diagnostics.Process.Start("explorer.exe", filePath);
                            }
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("无法打开文件位置：" + ex.Message);
                        }
                    }
                }
            }
        }

        private void PictureBox_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && sender is PictureBox pictureBox)
            {
                _dragStartPoint = e.Location;
                _dragSourceBox = pictureBox;
            }
        }

        private void PictureBox_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragStartPoint.HasValue && sender is PictureBox pictureBox)
            {
                // 计算鼠标移动距离
                int deltaX = Math.Abs(e.X - _dragStartPoint.Value.X);
                int deltaY = Math.Abs(e.Y - _dragStartPoint.Value.Y);

                // 如果移动距离超过阈值，开始拖动
                if (deltaX > DragThreshold || deltaY > DragThreshold)
                {
                    if (_savedFilePaths.ContainsKey(pictureBox) && !string.IsNullOrEmpty(_savedFilePaths[pictureBox]))
                    {
                        pictureBox.DoDragDrop(pictureBox, DragDropEffects.Move);
                    }
                    _dragStartPoint = null;
                }
            }
        }

        private void PictureBox_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && sender is PictureBox pictureBox)
            {
                // 如果鼠标没有移动（或移动很小），则触发点击事件
                if (_dragStartPoint.HasValue)
                {
                    int deltaX = Math.Abs(e.X - _dragStartPoint.Value.X);
                    int deltaY = Math.Abs(e.Y - _dragStartPoint.Value.Y);

                    if (deltaX <= DragThreshold && deltaY <= DragThreshold)
                    {
                        // 执行点击操作
                        if (_savedFilePaths.ContainsKey(pictureBox) && !string.IsNullOrEmpty(_savedFilePaths[pictureBox]))
                        {
                            _currentAnimatingBox = pictureBox;
                            _animationTimer.Start();

                            try
                            {
                                string filePath = _savedFilePaths[pictureBox];
                                if (_isFolder[pictureBox])
                                {
                                    System.Diagnostics.Process.Start("explorer.exe", filePath);
                                }
                                else
                                {
                                    System.Diagnostics.Process.Start(filePath);
                                }
                            }
                            catch (Exception ex)
                            {
                                MessageBox.Show("启动失败：" + ex.Message);
                            }
                        }
                    }
                }
                _dragStartPoint = null;
            }
        }

        private void AnimationTimer_Tick(object sender, EventArgs e)
        {
            if (_currentAnimatingBox == null) return;

            if (!_isSmall)
            {
                // 缩小到90%，但保持位置不变
                _currentAnimatingBox.SizeMode = PictureBoxSizeMode.StretchImage;
                _currentAnimatingBox.Padding = new Padding(
                    (int)(_originalSizes[_currentAnimatingBox].Width * 0.05),
                    (int)(_originalSizes[_currentAnimatingBox].Height * 0.05),
                    (int)(_originalSizes[_currentAnimatingBox].Width * 0.05),
                    (int)(_originalSizes[_currentAnimatingBox].Height * 0.05)
                );
                _isSmall = true;
            }
            else
            {
                // 恢复原始大小
                _currentAnimatingBox.SizeMode = PictureBoxSizeMode.Zoom;
                _currentAnimatingBox.Padding = new Padding(0);
                _isSmall = false;
                _animationTimer.Stop();
                _currentAnimatingBox = null;
            }
        }

        private void SaveConfig()
        {
            try
            {
                using (StreamWriter writer = new StreamWriter(currentconfig))
                {
                    foreach (var pair in _savedFilePaths)
                    {
                        if (!string.IsNullOrEmpty(pair.Value))
                        {
                            writer.WriteLine($"{pair.Key.Name}={pair.Value}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存配置失败：" + ex.Message);
            }
        }

        private void LoadConfig()
        {
            try
            {
                // 清空现有图标
                foreach (var pictureBox in _savedFilePaths.Keys.ToList())
                {
                    pictureBox.Image = null;
                    _savedFilePaths[pictureBox] = string.Empty;
                    _isFolder[pictureBox] = false;
                }

                // 读取配置文件
                if (File.Exists(currentconfig))
                {
                    string[] lines = File.ReadAllLines(currentconfig);
                    foreach (string line in lines)
                    {
                        string[] parts = line.Split('=');
                        if (parts.Length == 2)
                        {
                            Control[] controls = this.Controls.Find(parts[0], false);
                            if (controls.Length > 0 && controls[0] is PictureBox pictureBox)
                            {
                                string filePath = parts[1];
                                if (File.Exists(filePath) || Directory.Exists(filePath))
                                {
                                    HandleFileDropped(pictureBox, filePath);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("加载配置失败：" + ex.Message);
            }
        }

        private void LoadConfig(string configFilePath)
        {
            try
            {
                // 清空现有图标
                foreach (var pictureBox in _savedFilePaths.Keys.ToList())
                {
                    pictureBox.Image = null;
                    _savedFilePaths[pictureBox] = string.Empty;
                    _isFolder[pictureBox] = false;
                }

                // 读取配置文件
                if (File.Exists(configFilePath))
                {
                    string[] lines = File.ReadAllLines(configFilePath);
                    foreach (string line in lines)
                    {
                        string[] parts = line.Split('=');
                        if (parts.Length == 2)
                        {
                            Control[] controls = this.Controls.Find(parts[0], false);
                            if (controls.Length > 0 && controls[0] is PictureBox pictureBox)
                            {
                                string filePath = parts[1];
                                if (File.Exists(filePath) || Directory.Exists(filePath))
                                {
                                    HandleFileDropped(pictureBox, filePath);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("加载配置失败：" + ex.Message);
            }
        }


        private Icon GetFileIcon(string filePath)
        {
            try
            {
                // 如果是快捷方式，获取目标路径
                if (Path.GetExtension(filePath).ToLower() == ".lnk")
                {
                    string targetPath = GetShortcutTargetFile(filePath);
                    if (File.Exists(targetPath))
                    {
                        filePath = targetPath;
                    }
                }

                // 如果文件不存在，显示提示并返回默认图标
                if (!File.Exists(filePath))
                {
                    MessageBox.Show(
                        "无法获取图标。如果这是一个程序，请尝试：\n\n" +
                        "1. 在文件资源管理器中找到程序\n" +
                        "2. 右键程序 -> 属性\n" +
                        "3. 查看 目标 位置\n" +
                        "4. 将目标位置的程序拖入此处\n\n" +
                        $"文件路径：{filePath}",
                        "提示",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                    return SystemIcons.Application;
                }

                // 文件存在，尝试获取图标
                try
                {
                    Icon icon = Icon.ExtractAssociatedIcon(filePath);
                    if (icon != null)
                    {
                        return icon;
                    }
                }
                catch
                {
                    // 如果 ExtractAssociatedIcon 失败，尝试 ExtractIconEx
                    IntPtr[] largeIcons = new IntPtr[1];
                    IntPtr[] smallIcons = new IntPtr[1];

                    int iconCount = ExtractIconEx(filePath, 0, largeIcons, smallIcons, 1);

                    if (iconCount > 0 && largeIcons[0] != IntPtr.Zero)
                    {
                        Icon icon = Icon.FromHandle(largeIcons[0]);
                        if (smallIcons[0] != IntPtr.Zero)
                        {
                            DestroyIcon(smallIcons[0]);
                        }
                        return icon;
                    }
                }

                // 如果所有方法都失败，返回默认图标
                return SystemIcons.Application;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                "无法获取图标。如果这是一个程序，请尝试：\n\n" +
                "1. 在文件资源管理器中找到程序\n" +
                "2. 右键程序 -> 属性\n" +
                "3. 查看 目标 位置\n" +
                "4. 将目标位置的程序拖入此处\n\n" +
                $"文件路径：{filePath}",
                "提示",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information
            );
                return SystemIcons.Application;
            }
        }

        private string GetShortcutTargetFile(string shortcutFilename)
        {
            try
            {
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(t);
                var shortcut = shell.CreateShortcut(shortcutFilename);
                string targetFile = shortcut.TargetPath;
                Marshal.FinalReleaseComObject(shell);
                return targetFile;
            }
            catch
            {
                return shortcutFilename;
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern int ExtractIconEx(string szFileName, int nIconIndex,
            IntPtr[] phiconLarge, IntPtr[] phiconSmall, int nIcons);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool DestroyIcon(IntPtr handle);

        private Icon GetFolderIcon()
        {
            IntPtr[] largeIcons = new IntPtr[1];
            IntPtr[] smallIcons = new IntPtr[1];

            // 获取系统文件夹图标 (3是文件夹的索引)
            ExtractIconEx("shell32.dll", 3, largeIcons, smallIcons, 1);

            if (largeIcons[0] != IntPtr.Zero)
            {
                Icon icon = Icon.FromHandle(largeIcons[0]);
                if (smallIcons[0] != IntPtr.Zero)
                {
                    DestroyIcon(smallIcons[0]);
                }
                return icon;
            }

            // 如果获取失败，使用默认方法
            return Icon.ExtractAssociatedIcon(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        }


        private void DrawIconWithName(PictureBox pictureBox, string filePath, Graphics g)
        {
            string fileName = Path.GetFileName(filePath);
            bool isFolder = Directory.Exists(filePath);
            string ext = Path.GetExtension(filePath).ToLower();

            // 检查是否是文本文件或图片文件
            bool isTxt = ext == ".txt";
            bool isImage = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".ico", ".tiff",
             ".webp",".url",".bat",".toml",".md",".json",".yaml",".yml",".xml",
              ".zip",".rdp" }
                .Contains(ext);

            bool shouldShowName = isFolder || isTxt || isImage;

            // 获取图标
            Icon icon = isFolder ? GetFolderIcon() : GetFileIcon(filePath);

            try
            {
                // 增加图标尺寸到64x64
                int iconSize = 50; // 从48改为64
                int iconY = (pictureBox.Height - iconSize) / 2;

                // 使用高质量渲染设置
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.CompositingQuality = CompositingQuality.HighQuality;
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                // 使用更高质量的图标绘制方法
                if (icon.Width >= 64)
                {
                    // 如果图标本身就很大，直接绘制
                    g.DrawIcon(icon, new Rectangle(
                        pictureBox.Width / 2 - iconSize / 2,
                        iconY,
                        iconSize,
                        iconSize));
                }
                else
                {
                    // 如果图标较小，使用 DrawImage 方法进行高质量缩放
                    using (Bitmap iconBmp = icon.ToBitmap())
                    {
                        g.DrawImage(iconBmp, 
                            new Rectangle(pictureBox.Width / 2 - iconSize / 2, iconY, iconSize, iconSize),
                            new Rectangle(0, 0, iconBmp.Width, iconBmp.Height),
                            GraphicsUnit.Pixel);
                    }
                }

                // 显示文件夹、txt文件和图片文件的名称
                if (shouldShowName)
                {
                    using (StringFormat sf = new StringFormat())
                    {
                        sf.Alignment = StringAlignment.Center;
                        sf.LineAlignment = StringAlignment.Center;
                        sf.Trimming = StringTrimming.EllipsisCharacter;

                        // 调整文本区域位置
                        Rectangle textRect = new Rectangle(
                            2,
                            iconY + (iconSize * 3 / 4),  // 从图标3/4处开始
                            pictureBox.Width - 4,
                            iconSize / 4);             // 占用图标下1/4部分

                        // 使用更大的字体 //draw text
                        using (Font font = new Font("微软雅黑", 9, FontStyle.Bold))
                        {
                            using (GraphicsPath path = new GraphicsPath())
                            {
                                // 使用更高的DPI值来渲染文字
                                float emSize = font.Size * g.DpiY / 72;
                                path.AddString(
                                    fileName,
                                    font.FontFamily,
                                    (int)font.Style,
                                    emSize,
                                    textRect,
                                    sf);

                                // 增加描边效果以提高可读性
                                using (Pen pen = new Pen(Color.Black, 3))
                                {
                                    pen.LineJoin = LineJoin.Round; // 使描边更圆滑
                                    g.DrawPath(pen, path);
                                }
                                g.FillPath(Brushes.White, path);
                            }
                        }
                    }
                }
            }
            finally
            {
                icon.Dispose();
            }
        }

        private void HandleFileDropped(PictureBox pictureBox, string filePath)
        {
            try
            {
                _savedFilePaths[pictureBox] = filePath;
                _isFolder[pictureBox] = Directory.Exists(filePath);

                // 创建2倍大小的位图以提高清晰度
                using (Bitmap bmp = new Bitmap(pictureBox.Width * 2, pictureBox.Height * 2))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        // 设置高质量缩放
                        g.ScaleTransform(2, 2);
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

                        DrawIconWithName(pictureBox, filePath, g);
                    }

                    // 缩放回原始大小
                    using (Bitmap finalBmp = new Bitmap(pictureBox.Width, pictureBox.Height))
                    {
                        using (Graphics g = Graphics.FromImage(finalBmp))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(bmp, 0, 0, pictureBox.Width, pictureBox.Height);
                        }
                        pictureBox.Image = finalBmp.Clone() as Image;

                        // 更新缓存
                        string currentPage = Path.GetFileNameWithoutExtension(currentconfig);
                        if (_pageImageCache.ContainsKey(currentPage))
                        {
                            if (_pageImageCache[currentPage].ContainsKey(pictureBox))
                            {
                                _pageImageCache[currentPage][pictureBox]?.Dispose();
                            }
                            _pageImageCache[currentPage][pictureBox] = finalBmp.Clone() as Image;
                            _pagePathCache[currentPage][pictureBox] = filePath;
                            _pageFolderCache[currentPage][pictureBox] = Directory.Exists(filePath);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("获取图标失败：" + ex.Message);
            }
        }

        private void SwapIcons(PictureBox source, PictureBox target)
        {
            // 保存目标框的数据
            string targetPath = _savedFilePaths.ContainsKey(target) ? _savedFilePaths[target] : string.Empty;
            bool targetIsFolder = _isFolder.ContainsKey(target) ? _isFolder[target] : false;
            Image targetImage = target.Image;

            // 将源框的数据复制到目标框
            if (_savedFilePaths.ContainsKey(source))
            {
                _savedFilePaths[target] = _savedFilePaths[source];
                _savedFilePaths[source] = targetPath;
            }

            if (_isFolder.ContainsKey(source))
            {
                _isFolder[target] = _isFolder[source];
                _isFolder[source] = targetIsFolder;
            }

            // 交换图像
            target.Image = source.Image;
            source.Image = targetImage;

            // 保存更改
            SaveConfig();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            SaveConfig();

            // 清理图片缓存
            foreach (var pageCache in _pageImageCache.Values)
            {
                foreach (var image in pageCache.Values)
                {
                    image?.Dispose();
                }
            }
        }

        private void exitbutton_Click(object sender, EventArgs e)
        {
            this.Hide();
        }

        private void Launcher_Load(object sender, EventArgs e)
        {

        }

        private void button1_Click(object sender, EventArgs e)
        {
            Form helpForm = new Form
            {
                Size = new Size(400, 300),
                FormBorderStyle = FormBorderStyle.None, // 去掉标题栏
                StartPosition = FormStartPosition.CenterParent,
                BackColor = Color.Black
            };
            helpForm.TopMost = true;

            Label helpLabel = new Label
            {
                Text = "帮助指南\n\n" +
                       "1. 拖动程序进来变成快速启动\n" +
                       "2. 左键打开程序\n" +
                       "3. 中键打开文件位置\n" +
                       "4. 右键删除图标\n" +
                       "5. 点击小火箭icon，打开配置文件位置\n" +
                       "6. 程序有四个页面，可以用AD/1234按键切换\n" +
                       "7. 右键托盘退出程序\n" +
                       "程序作者：kasusa",
                ForeColor = Color.White,
                Font = new Font("微软雅黑", 10),
                AutoSize = false,
                Size = new Size(360, 200),
                TextAlign = ContentAlignment.TopLeft,
                Location = new Point(20, 20)
            };

            // 自定义关闭按钮
            Button closeButton = new Button
            {
                Text = "关闭",
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.White,
                FlatStyle = FlatStyle.Flat,
                Size = new Size(80, 30),
                Location = new Point(160, 220)
            };
            closeButton.Click += (s, args) => helpForm.Close();

            helpForm.Controls.Add(helpLabel);
            helpForm.Controls.Add(closeButton);

            helpForm.ShowDialog();
        }



        private void notifyIcon1_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            if (this.Visible)
            {
                this.Hide();
            }
            else
            {
                this.Show();
                this.Activate();
            }
        }

        private void exitToolStripMenuItem_Click(object sender, EventArgs e)
        {
            this.Close();
        }

        private void notifyIcon1_MouseDoubleClick(object sender, EventArgs e)
        {

        }

        private void button2_Click(object sender, EventArgs e)
        {
            string exePath = Application.ExecutablePath;

            // 打开资源管理器并选中程序本体
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{exePath}\"");
        }

        private void button3_Click(object sender, EventArgs e)
        {
            if (this.TopMost == false)
            {
                this.TopMost = true;
                button3.ForeColor = Color.Yellow;
            }
            else
            {
                this.TopMost = false;
                button3.ForeColor = Color.Gray;
            }
        }

        //pencil button
        private void button4_Click(object sender, EventArgs e)
        {
            if (!panel2.Visible)
            {
                panel2.Left = 0;
                panel2.Top = 24;
                panel2.Show();
                button4.ForeColor = Color.Cyan;
                textBox1.Focus();
            }
            else
            {
                panel2.Hide();
                button4.ForeColor = Color.Gray;

            }

        }

        private void panel_dot1_Click(object sender, EventArgs e)
        {
            panel_dot1.BackColor = Color.Gray;
            panel_dot2.BackColor = Color.Gray;
            panel_dot3.BackColor = Color.Gray;
            panel_dot4.BackColor = Color.Gray;
            Panel clickedPanel = sender as Panel;
            clickedPanel.BackColor = Color.White;

            clearImageBoxes();
            currentconfig = clickedPanel.Name + ".txt";
            
            // 从缓存加载页面
            string pageName = clickedPanel.Name;
            LoadPageFromCache(pageName);
        }

        private void clearImageBoxes()
        {
            foreach (Control control in this.Controls)
            {
                if (control is PictureBox pictureBox)
                {
                    pictureBox.Image = null; // 清除 PictureBox 中的图像
                    _savedFilePaths[pictureBox] = string.Empty; // 清空保存的文件路径
                    _isFolder[pictureBox] = false; // 重置文件夹标记
                }
            }
        }

        private int _currentPage = 1;
        private int _totalPages = 4;
        private void Launcher_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.A)
            {
                // 左键，切换到上一页
                _currentPage--;
                if (_currentPage < 1)
                {
                    _currentPage = 4; // 如果当前是第一页，则切换到最后一页
                }
                UpdateCurrentPage();
            }
            else if (e.KeyCode == Keys.D)
            {
                // 右键，切换到下一页
                _currentPage++;
                if (_currentPage > 4)
                {
                    _currentPage = 1; // 如果当前是最后一页，则切换到第一页
                }
                UpdateCurrentPage();
            }
            else
            {
                switch (e.KeyCode)
                {
                    case Keys.D1: // 数字键 1
                        _currentPage = 1;
                        UpdateCurrentPage();
                        break;
                    case Keys.D2: // 数字键 2
                        _currentPage = 2;
                        UpdateCurrentPage();
                        break;
                    case Keys.D3: // 数字键 3
                        _currentPage = 3;
                        UpdateCurrentPage();
                        break;
                    case Keys.D4: // 数字键 4
                        _currentPage = 4;
                        UpdateCurrentPage();
                        break;
                }
            }

        }

        private void UpdateCurrentPage()
        {
            switch (_currentPage)
            {
                case 1:
                    panel_dot1_Click(panel_dot1, EventArgs.Empty);
                    break;
                case 2:
                    panel_dot1_Click(panel_dot2, EventArgs.Empty);
                    break;
                case 3:
                    panel_dot1_Click(panel_dot3, EventArgs.Empty);
                    break;
                case 4:
                    panel_dot1_Click(panel_dot4, EventArgs.Empty);
                    break;
            }
        }

        private void Form1_MouseWheel(object sender, MouseEventArgs e)
        {
            if (e.Delta > 0)
            {
                // 左键，切换到上一页
                _currentPage--;
                if (_currentPage < 1)
                {
                    _currentPage = 4; // 如果当前是第一页，则切换到最后一页
                }
                UpdateCurrentPage();
            }
            else
            {
                // 右键，切换到下一页
                _currentPage++;
                if (_currentPage > 4)
                {
                    _currentPage = 1; // 如果当前是最后一页，则切换到第一页
                }
                UpdateCurrentPage();
            }
        }

        private void InitializePageCaches()
        {
            string[] pages = new[] { "panel_dot1", "panel_dot2", "panel_dot3", "panel_dot4" };
            foreach (string page in pages)
            {
                _pageImageCache[page] = new Dictionary<PictureBox, Image>();
                _pagePathCache[page] = new Dictionary<PictureBox, string>();
                _pageFolderCache[page] = new Dictionary<PictureBox, bool>();
            }
        }

        private void PreloadAllPages()
        {
            string[] pages = new[] { "panel_dot1", "panel_dot2", "panel_dot3", "panel_dot4" };
            foreach (string page in pages)
            {
                LoadPageToCache(page + ".txt");
            }
        }

        private void LoadPageToCache(string configFile)
        {
            string pageName = Path.GetFileNameWithoutExtension(configFile);
            try
            {
                if (File.Exists(configFile))
                {
                    string[] lines = File.ReadAllLines(configFile);
                    foreach (string line in lines)
                    {
                        string[] parts = line.Split('=');
                        if (parts.Length == 2)
                        {
                            Control[] controls = this.Controls.Find(parts[0], false);
                            if (controls.Length > 0 && controls[0] is PictureBox pictureBox)
                            {
                                string filePath = parts[1];
                                if (File.Exists(filePath) || Directory.Exists(filePath))
                                {
                                    // 创建图标并保存到缓存
                                    using (Bitmap bmp = new Bitmap(pictureBox.Width, pictureBox.Height))
                                    {
                                        using (Graphics g = Graphics.FromImage(bmp))
                                        {
                                            g.SmoothingMode = SmoothingMode.AntiAlias;
                                            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
                                            DrawIconWithName(pictureBox, filePath, g);
                                        }
                                        _pageImageCache[pageName][pictureBox] = bmp.Clone() as Image;
                                        _pagePathCache[pageName][pictureBox] = filePath;
                                        _pageFolderCache[pageName][pictureBox] = Directory.Exists(filePath);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载页面 {pageName} 失败：{ex.Message}");
            }
        }

        private void LoadPageFromCache(string pageName)
        {
            if (_pageImageCache.ContainsKey(pageName))
            {
                foreach (var pair in _pageImageCache[pageName])
                {
                    pair.Key.Image = pair.Value;
                    _savedFilePaths[pair.Key] = _pagePathCache[pageName][pair.Key];
                    _isFolder[pair.Key] = _pageFolderCache[pageName][pair.Key];
                }
            }
        }
    }
}
