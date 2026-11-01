using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace AtlasView
{
    public sealed class AtlasCanvas : Control
    {
        public Bitmap Image;
        public float Zoom = 1, PanX = 24, PanY = 24;
        public int Channel, BackgroundMode, CellW = 32, CellH = 32;
        public bool Grid;
        public Rectangle Selection
        {
            get; set;
        }
        public Action<string> Hover;
        public Action<Point> ImageDoubleClicked, ImageClicked;
        public List<Rectangle> Highlights = new List<Rectangle>();
        private Point selectionStart, previousMousePosition, clickStart;
        private bool isSelecting, isPanning;
        public AtlasCanvas()
        {
            DoubleBuffered = true;
            Dock = DockStyle.Fill;
            BackColor = Color.FromArgb(20, 23, 29);
            SetStyle(ControlStyles.Selectable, true);
            TabStop = true;
        }
        public void Fit()
        {
            if (Image == null)
            {
                return;
            }

            Zoom = Math.Min(8, Math.Min((Width - 48f) / Image.Width, (Height - 48f) / Image.Height));
            Zoom = Math.Max(0.025f, Zoom);
            PanX = (Width - Image.Width * Zoom) / 2;
            PanY = (Height - Image.Height * Zoom) / 2;
            Invalidate();
        }
        public void SetZoom(float value)
        {
            float previousZoom = Zoom;
            Zoom = Math.Max(0.025f, Math.Min(32, value));
            PanX = Width / 2f - (Width / 2f - PanX) * Zoom / previousZoom;
            PanY = Height / 2f - (Height / 2f - PanY) * Zoom / previousZoom;
            Invalidate();
        }
        Point Pixel(Point imagePoint)
        {
            return new Point((int)Math.Floor((imagePoint.X - PanX) / Zoom), (int)Math.Floor((imagePoint.Y - PanY) / Zoom));
        }
        protected override void OnMouseDown(MouseEventArgs eventArgs)
        {
            base.OnMouseDown(eventArgs);
            Focus();
            if (Image == null)
            {
                return;
            }

            previousMousePosition = eventArgs.Location;
            if (eventArgs.Button == MouseButtons.Right || eventArgs.Button == MouseButtons.Middle)
            {
                isPanning = true;
            }
            else if (eventArgs.Button == MouseButtons.Left)
            {
                isSelecting = true;
                clickStart = eventArgs.Location;
                selectionStart = Pixel(eventArgs.Location);
                Selection = Rectangle.Empty;
            }
            Capture = true;
        }
        protected override void OnMouseMove(MouseEventArgs eventArgs)
        {
            base.OnMouseMove(eventArgs);
            if (Image == null)
            {
                return;
            }

            Point imagePoint = Pixel(eventArgs.Location);
            if (isPanning)
            {
                PanX += eventArgs.X - previousMousePosition.X;
                PanY += eventArgs.Y - previousMousePosition.Y;
                previousMousePosition = eventArgs.Location;
                Invalidate();
            }
            if (isSelecting)
            {
                int selectionLeft = Math.Max(0, Math.Min(selectionStart.X, imagePoint.X)), selectionTop = Math.Max(0, Math.Min(selectionStart.Y, imagePoint.Y)), selectionRight = Math.Min(Image.Width, Math.Max(selectionStart.X, imagePoint.X) + 1), selectionBottom = Math.Min(Image.Height, Math.Max(selectionStart.Y, imagePoint.Y) + 1);
                Selection = selectionRight > selectionLeft && selectionBottom > selectionTop ? Rectangle.FromLTRB(selectionLeft, selectionTop, selectionRight, selectionBottom) : Rectangle.Empty;
                Invalidate();
            }
            if (Hover != null && imagePoint.X >= 0 && imagePoint.Y >= 0 && imagePoint.X < Image.Width && imagePoint.Y < Image.Height)
            {
                var pixelColor = Image.GetPixel(imagePoint.X, imagePoint.Y);
                Hover(string.Format("X {0}   Y {1}    RGBA {2}, {3}, {4}, {5}    확대 {6:0.#}%    선택 {7},{8}  {9}×{10}", imagePoint.X, imagePoint.Y, pixelColor.R, pixelColor.G, pixelColor.B, pixelColor.A, Zoom * 100, Selection.X, Selection.Y, Selection.Width, Selection.Height));
            }
        }
        protected override void OnMouseUp(MouseEventArgs eventArgs)
        {
            bool click = isSelecting && eventArgs.Button == MouseButtons.Left && Math.Abs(eventArgs.X - clickStart.X) <= 3 && Math.Abs(eventArgs.Y - clickStart.Y) <= 3;
            isSelecting = isPanning = false;
            Capture = false;
            base.OnMouseUp(eventArgs);
            if (click && Image != null && ImageClicked != null)
            {
                ImageClicked(Pixel(eventArgs.Location));
            }
        }
        protected override void OnMouseDoubleClick(MouseEventArgs eventArgs)
        {
            base.OnMouseDoubleClick(eventArgs);
            if (eventArgs.Button == MouseButtons.Left && Image != null && ImageDoubleClicked != null)
            {
                ImageDoubleClicked(Pixel(eventArgs.Location));
            }
        }
        protected override void OnMouseWheel(MouseEventArgs eventArgs)
        {
            base.OnMouseWheel(eventArgs);
            float previousZoom = Zoom;
            Zoom = Math.Max(0.025f, Math.Min(32, Zoom * (eventArgs.Delta > 0 ? 1.25f : 0.8f)));
            PanX = eventArgs.X - (eventArgs.X - PanX) * Zoom / previousZoom;
            PanY = eventArgs.Y - (eventArgs.Y - PanY) * Zoom / previousZoom;
            Invalidate();
        }
        protected override void OnPaint(PaintEventArgs eventArgs)
        {
            base.OnPaint(eventArgs);
            var graphics = eventArgs.Graphics;
            if (Image == null)
            {
                using (var font = new Font("Malgun Gothic", 15))
                using (var brush = new SolidBrush(Color.FromArgb(160, 175, 191)))
                {
                    graphics.DrawString("PS3 ATLAS VIEWER\n\n복호화된 폴더를 열고 텍스처를 선택하세요.\n\n휠: 확대  ·  오른쪽 드래그: 이동\n왼쪽 드래그: 영역 선택", font, brush, new RectangleF(40, 60, Math.Max(1, Width - 80), Height - 80));
                }

                return;
            }
            var dest = new RectangleF(PanX, PanY, Image.Width * Zoom, Image.Height * Zoom);
            var clip = RectangleF.Intersect(dest, ClientRectangle);
            graphics.SetClip(clip);
            Color backgroundColor = BackgroundMode == 1 ? Color.Black : BackgroundMode == 2 ? Color.White : Color.FromArgb(48, 53, 63);
            using (var brush = new SolidBrush(backgroundColor))
            {
                graphics.FillRectangle(brush, clip);
            }

            if (BackgroundMode == 0)
            {
                using (var brush = new SolidBrush(Color.FromArgb(64, 70, 81)))
                {
                    for (int y = (int)clip.Top / 16 * 16; y < clip.Bottom; y += 16)
                    {
                        for (int x = (int)clip.Left / 16 * 16; x < clip.Right; x += 16)
                        {
                            if (((x / 16 + y / 16) & 1) == 0)
                            {
                                graphics.FillRectangle(brush, x, y, 16, 16);
                            }
                        }
                    }
                }
            }
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            using (var imageAttributes = new ImageAttributes())
            {
                var colorMatrix = new ColorMatrix();
                if (Channel == 1)
                {
                    colorMatrix.Matrix33 = 0;
                }

                colorMatrix.Matrix43 = Channel == 1 ? 1 : 0;
                if (Channel >= 2)
                {
                    colorMatrix = new ColorMatrix(new float[][] { new float[5], new float[5], new float[5], new float[5], new float[] { 0, 0, 0, 1, 1 } });
                    int sourceChannel = Channel == 2 ? 3 : Channel - 3;
                    colorMatrix[sourceChannel, 0] = 1;
                    colorMatrix[sourceChannel, 1] = 1;
                    colorMatrix[sourceChannel, 2] = 1;
                }
                imageAttributes.SetColorMatrix(colorMatrix);
                graphics.DrawImage(Image, Rectangle.Round(dest), 0, 0, Image.Width, Image.Height, GraphicsUnit.Pixel, imageAttributes);
            }
            if (Grid && CellW * Zoom >= 4 && CellH * Zoom >= 4)
            {
                using (var pen = new Pen(Color.FromArgb(150, 65, 215, 194)))
                {
                    int firstGridColumn = Math.Max(0, (int)((clip.Left - PanX) / Zoom / CellW)), firstGridRow = Math.Max(0, (int)((clip.Top - PanY) / Zoom / CellH));
                    for (int x = firstGridColumn * CellW; x <= Image.Width && PanX + x * Zoom <= clip.Right; x += CellW)
                    {
                        graphics.DrawLine(pen, PanX + x * Zoom, clip.Top, PanX + x * Zoom, clip.Bottom);
                    }

                    for (int y = firstGridRow * CellH; y <= Image.Height && PanY + y * Zoom <= clip.Bottom; y += CellH)
                    {
                        graphics.DrawLine(pen, clip.Left, PanY + y * Zoom, clip.Right, PanY + y * Zoom);
                    }
                }
            }

            using (var pen = new Pen(Color.FromArgb(95, 222, 201), 3))
            {
                foreach (var cellBounds in Highlights)
                {
                    graphics.DrawRectangle(pen, PanX + cellBounds.X * Zoom, PanY + cellBounds.Y * Zoom, Math.Max(1, cellBounds.Width * Zoom - 2), Math.Max(1, cellBounds.Height * Zoom - 2));
                }
            }

            if (!Selection.IsEmpty)
            {
                using (var pen = new Pen(Color.FromArgb(255, 202, 104), 2))
                {
                    graphics.DrawRectangle(pen, PanX + Selection.X * Zoom, PanY + Selection.Y * Zoom, Selection.Width * Zoom, Selection.Height * Zoom);
                }
            }

            graphics.ResetClip();
        }
    }

    public sealed class RawDialog : Form
    {
        public Texture Result;
        TextBox offsetInput = new TextBox { Text = "0x0", Width = 220 };
        NumericUpDown widthInput = Number(256, 1, 8192), heightInput = Number(256, 1, 8192), rowPitchInput = Number(0, 0, 1048576);
        ComboBox formatSelector = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
        CheckBox swizzle = new CheckBox { Text = "Morton 스위즐 (비압축)", AutoSize = true };
        Texture inputTexture;
        static NumericUpDown Number(int initialValue, int minimum, int maximum)
        {
            return new NumericUpDown { Minimum = minimum, Maximum = maximum, Value = initialValue, Width = 220 };
        }
        public RawDialog(Texture selected)
        {
            inputTexture = selected;
            Text = "RAW 해석 — 원본 수정 없음";
            Size = new Size(480, 410);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            Font = new Font("Malgun Gothic", 9);
            formatSelector.Items.AddRange(new object[] { "RGBA8", "ARGB8", "BGRA8", "L8", "A8", "BC1", "BC2", "BC3" });
            formatSelector.SelectedIndex = 0;
            var table = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 8 };
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150));
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var note = new Label { Text = "오프셋은 선택 항목 기준입니다.\nLZP2 항목에서는 해제된 데이터 기준입니다.", AutoSize = true };
            table.Controls.Add(note, 0, 0);
            table.SetColumnSpan(note, 2);
            string[] names = { "오프셋 (16진/10진)", "너비", "높이", "포맷", "행 피치 (0=자동)" };
            Control[] inputControls = { offsetInput, widthInput, heightInput, formatSelector, rowPitchInput };
            for (int controlIndex = 0; controlIndex < inputControls.Length; controlIndex++)
            {
                table.Controls.Add(new Label { Text = names[controlIndex], AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, 0, controlIndex + 1);
                table.Controls.Add(inputControls[controlIndex], 1, controlIndex + 1);
            }
            table.Controls.Add(swizzle, 1, 6);
            var previewButton = new Button { Text = "미리보기", AutoSize = true };
            previewButton.Click += delegate
            {
                try
                {
                    Build();
                    DialogResult = DialogResult.OK;
                }
                catch (Exception exception) { MessageBox.Show(this, exception.Message, "RAW 설정"); }
            };
            table.Controls.Add(previewButton, 1, 7);
            Controls.Add(table);
            AcceptButton = previewButton;
            if (selected.Width > 0 && selected.Width <= 8192)
            {
                widthInput.Value = selected.Width;
            }

            if (selected.Height > 0 && selected.Height <= 8192)
            {
                heightInput.Value = selected.Height;
            }

            offsetInput.Text = "0x" + selected.DataOffset.ToString("X");
            if (selected.Format != null && formatSelector.Items.Contains(selected.Format))
            {
                formatSelector.SelectedItem = selected.Format;
            }
        }
        internal void Build()
        {
            string text = offsetInput.Text.Trim();
            long dataOffset = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? Convert.ToInt64(text.Substring(2), 16) : long.Parse(text);
            Bin.Need(dataOffset >= 0, "음수 오프셋은 사용할 수 없습니다.");
            string format = (string)formatSelector.SelectedItem;
            var texture = new Texture { Source = inputTexture.Source, Name = "RAW", Container = "수동 해석", Format = format, Width = (int)widthInput.Value, Height = (int)heightInput.Value, Pitch = (int)rowPitchInput.Value, BytesPerPixel = (format == "L8" || format == "A8") ? 1 : 4, Swizzle = swizzle.Checked, Note = "사용자가 지정한 RAW 해석 · 자동 검증된 포맷 아님" };
            int length = Pixels.ByteCount(texture);
            Bin.Need(!texture.Swizzle || !format.StartsWith("BC"), "BC 블록 스위즐은 미지원");
            if (inputTexture.Source.Lzp)
            {
                Bin.Need(dataOffset <= int.MaxValue, "해제 버퍼 오프셋 초과");
                texture.DataOffset = (int)dataOffset;
                texture.DataLength = length;
            }
            else
            {
                long fileLength = new FileInfo(inputTexture.Source.Path).Length;
                Bin.Need(dataOffset <= fileLength - inputTexture.Source.Offset - length, "RAW 읽기 범위 초과");
                texture.Source = new Source { Path = inputTexture.Source.Path, Offset = checked(inputTexture.Source.Offset + dataOffset), Length = length, Label = inputTexture.Source.Label + " RAW @0x" + dataOffset.ToString("X") };
                texture.DataLength = length;
            }
            Result = texture;
        }
    }

    public sealed class MainForm : Form
    {
        TextBox folderPathInput = new TextBox(), filterInput = new TextBox(), resourceInfoText = new TextBox();
        Button chooseFolderButton, rescanButton, cancelScanButton;
        CheckBox searchFileContentsCheckBox = new CheckBox { Text = "파일 내부 검색", AutoSize = true, Checked = true }, includeUnsupportedCheckBox = new CheckBox { Text = "미지원 포함", AutoSize = true };
        ListView list = new ListView();
        AtlasCanvas canvas = new AtlasCanvas();
        Label statusLabel = new Label(), positionLabel = new Label(), headingLabel = new Label();
        ComboBox channelSelector = new ComboBox(), backgroundSelector = new ComboBox();
        ScanResult result;
        List<ResourceGroup> visible = new List<ResourceGroup>();
        CancellationTokenSource scanCancellationSource, previewCancel;
        int previewGeneration;
        Texture currentTexture;
        ResourceGroup currentResource;
        bool resourceOverview;
        List<Rectangle> resourceCells = new List<Rectangle>();
        string initialFolderPath;
        readonly PreviewLoader loader = new PreviewLoader();
        readonly HiddenImageStore hidden = new HiddenImageStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings", "hidden-images.json"), true);
        readonly TranslationStore translations = new TranslationStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "untranslated"));
        string currentSourceStamp;
        HashSet<string> recordedResourceIds = new HashSet<string>();
        readonly string settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings", "viewer.json");
        readonly NumericUpDown batchSizeInput = new NumericUpDown { Minimum = 1, Maximum = 32, Value = 8, Width = 55 };
        ResourceBatchPreview batchPreview;
        bool isBatchOverview, isSyncingSelection;
        Button previousBatchButton, nextBatchButton, hideBatchButton, recordBatchButton;
        Label batchStatusLabel = new Label { AutoSize = true, Padding = new Padding(4, 8, 0, 0) };
        readonly ResourcePage pages = new ResourcePage();
        Label pageStatusLabel = new Label();
        Button previousPageButton, nextPageButton;
        readonly CheckBox hideCurrentCheckBox = new CheckBox { Text = "리소스 전체 숨기기 (H)", AutoSize = true, Enabled = false, Margin = new Padding(8, 9, 4, 3) };
        public MainForm(string path)
        {
            initialFolderPath = path;
            Text = "PS3 Atlas Viewer";
            Size = new Size(1440, 940);
            MinimumSize = new Size(1024, 700);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Malgun Gothic", 9);
            BackColor = Color.FromArgb(28, 32, 40);
            ForeColor = Color.FromArgb(223, 230, 237);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(16) };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
            Controls.Add(layout);
            var title = new Label { Text = "ATLAS / PS3", Font = new Font("Segoe UI", 23, FontStyle.Bold), Dock = DockStyle.Fill, ForeColor = Color.FromArgb(95, 222, 201) };
            layout.Controls.Add(title, 0, 0);
            var folderToolbar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6 };
            folderToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            folderToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            folderToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
            folderToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 85));
            folderToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140));
            folderToolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 10));
            chooseFolderButton = Button("폴더 선택", Choose);
            rescanButton = Button("다시 검색", delegate
            {
                BeginScan(folderPathInput.Text);
            });
            cancelScanButton = Button("중지", delegate
            {
                if (scanCancellationSource != null)
                {
                    scanCancellationSource.Cancel();
                }
            });
            cancelScanButton.Enabled = false;
            folderPathInput.Dock = DockStyle.Fill;
            folderPathInput.Margin = new Padding(4, 6, 8, 0);
            searchFileContentsCheckBox.Margin = new Padding(10, 8, 0, 0);
            folderToolbar.Controls.Add(chooseFolderButton, 0, 0);
            folderToolbar.Controls.Add(folderPathInput, 1, 0);
            folderToolbar.Controls.Add(rescanButton, 2, 0);
            folderToolbar.Controls.Add(cancelScanButton, 3, 0);
            folderToolbar.Controls.Add(searchFileContentsCheckBox, 4, 0);
            layout.Controls.Add(folderToolbar, 0, 1);
            var split = new SplitContainer { Dock = DockStyle.Fill, SplitterWidth = 8, BackColor = BackColor, Size = new Size(1380, 720), SplitterDistance = 430, Panel1MinSize = 310, Panel2MinSize = 500 };
            layout.Controls.Add(split, 0, 2);
            var left = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            left.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
            split.Panel1.Controls.Add(left);
            headingLabel.Text = "리소스 탐색";
            headingLabel.Dock = DockStyle.Fill;
            left.Controls.Add(headingLabel, 0, 0);
            var search = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
            filterInput.Dock = DockStyle.Fill;
            filterInput.TextChanged += delegate
            {
                Refilter();
            };
            includeUnsupportedCheckBox.CheckedChanged += delegate
            {
                Refilter();
            };
            search.Controls.Add(filterInput, 0, 0);
            search.Controls.Add(includeUnsupportedCheckBox, 1, 0);
            left.Controls.Add(search, 0, 1);
            list.Dock = DockStyle.Fill;
            list.View = View.Details;
            list.FullRowSelect = true;
            list.HideSelection = false;
            list.MultiSelect = false;
            list.VirtualMode = true;
            list.BackColor = Color.FromArgb(34, 39, 48);
            list.ForeColor = ForeColor;
            list.BorderStyle = BorderStyle.None;
            list.Columns.Add("파일 / 내부 리소스 위치", 265);
            list.Columns.Add("이미지 수", 75);
            list.Columns.Add("포맷", 100);
            list.RetrieveVirtualItem += delegate (object sender, RetrieveVirtualItemEventArgs eventArgs)
            {
                if (eventArgs.ItemIndex >= pages.Items.Count)
                {
                    eventArgs.Item = new ListViewItem();
                    return;
                }
                var group = pages.Items[eventArgs.ItemIndex];
                eventArgs.Item = new ListViewItem(new[] { group.Label, group.Textures.Count + "개", string.Join(", ", group.Textures.Select(texture => texture.Format).Distinct()) });
                if (!group.Supported)
                {
                    eventArgs.Item.ForeColor = Color.FromArgb(230, 172, 111);
                }
            };
            var previewQueue = new SelectionPreviewQueue(this, delegate
            {
                if (!isSyncingSelection)
                {
                    PreviewSelected(false);
                }
            });
            list.SelectedIndexChanged += delegate
            {
                if (!isSyncingSelection)
                {
                    previewQueue.Schedule();
                }
            };
            left.Controls.Add(list, 0, 2);
            var bottom = new FlowLayoutPanel { Dock = DockStyle.Fill };
            bottom.Controls.Add(Button("목록 CSV", SaveList));
            bottom.Controls.Add(Button("RAW 해석", Raw));
            var manage = Button("숨김 목록", ManageHidden);
            bottom.Controls.Add(manage);
            bottom.Controls.Add(hideCurrentCheckBox);
            hideCurrentCheckBox.CheckedChanged += delegate
            {
                if (hideCurrentCheckBox.Checked)
                {
                    HideCurrent();
                }
            };
            left.Controls.Add(bottom, 0, 4);
            var paging = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3 };
            paging.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            paging.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            paging.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 80));
            previousPageButton = Button("이전", delegate
            {
                MovePage(-1);
            });
            nextPageButton = Button("다음", delegate
            {
                MovePage(1);
            });
            pageStatusLabel.Dock = DockStyle.Fill;
            pageStatusLabel.TextAlign = ContentAlignment.MiddleCenter;
            paging.Controls.Add(previousPageButton, 0, 0);
            paging.Controls.Add(pageStatusLabel, 1, 0);
            paging.Controls.Add(nextPageButton, 2, 0);
            left.Controls.Add(paging, 0, 3);
            UpdatePage();
            var right = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6 };
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 125));
            right.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
            split.Panel2.Controls.Add(right);
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            toolbar.Controls.Add(Button("맞춤", delegate
            {
                canvas.Fit();
            }));
            toolbar.Controls.Add(Button("1:1", delegate
            {
                canvas.SetZoom(1);
            }));
            toolbar.Controls.Add(Button("−", delegate
            {
                canvas.SetZoom(canvas.Zoom / 1.25f);
            }));
            toolbar.Controls.Add(Button("+", delegate
            {
                canvas.SetZoom(canvas.Zoom * 1.25f);
            }));
            SetupCombo(channelSelector, new[] { "RGBA", "RGB (불투명)", "알파", "R", "G", "B" }, 120);
            SetupCombo(backgroundSelector, new[] { "체커 배경", "검정 배경", "흰색 배경" }, 110);
            channelSelector.SelectedIndexChanged += delegate
            {
                canvas.Channel = channelSelector.SelectedIndex;
                canvas.Invalidate();
            };
            backgroundSelector.SelectedIndexChanged += delegate
            {
                canvas.BackgroundMode = backgroundSelector.SelectedIndex;
                canvas.Invalidate();
            };
            toolbar.Controls.Add(channelSelector);
            toolbar.Controls.Add(backgroundSelector);
            right.Controls.Add(toolbar, 0, 0);
            right.Controls.Add(canvas, 0, 1);
            var options = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            var grid = new CheckBox { Text = "격자", AutoSize = true, Margin = new Padding(3, 9, 3, 3) };
            var cellWidthInput = new NumericUpDown { Minimum = 1, Maximum = 4096, Value = 32, Width = 60 };
            var cellHeightInput = new NumericUpDown { Minimum = 1, Maximum = 4096, Value = 32, Width = 60 };
            grid.CheckedChanged += delegate
            {
                canvas.Grid = grid.Checked;
                canvas.Invalidate();
            };
            cellWidthInput.ValueChanged += delegate
            {
                canvas.CellW = (int)cellWidthInput.Value;
                canvas.Invalidate();
            };
            cellHeightInput.ValueChanged += delegate
            {
                canvas.CellH = (int)cellHeightInput.Value;
                canvas.Invalidate();
            };
            options.Controls.Add(grid);
            options.Controls.Add(cellWidthInput);
            options.Controls.Add(new Label { Text = "×", AutoSize = true, Padding = new Padding(0, 7, 0, 0) });
            options.Controls.Add(cellHeightInput);
            options.Controls.Add(Button("PNG 저장", delegate
            {
                Export(false);
            }));
            options.Controls.Add(Button("선택 영역 저장", delegate
            {
                Export(true);
            }));
            right.Controls.Add(options, 0, 2);
            var translationBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
            translationBar.Controls.Add(Button("리소스 전체 보기", delegate
            {
                if (currentResource != null)
                {
                    PreviewResource(currentResource);
                }
            }));
            translationBar.Controls.Add(Button("리소스 미번역 기록", RecordUntranslated));
            translationBar.Controls.Add(Button("미번역 목록 / 전달", ManageTranslations));
            right.Controls.Add(translationBar, 0, 3);
            var batchBar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true };
            batchBar.Controls.Add(new Label { Text = "묶음 크기", AutoSize = true, Padding = new Padding(0, 8, 0, 0) });
            batchBar.Controls.Add(batchSizeInput);
            batchSizeInput.Margin = new Padding(3, 6, 3, 3);
            previousBatchButton = Button("이전 묶음", () => MoveBatch(-1));
            nextBatchButton = Button("다음 묶음", () => MoveBatch(1));
            batchBar.Controls.Add(previousBatchButton);
            batchBar.Controls.Add(nextBatchButton);
            var showBatch = Button("묶음 보기", () => PreviewSelected(true));
            batchBar.Controls.Add(showBatch);
            batchBar.SetFlowBreak(showBatch, true);
            hideBatchButton = Button("묶음 전체 숨기기", HideBatch);
            recordBatchButton = Button("묶음 전체 미번역 기록", RecordBatch);
            batchBar.Controls.Add(hideBatchButton);
            batchBar.Controls.Add(recordBatchButton);
            batchBar.Controls.Add(batchStatusLabel);
            right.Controls.Add(batchBar, 0, 5);
            UpdateBatchControls();
            batchSizeInput.ValueChanged += delegate
            {
                if (previewCancel == null && scanCancellationSource == null)
                {
                    PreviewSelected(true);
                }
            };
            canvas.ImageClicked = delegate (Point imagePoint)
            {
                if (!isBatchOverview || batchPreview == null || previewCancel != null)
                {
                    return;
                }

                var cell = batchPreview.Cells.FirstOrDefault(batchCell => batchCell.Bounds.Contains(imagePoint));
                if (cell != null)
                {
                    ActivateBatchResource(cell.Group);
                    SelectResource(cell.Group, false);
                }
            };
            canvas.ImageDoubleClicked = delegate (Point imagePoint)
            {
                if (isBatchOverview && batchPreview != null)
                {
                    var cell = batchPreview.Cells.FirstOrDefault(batchCell => batchCell.Bounds.Contains(imagePoint));
                    if (cell != null)
                    {
                        BeginInvoke((Action)delegate
                        {
                            ActivateBatchResource(cell.Group);
                            SelectResource(cell.Group, false);
                            resourceOverview = false;
                            isBatchOverview = false;
                            Preview(cell.Texture);
                        });
                    }

                    return;
                }
                if (!resourceOverview || currentResource == null)
                {
                    return;
                }

                int textureIndex = resourceCells.FindIndex(bounds => bounds.Contains(imagePoint));
                if (textureIndex >= 0)
                {
                    Texture texture = currentResource.Textures[textureIndex];
                    BeginInvoke((Action)delegate
                    {
                        resourceOverview = false;
                        Preview(texture);
                    });
                }
            };
            resourceInfoText.Dock = DockStyle.Fill;
            resourceInfoText.Multiline = true;
            resourceInfoText.ReadOnly = true;
            resourceInfoText.ScrollBars = ScrollBars.Vertical;
            resourceInfoText.BorderStyle = BorderStyle.None;
            resourceInfoText.BackColor = Color.FromArgb(34, 39, 48);
            resourceInfoText.ForeColor = ForeColor;
            right.Controls.Add(resourceInfoText, 0, 4);
            statusLabel.Text = "폴더를 선택하세요. 모든 입력 파일은 읽기 전용으로 엽니다.";
            statusLabel.Dock = DockStyle.Fill;
            positionLabel.Dock = DockStyle.Fill;
            positionLabel.ForeColor = Color.FromArgb(140, 162, 182);
            canvas.Hover = delegate (string statusMessage)
            {
                positionLabel.Text = statusMessage;
            };
            layout.Controls.Add(statusLabel, 0, 3);
            layout.Controls.Add(positionLabel, 0, 4);
            AllowDrop = true;
            DragEnter += delegate (object eventSender, DragEventArgs eventArgs)
            {
                if (eventArgs.Data.GetDataPresent(DataFormats.FileDrop))
                {
                    eventArgs.Effect = DragDropEffects.Copy;
                }
            };
            DragDrop += delegate (object eventSender, DragEventArgs eventArgs)
            {
                var paths = (string[])eventArgs.Data.GetData(DataFormats.FileDrop);
                if (paths.Length > 0 && Directory.Exists(paths[0]))
                {
                    BeginScan(paths[0]);
                }
            };
            FormClosing += delegate
            {
                if (scanCancellationSource != null)
                {
                    scanCancellationSource.Cancel();
                }

                if (previewCancel != null)
                {
                    previewCancel.Cancel();
                }

                previewGeneration++;
            };
            FormClosed += delegate
            {
                if (canvas.Image != null)
                {
                    canvas.Image.Dispose();
                }
            };
            Shown += delegate
            {
                ReloadRecordedResources();
                if (!string.IsNullOrWhiteSpace(initialFolderPath))
                {
                    BeginScan(initialFolderPath);
                }
                else
                {
                    try
                    {
                        folderPathInput.Text = ViewerSettings.ReadFolder(settingsPath);
                    }
                    catch (Exception exception) { MessageBox.Show(this, exception.Message, "이전 폴더 설정 읽기 오류"); }
                    statusLabel.Text = folderPathInput.Text.Length == 0 ? "폴더를 선택하세요." : "이전 폴더 경로를 불러왔습니다. 다시 검색을 누르세요.";
                }
            };
        }
        static Button Button(string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(44, 51, 62), ForeColor = Color.FromArgb(223, 230, 237), Margin = new Padding(3) };
            button.FlatAppearance.BorderColor = Color.FromArgb(66, 78, 91);
            button.Click += delegate
            {
                action();
            };
            return button;
        }
        static void SetupCombo(ComboBox comboBox, string[] values, int width)
        {
            comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox.Items.AddRange(values);
            comboBox.SelectedIndex = 0;
            comboBox.Width = width;
            comboBox.Margin = new Padding(3, 5, 3, 3);
        }
        void Choose()
        {
            using (var dialog = new FolderBrowserDialog { Description = "복호화된 PS3 게임 폴더 또는 리소스 폴더", ShowNewFolderButton = false, SelectedPath = Directory.Exists(folderPathInput.Text) ? folderPathInput.Text : "" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    BeginScan(dialog.SelectedPath);
                }
            }
        }
        void SelectResource(ResourceGroup group, bool preview)
        {
            int resourceIndex = visible.FindIndex(resourceGroup => resourceGroup.Id == group.Id);
            if (resourceIndex < 0)
            {
                return;
            }

            isSyncingSelection = true;
            try
            {
                if (pages.Number != resourceIndex / ResourcePage.Size)
                {
                    pages.Move(resourceIndex / ResourcePage.Size);
                    UpdatePage();
                }
                list.SelectedIndices.Clear();
                list.SelectedIndices.Add(resourceIndex % ResourcePage.Size);
                list.EnsureVisible(resourceIndex % ResourcePage.Size);
            }
            finally { isSyncingSelection = false; }
            if (preview && IsHandleCreated)
            {
                BeginInvoke((Action)delegate
                {
                    if (!IsDisposed)
                    {
                        PreviewSelected(false);
                    }
                });
            }
        }
        List<ResourceGroup> BatchTargets()
        {
            if (canvas.Image == null)
            {
                return new List<ResourceGroup>();
            }

            return (batchPreview == null ? (currentResource == null ? new List<ResourceGroup>() : new List<ResourceGroup> { currentResource }) : batchPreview.Groups).Where(resourceGroup => !Excluded(resourceGroup)).ToList();
        }
        void UpdateBatchControls()
        {
            if (previousBatchButton == null)
            {
                return;
            }

            int resourceIndex = currentResource == null ? -1 : visible.FindIndex(resourceGroup => resourceGroup.Id == currentResource.Id), size = (int)batchSizeInput.Value;
            bool busy = previewCancel != null || scanCancellationSource != null;
            previousBatchButton.Enabled = !busy && resourceIndex >= size;
            nextBatchButton.Enabled = !busy && resourceIndex >= 0 && (resourceIndex / size + 1) * size < visible.Count;
            batchSizeInput.Enabled = !busy;
            int count = BatchTargets().Count;
            hideBatchButton.Enabled = recordBatchButton.Enabled = !busy && count > 0;
            batchStatusLabel.Text = resourceIndex < 0 ? "" : (resourceIndex / size + 1) + " / " + Math.Max(1, (visible.Count + size - 1) / size) + " 묶음 · 표시 " + count + "개";
        }
        void MoveBatch(int delta)
        {
            if (previewCancel != null || scanCancellationSource != null || visible.Count == 0)
            {
                return;
            }

            int size = (int)batchSizeInput.Value, resourceIndex = currentResource == null ? 0 : visible.FindIndex(resourceGroup => resourceGroup.Id == currentResource.Id);
            int start = Math.Max(0, Math.Min((visible.Count - 1) / size, Math.Max(0, resourceIndex) / size + delta)) * size;
            SelectResource(visible[start], true);
        }
        void PreviewSelected(bool force)
        {
            if (previewCancel != null || scanCancellationSource != null || list.SelectedIndices.Count == 0 || list.SelectedIndices[0] >= pages.Items.Count)
            {
                return;
            }

            var selected = pages.Items[list.SelectedIndices[0]];
            int resourceIndex = visible.FindIndex(resourceGroup => resourceGroup.Id == selected.Id);
            var groups = ResourceBatch.Slice(visible, resourceIndex, (int)batchSizeInput.Value);
            if (groups.Count == 0)
            {
                return;
            }

            if (groups.Count == 1)
            {
                PreviewResource(selected);
                return;
            }
            if (!force && isBatchOverview && batchPreview != null && groups.Select(resourceGroup => resourceGroup.Id).SequenceEqual(batchPreview.Groups.Select(resourceGroup => resourceGroup.Id)))
            {
                ActivateBatchResource(selected);
                return;
            }
            ClearPreview();
            previewCancel = new CancellationTokenSource();
            var cancellationSource = previewCancel;
            UpdateBatchControls();
            try
            {
                var loaded = LoadingDialog.Run(this, "읽는 중 — 리소스 묶음", groups.Count + "개 리소스를 함께 표시합니다.", cancellationSource, (cancellationToken, reportProgress) => ResourceBatchPreview.Load(groups, loader, cancellationToken, reportProgress));
                if (IsDisposed || cancellationSource.IsCancellationRequested)
                {
                    loaded.Dispose();
                    return;
                }
                batchPreview = loaded;
                canvas.Image = loaded.Image;
                loaded.Image = null;
                isBatchOverview = true;
                ActivateBatchResource(selected);
                canvas.Fit();
                statusLabel.Text = groups.Count + "개 리소스 · 이미지 " + loaded.Cells.Count + "개 · " + loader.MemorySummary;
            }
            catch (OperationCanceledException) { positionLabel.Text = "묶음 읽기 취소됨"; }
            catch (Exception exception) { if (!IsDisposed)
                {
                    resourceInfoText.Text = "묶음 읽기 오류: " + exception.Message;
                }
            }
            finally { cancellationSource.Dispose(); if (previewCancel == cancellationSource) { previewCancel = null; } UpdateBatchControls(); }
        }
        void ActivateBatchResource(ResourceGroup group)
        {
            if (batchPreview == null)
            {
                return;
            }

            currentResource = group;
            currentTexture = group.First;
            resourceOverview = false;
            hideCurrentCheckBox.Enabled = true;
            currentSourceStamp = null;
            batchPreview.Stamps.TryGetValue(group.Id, out currentSourceStamp);
            canvas.Highlights = batchPreview.Cells.Where(batchCell => batchCell.Group.Id == group.Id).Select(batchCell => batchCell.Bounds).ToList();
            canvas.Selection = Rectangle.Empty;
            canvas.Invalidate();
            resourceInfoText.Text = "선택 대상: " + group.Label + " · 이미지 " + group.Textures.Count + "개\r\n" + group.First.Source.Path + "\r\n리소스 숨기기(H)·미번역 기록은 선택 대상에만 적용합니다. 묶음 전체 버튼은 표시 중인 " + batchPreview.Groups.Count + "개 리소스에 적용합니다.";
            if (batchPreview.Errors.Count > 0)
            {
                resourceInfoText.AppendText("\r\n" + string.Join("\r\n", batchPreview.Errors));
            }

            positionLabel.Text = "클릭: 리소스 선택 · 더블클릭: 개별 확대 · 청록색 테두리: 선택 대상";
            UpdateBatchControls();
        }
        void HideBatch()
        {
            if (previewCancel != null || scanCancellationSource != null)
            {
                return;
            }

            var groups = BatchTargets();
            if (groups.Count == 0)
            {
                return;
            }

            if (!hidden.HideMany(groups.Select(resourceGroup => resourceGroup.First)))
            {
                MessageBox.Show(this, hidden.LastError, "묶음 숨김 저장 오류");
                return;
            }
            AdvanceAfterRemoval(groups.Last());
            statusLabel.Text = groups.Count + "개 리소스를 숨겼습니다. 숨김 목록에서 각각 복원할 수 있습니다.";
        }
        void RecordBatch()
        {
            if (previewCancel != null || scanCancellationSource != null)
            {
                return;
            }

            var groups = BatchTargets();
            if (groups.Count == 0)
            {
                return;
            }

            var stamps = batchPreview == null ? new Dictionary<string, string> { { groups[0].Id, currentSourceStamp } } : new Dictionary<string, string>(batchPreview.Stamps);
            try
            {
                using (var dialog = new TranslationNoteDialog("", groups.Count))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }

                    string note = dialog.Note, root = result == null ? "" : result.Root, game = result == null ? "" : result.Game;
                    using (var cancellationSource = new CancellationTokenSource())
                    {
                        LoadingDialog.Run(this, "미번역 묶음 기록", groups.Count + "개 리소스를 각각 기록합니다.", cancellationSource, (cancellationToken, reportProgress) => { translations.SaveResources(groups, loader, note, root, game, stamps, cancellationToken, reportProgress); return true; });
                    }

                    foreach (var group in groups)
                    {
                        recordedResourceIds.Add(group.Id);
                    }

                    AdvanceAfterRemoval(groups.Last());
                    statusLabel.Text = groups.Count + "개 리소스 미번역 기록 완료 · 목록에서 각각 관리할 수 있습니다.";
                }
            }
            catch (OperationCanceledException) { statusLabel.Text = "묶음 기록 취소 · 미번역 목록은 변경되지 않았습니다."; }
            catch (Exception exception) { MessageBox.Show(this, exception.Message, "미번역 묶음 저장 오류"); }
        }
        void MovePage(int delta)
        {
            pages.Move(pages.Number + delta);
            UpdatePage();
        }
        void UpdatePage()
        {
            list.SelectedIndices.Clear();
            list.VirtualListSize = pages.Items.Count;
            list.Invalidate();
            if (pages.Items.Count > 0)
            {
                list.EnsureVisible(0);
            }

            pageStatusLabel.Text = (pages.Number + 1) + " / " + pages.Count + " 페이지 · 200개씩";
            if (previousPageButton != null)
            {
                previousPageButton.Enabled = pages.Number > 0;
                nextPageButton.Enabled = pages.Number + 1 < pages.Count;
            }
        }
        void ClearPreview()
        {
            currentTexture = null;
            currentResource = null;
            resourceOverview = false;
            isBatchOverview = false;
            if (batchPreview != null)
            {
                batchPreview.Dispose();
                batchPreview = null;
            }
            resourceCells.Clear();
            canvas.Highlights.Clear();
            hideCurrentCheckBox.Checked = false;
            hideCurrentCheckBox.Enabled = false;
            var previousImage = canvas.Image;
            canvas.Image = null;
            if (previousImage != null)
            {
                previousImage.Dispose();
            }

            canvas.Selection = Rectangle.Empty;
            canvas.Invalidate();
            resourceInfoText.Clear();
            positionLabel.Text = "";
            UpdateBatchControls();
        }
        protected override bool ProcessCmdKey(ref Message message, Keys keyData)
        {
            if (HideNavigation.ShortcutAllowed(keyData, ActiveControl) && previewCancel == null && scanCancellationSource == null)
            {
                // A held key must not hide successive images as their previews finish.
                if ((message.LParam.ToInt64() & (1L << 30)) == 0)
                {
                    HideCurrent();
                }

                return true;
            }
            return base.ProcessCmdKey(ref message, keyData);
        }
        void HideCurrent()
        {
            hideCurrentCheckBox.Checked = false;
            if (previewCancel != null || scanCancellationSource != null)
            {
                return;
            }

            ResourceGroup target = list.SelectedIndices.Count > 0 && list.SelectedIndices[0] < pages.Items.Count ? pages.Items[list.SelectedIndices[0]] : currentResource;
            if (target == null)
            {
                return;
            }

            if (!hidden.Hide(target.First))
            {
                MessageBox.Show(this, hidden.LastError, "숨김 저장 오류");
                return;
            }
            AdvanceAfterRemoval(target);
            statusLabel.Text = "내부 리소스 전체를 숨겼습니다. ‘숨김 목록’에서 복원할 수 있습니다.";
        }
        bool Excluded(ResourceGroup group)
        {
            return hidden.Contains(group.First) || recordedResourceIds.Contains(group.Id);
        }
        void AdvanceAfterRemoval(ResourceGroup target)
        {
            int index = visible.FindIndex(resourceGroup => resourceGroup.Id == target.Id);
            ResourceGroup next = visible.Skip(Math.Max(0, index + 1)).FirstOrDefault(resourceGroup => !Excluded(resourceGroup)) ?? visible.Take(Math.Max(0, index)).LastOrDefault(resourceGroup => !Excluded(resourceGroup));
            ClearPreview();
            Refilter();
            if (next != null)
            {
                int resourceIndex = visible.FindIndex(resourceGroup => resourceGroup.Id == next.Id);
                if (resourceIndex >= 0)
                {
                    pages.Move(resourceIndex / ResourcePage.Size);
                    UpdatePage();
                    int pageRowIndex = resourceIndex % ResourcePage.Size;
                    list.SelectedIndices.Add(pageRowIndex);
                    list.EnsureVisible(pageRowIndex);
                    list.Focus();
                }
            }
        }
        void ReloadRecordedResources()
        {
            try
            {
                recordedResourceIds = translations.ResourceIds();
            }
            catch (Exception exception) { MessageBox.Show(this, exception.Message, "미번역 목록 읽기 오류"); }
        }
        void ManageHidden()
        {
            using (var dialog = new HiddenImagesDialog(hidden, result == null ? null : result.Items))
            {
                dialog.ShowDialog(this);
            }

            Refilter();
        }
        void ManageTranslations()
        {
            try
            {
                using (var dialog = new TranslationDialog(translations))
                {
                    dialog.ShowDialog(this);
                }
            }
            catch (Exception exception) { MessageBox.Show(this, exception.Message, "미번역 목록"); }
            finally { ReloadRecordedResources(); Refilter(); }
        }
        void RecordUntranslated()
        {
            if (currentResource == null || canvas.Image == null || previewCancel != null || scanCancellationSource != null)
            {
                MessageBox.Show(this, "먼저 기록할 리소스를 여세요.");
                return;
            }
            try
            {
                var existing = translations.Entries().FirstOrDefault(translationEntry => translationEntry.Id == currentResource.Id);
                using (var dialog = new TranslationNoteDialog(existing == null ? "" : existing.Note))
                {
                    if (dialog.ShowDialog(this) != DialogResult.OK)
                    {
                        return;
                    }

                    var group = currentResource;
                    string stamp = currentSourceStamp, note = dialog.Note, root = result == null ? "" : result.Root, game = result == null ? "" : result.Game;
                    using (var cancellationSource = new CancellationTokenSource())
                    {
                        LoadingDialog.Run(this, "미번역 리소스 기록", "리소스 전체 이미지와 원본 위치를 저장하고 있습니다.", cancellationSource, (cancellationToken, reportProgress) => { translations.SaveResource(group, loader, note, root, game, stamp, cancellationToken, reportProgress); return true; });
                    }

                    recordedResourceIds.Add(group.Id);
                    AdvanceAfterRemoval(group);
                    statusLabel.Text = "리소스 전체 미번역 기록 저장 완료 · ‘미번역 목록 / 전달’에서 확인하세요.";
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception exception) { MessageBox.Show(this, exception.Message, "미번역 기록 저장 오류"); }
        }
        void BeginScan(string path)
        {
            if (scanCancellationSource != null)
            {
                return;
            }

            if (!Directory.Exists(path))
            {
                MessageBox.Show(this, "존재하는 폴더를 지정하세요.");
                return;
            }
            folderPathInput.Text = Path.GetFullPath(path);
            try
            {
                ViewerSettings.SaveFolder(settingsPath, folderPathInput.Text);
            }
            catch (Exception exception) { MessageBox.Show(this, exception.Message, "이전 폴더 설정 저장 오류"); }
            scanCancellationSource = new CancellationTokenSource();
            chooseFolderButton.Enabled = rescanButton.Enabled = folderPathInput.Enabled = searchFileContentsCheckBox.Enabled = false;
            cancelScanButton.Enabled = true;
            loader.Clear();
            try
            {
                bool internalSearch = searchFileContentsCheckBox.Checked;
                var scanResult = LoadingDialog.Run(this, "읽는 중 — 리소스 검색", folderPathInput.Text, scanCancellationSource, (cancellationToken, reportProgress) => Scanner.Scan(path, internalSearch, cancellationToken, reportProgress));
                if (IsDisposed)
                {
                    return;
                }

                result = scanResult;
                Refilter();
                statusLabel.Text = (scanResult.Cancelled ? "중지됨 · " : "검색 완료 · ") + scanResult.Files + " 파일 · " + scanResult.Items.Count(texture => texture.Supported) + " 표시 가능 · " + scanResult.Items.Count(texture => !texture.Supported) + " 미지원 · " + scanResult.Messages.Count + " 기록";
                Text = "PS3 Atlas Viewer" + (scanResult.Game.Length > 0 ? " — " + scanResult.Game : "");
            }
            catch (Exception exception) { if (!IsDisposed)
                {
                    MessageBox.Show(this, exception.Message, "검색 오류");
                }
            }
            finally { scanCancellationSource.Dispose(); scanCancellationSource = null; if (!IsDisposed) { chooseFolderButton.Enabled = rescanButton.Enabled = folderPathInput.Enabled = searchFileContentsCheckBox.Enabled = true; cancelScanButton.Enabled = false; } }
            if (!IsDisposed && result != null && !result.Cancelled && visible.Count > 0)
            {
                list.SelectedIndices.Clear();
                list.SelectedIndices.Add(0);
            }
        }
        void Refilter()
        {
            if (result == null)
            {
                return;
            }

            string searchText = filterInput.Text.Trim();
            var groups = ResourceGroup.Group(result.Items);
            int hiddenCount = groups.Count(resourceGroup => hidden.Contains(resourceGroup.First)), recordedCount = groups.Count(resourceGroup => recordedResourceIds.Contains(resourceGroup.Id));
            visible = groups.Where(resourceGroup => !Excluded(resourceGroup) && (includeUnsupportedCheckBox.Checked || resourceGroup.Supported) && resourceGroup.Matches(searchText)).ToList();
            pages.Reset(visible);
            UpdatePage();
            headingLabel.Text = "리소스 " + visible.Count + " / " + groups.Count + " · 이미지 " + visible.Sum(resourceGroup => resourceGroup.Textures.Count) + " · 숨김 " + hiddenCount + " · 미번역 " + recordedCount;
            if ((currentResource != null && Excluded(currentResource)) || (batchPreview != null && batchPreview.Groups.Any(Excluded)))
            {
                ClearPreview();
            }

            UpdateBatchControls();
        }
        void PreviewResource(ResourceGroup group)
        {
            if (previewCancel != null || Excluded(group))
            {
                return;
            }

            ClearPreview();
            currentResource = group;
            currentTexture = group.First;
            resourceOverview = group.Textures.Count > 1;
            hideCurrentCheckBox.Enabled = true;
            currentSourceStamp = null;
            previewCancel = new CancellationTokenSource();
            var cancellationSource = previewCancel;
            resourceInfoText.Text = group.First.Source.Path + "\r\n" + group.Label + " · 이미지 " + group.Textures.Count + "개\r\n원본 오프셋 0x" + group.First.Source.Offset.ToString("X") + " · 저장 길이 " + group.First.Source.Length;
            try
            {
                var loaded = LoadingDialog.Run(this, "읽는 중 — 리소스 전체", group.Label, cancellationSource, (cancellationToken, reportProgress) => ResourcePreview.Load(group, loader, cancellationToken, reportProgress));
                using (loaded)
                {
                    if (IsDisposed || cancellationSource.IsCancellationRequested)
                    {
                        return;
                    }

                    canvas.Image = loaded.Image;
                    loaded.Image = null;
                    resourceCells = loaded.Cells;
                    currentSourceStamp = loaded.SourceStamp;
                    canvas.Fit();
                    canvas.Invalidate();
                    if (loaded.Errors.Count > 0)
                    {
                        resourceInfoText.AppendText("\r\n" + string.Join("\r\n", loaded.Errors));
                    }
                }
                positionLabel.Text = resourceOverview ? "내부 이미지 전체 · 더블클릭: 개별 확대 · 숨김/미번역 기록: 리소스 전체" : "내부 이미지 1개 · 숨김/미번역 기록: 리소스 전체";
                statusLabel.Text = group.Textures.Count + "개 이미지 표시 · " + loader.MemorySummary;
            }
            catch (OperationCanceledException) { positionLabel.Text = "리소스 읽기 취소됨"; }
            catch (Exception exception) { if (!IsDisposed)
                {
                    resourceInfoText.AppendText("\r\n리소스 읽기 오류: " + exception.Message);
                }
            }
            finally { cancellationSource.Dispose(); if (previewCancel == cancellationSource) { previewCancel = null; } UpdateBatchControls(); }
        }
        void Preview(Texture texture)
        {
            if (previewCancel != null || hidden.Contains(texture))
            {
                return;
            }

            isBatchOverview = false;
            canvas.Highlights.Clear();
            int generation = ++previewGeneration;
            previewCancel = new CancellationTokenSource();
            var cancellationSource = previewCancel;
            currentTexture = texture;
            hideCurrentCheckBox.Checked = false;
            hideCurrentCheckBox.Enabled = texture.Supported;
            var previousImage = canvas.Image;
            canvas.Image = null;
            if (previousImage != null)
            {
                previousImage.Dispose();
            }

            canvas.Selection = Rectangle.Empty;
            canvas.Invalidate();
            resourceInfoText.Text = texture.Source.Path + "\r\n" + texture.Locator + "\r\n" + texture.Container + " · " + texture.Format + " · " + texture.Width + "×" + texture.Height + "\r\n원본 오프셋 0x" + texture.Source.Offset.ToString("X") + " / 저장 길이 " + texture.Source.Length + " / " + (texture.Source.Lzp ? "LZP2 해제 후 " : "") + "픽셀 오프셋 0x" + texture.DataOffset.ToString("X") + "\r\n" + texture.Note + (texture.Supported ? "" : "\r\n미지원: " + texture.Issue);
            if (!texture.Supported)
            {
                cancellationSource.Dispose();
                if (previewCancel == cancellationSource)
                {
                    previewCancel = null;
                }

                return;
            }
            try
            {
                double loadMs = 0;
                string loadedStamp = null;
                currentSourceStamp = null;
                var bitmap = LoadingDialog.Run(this, "읽는 중 — 이미지 미리보기", texture.Locator, cancellationSource, (cancellationToken, reportProgress) => { var watch = System.Diagnostics.Stopwatch.StartNew(); string before = PreviewLoader.SourceKey(texture.Source); var loaded = loader.Load(texture, cancellationToken, reportProgress); if (before == PreviewLoader.SourceKey(texture.Source)) { loadedStamp = before; } loadMs = watch.Elapsed.TotalMilliseconds; reportProgress(loader.LastOrigin + " · 이미지 준비 완료"); return loaded; });
                if (IsDisposed || generation != previewGeneration || cancellationSource.IsCancellationRequested)
                {
                    bitmap.Dispose();
                    positionLabel.Text = "이미지 읽기 취소됨";
                    return;
                }
                currentSourceStamp = loadedStamp;
                canvas.Image = bitmap;
                canvas.Fit();
                positionLabel.Text = "휠: 확대 · 오른쪽 드래그: 이동 · 왼쪽 드래그: 영역 선택";
                statusLabel.Text = loader.LastOrigin + " · 처리 " + loadMs.ToString("0.#") + " ms · " + loader.MemorySummary;
                resourceInfoText.AppendText("\r\n불러오기: " + loader.LastOrigin);
            }
            catch (OperationCanceledException) { positionLabel.Text = "이미지 읽기 취소됨"; }
            catch (Exception exception) { if (!IsDisposed && generation == previewGeneration)
                {
                    resourceInfoText.AppendText("\r\n미리보기 오류: " + exception.Message);
                }
            }
            finally { cancellationSource.Dispose(); if (previewCancel == cancellationSource) { previewCancel = null; } UpdateBatchControls(); }
        }
        void Raw()
        {
            Texture texture = currentTexture;
            if (texture == null)
            {
                MessageBox.Show(this, "먼저 리소스를 선택하세요.");
                return;
            }
            using (var dialog = new RawDialog(texture))
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    ClearPreview();
                    currentResource = new ResourceGroup { Textures = new List<Texture> { dialog.Result } };
                    resourceOverview = false;
                    Preview(dialog.Result);
                }
            }
        }
        void Export(bool crop)
        {
            if (canvas.Image == null || currentTexture == null)
            {
                return;
            }

            if (isBatchOverview)
            {
                if (crop)
                {
                    MessageBox.Show(this, "이미지를 더블클릭해 개별 확대한 뒤 영역을 선택하세요.");
                }
                else
                {
                    ExportResource();
                }

                return;
            }
            if (resourceOverview)
            {
                if (crop)
                {
                    MessageBox.Show(this, "이미지를 더블클릭해 개별 확대한 뒤 영역을 선택하세요.");
                    return;
                }
                ExportResource();
                return;
            }
            if (crop && canvas.Selection.IsEmpty)
            {
                MessageBox.Show(this, "왼쪽 드래그로 저장할 영역을 선택하세요.");
                return;
            }
            using (var dialog = new SaveFileDialog { Filter = "PNG 이미지|*.png", FileName = SafeName(currentTexture.Source.Label + "_" + currentTexture.Name) + (crop ? "_crop" : "") + ".png", OverwritePrompt = false })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        // CreateNew rejects every existing path, including a source file or prior export.
                        using (var stream = new FileStream(dialog.FileName, FileMode.CreateNew, FileAccess.Write))
                        {
                            if (crop)
                            {
                                using (var bitmap = canvas.Image.Clone(canvas.Selection, PixelFormat.Format32bppArgb))
                                {
                                    bitmap.Save(stream, ImageFormat.Png);
                                }
                            }
                            else
                            {
                                canvas.Image.Save(stream, ImageFormat.Png);
                            }
                        }
                        var metadata = new
                        {
                            source = currentTexture.Source.Path,
                            sourceOffset = currentTexture.Source.Offset,
                            storedLength = currentTexture.Source.Length,
                            lzp2 = currentTexture.Source.Lzp,
                            payloadOffset = currentTexture.DataOffset,
                            format = currentTexture.Format,
                            width = currentTexture.Width,
                            height = currentTexture.Height,
                            selection = crop ? canvas.Selection : new Rectangle(0, 0, canvas.Image.Width, canvas.Image.Height),
                            exportSha256 = Bin.FileHash(dialog.FileName),
                            note = "원본 RGBA 출력. 화면 채널·배경·격자는 저장에 포함되지 않습니다."
                        };
                        using (var stream = new FileStream(dialog.FileName + ".json", FileMode.CreateNew, FileAccess.Write))
                        using (var writer = new StreamWriter(stream, Encoding.UTF8))
                        {
                            writer.Write(new JavaScriptSerializer().Serialize(metadata));
                        }

                        statusLabel.Text = "저장 완료: " + dialog.FileName;
                    }
                    catch (Exception exception) { MessageBox.Show(this, exception.Message, "저장 결과 확인"); }
                }
            }
        }
        static string SafeName(string fileName)
        {
            foreach (char character in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(character, '_');
            }

            return fileName.Length > 150 ? fileName.Substring(fileName.Length - 150) : fileName;
        }
        void ExportResource()
        {
            if (currentResource == null)
            {
                return;
            }

            using (var dialog = new FolderBrowserDialog { Description = "리소스의 전체 원본 PNG를 저장할 위치" })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        string destination = Path.Combine(dialog.SelectedPath, SafeName(currentResource.Label) + "-" + Guid.NewGuid().ToString("N"));
                        Directory.CreateDirectory(destination);
                        var group = currentResource;
                        using (var cancellationSource = new CancellationTokenSource())
                        {
                            LoadingDialog.Run(this, "리소스 PNG 저장", group.Label, cancellationSource, (cancellationToken, reportProgress) => { for (int textureIndex = 0; textureIndex < group.Textures.Count; textureIndex++) { cancellationToken.ThrowIfCancellationRequested(); var texture = group.Textures[textureIndex]; if (!texture.Supported) { continue; } using (var bitmap = loader.Load(texture, cancellationToken, reportProgress)) using (var fileStream = new FileStream(Path.Combine(destination, textureIndex.ToString("D4") + "-" + SafeName(texture.Name) + ".png"), FileMode.CreateNew)) { bitmap.Save(fileStream, ImageFormat.Png); } } File.WriteAllText(Path.Combine(destination, "resource.json"), new JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 }.Serialize(group), Encoding.UTF8); return true; });
                        }

                        statusLabel.Text = "리소스 PNG 저장 완료: " + destination;
                    }
                    catch (Exception exception) { MessageBox.Show(this, exception.Message, "리소스 PNG 저장"); }
                }
            }
        }
        void SaveList()
        {
            if (result == null)
            {
                return;
            }

            using (var dialog = new SaveFileDialog { Filter = "CSV 목록|*.csv", FileName = "atlas-index.csv", OverwritePrompt = false })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    try
                    {
                        Reports.Csv(result, dialog.FileName);
                        statusLabel.Text = "전체 목록 저장: " + dialog.FileName;
                    }
                    catch (Exception exception) { MessageBox.Show(this, exception.Message); }
                }
            }
        }
    }
    public static class Reports
    {
        static string Q(string value)
        {
            value = value ?? "";
            if (value.Length > 0 && "=+-@".IndexOf(value[0]) >= 0)
            {
                value = "'" + value;
            }

            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
        public static void Csv(ScanResult scanResult, string path, bool replace = false)
        {
            using (var fileStream = new FileStream(path, replace ? FileMode.Create : FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(fileStream, new UTF8Encoding(true)))
            {
                writer.WriteLine("source,entry,container,format,width,height,source_offset,payload_offset,stored_length,lzp2,supported,issue,note");
                foreach (var texture in scanResult.Items)
                {
                    writer.WriteLine(string.Join(",", new[] { Q(texture.Source.Path), Q(texture.Locator), Q(texture.Container), Q(texture.Format), texture.Width.ToString(), texture.Height.ToString(), texture.Source.Offset.ToString(), texture.DataOffset.ToString(), texture.Source.Length.ToString(), texture.Source.Lzp.ToString(), texture.Supported.ToString(), Q(texture.Issue), Q(texture.Note) }));
                }

                foreach (var scanMessage in scanResult.Messages)
                {
                    writer.WriteLine(Q(scanMessage) + ",,,,,,,,,,,scan-message,");
                }
            }
        }
    }
}
