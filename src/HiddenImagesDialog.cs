using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AtlasView
{
    internal sealed class HiddenImagesDialog : Form
    {
        internal const int PageSize = 200;
        readonly HiddenImageStore store;
        readonly TextBox search = new TextBox();
        readonly Label summary = new Label(), pageLabel = new Label(), detail = new Label();
        internal readonly ListView Items = new ListView();
        readonly HashSet<string> marked = new HashSet<string>();
        bool rebuilding;
        int page;
        List<HiddenImageEntry> all = new List<HiddenImageEntry>(), filtered = new List<HiddenImageEntry>();
        readonly System.Windows.Forms.Timer searchTimer = new System.Windows.Forms.Timer { Interval = 180 }, progressTimer = new System.Windows.Forms.Timer { Interval = 80 };
        readonly AtlasCanvas canvas = new AtlasCanvas();
        readonly ProgressBar progress = new ProgressBar { Style = ProgressBarStyle.Marquee, Dock = DockStyle.Bottom, Height = 8, Visible = false };
        readonly PreviewLoader loader = new PreviewLoader();
        readonly HiddenPreviewResolver resolver;
        readonly SemaphoreSlim loadGate = new SemaphoreSlim(1, 1);
        Button previous, next, stop;
        CancellationTokenSource previewCancel;
        int generation;
        volatile string progressText = "";
        ResourceGroup current;
        HiddenImageEntry selected;
        List<Rectangle> cells = new List<Rectangle>();
        bool individual;
        internal HiddenImagesDialog(HiddenImageStore hidden) : this(hidden, null) { }
        internal HiddenImagesDialog(HiddenImageStore hidden, IEnumerable<Texture> known)
        {
            store = hidden;
            resolver = new HiddenPreviewResolver(known);
            Text = "숨긴 리소스 관리";
            Size = new Size(1320, 800);
            MinimumSize = new Size(1000, 650);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Font = new Font("Malgun Gothic", 9);
            BackColor = Color.FromArgb(28, 32, 40);
            ForeColor = Color.FromArgb(223, 230, 237);
            var split = new SplitContainer { Dock = DockStyle.Fill, Size = new Size(1280, 740), SplitterDistance = 640, Panel1MinSize = 480, Panel2MinSize = 340, Padding = new Padding(8) };
            Controls.Add(split);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(8), ColumnCount = 1, RowCount = 6 };
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            split.Panel1.Controls.Add(layout);
            layout.Controls.Add(new Label { Text = "행 선택: 이미지 보기 · 체크: 복원할 리소스 지정", Dock = DockStyle.Fill }, 0, 0);
            search.Dock = DockStyle.Fill;
            search.TextChanged += delegate
            {
                searchTimer.Stop();
                searchTimer.Start();
            };
            searchTimer.Tick += delegate
            {
                searchTimer.Stop();
                ApplyFilter();
            };
            layout.Controls.Add(search, 0, 1);
            Items.Dock = DockStyle.Fill;
            Items.View = View.Details;
            Items.CheckBoxes = true;
            Items.FullRowSelect = true;
            Items.MultiSelect = false;
            Items.HideSelection = false;
            Items.BackColor = Color.FromArgb(34, 39, 48);
            Items.ForeColor = ForeColor;
            Items.Columns.Add("파일 / 내부 리소스 위치", 265);
            Items.Columns.Add("포맷", 70);
            Items.Columns.Add("원본 경로", 320);
            Items.Columns.Add("숨긴 시각", 155);
            Items.ItemChecked += delegate (object sender, ItemCheckedEventArgs eventArgs)
            {
                if (rebuilding || eventArgs.Item.Tag == null)
                {
                    return;
                }

                string key = (string)eventArgs.Item.Tag;
                if (eventArgs.Item.Checked)
                {
                    marked.Add(key);
                }
                else
                {
                    marked.Remove(key);
                }

                UpdateSummary();
            };
            var queue = new SelectionPreviewQueue(this, delegate
            {
                if (!rebuilding && Items.SelectedItems.Count > 0)
                {
                    string key = (string)Items.SelectedItems[0].Tag;
                    var entry = filtered.FirstOrDefault(hiddenEntry => hiddenEntry.Key == key);
                    if (entry != null)
                    {
                        StartPreview(entry, null);
                    }
                }
            });
            Items.SelectedIndexChanged += delegate
            {
                if (!rebuilding)
                {
                    queue.Schedule();
                }
            };
            layout.Controls.Add(Items, 0, 2);
            var paging = new FlowLayoutPanel { Dock = DockStyle.Fill };
            previous = MakeButton("이전", () => MovePage(-1));
            next = MakeButton("다음", () => MovePage(1));
            pageLabel.AutoSize = true;
            pageLabel.Padding = new Padding(5, 6, 5, 0);
            paging.Controls.Add(previous);
            paging.Controls.Add(pageLabel);
            paging.Controls.Add(next);
            layout.Controls.Add(paging, 0, 3);
            summary.Dock = DockStyle.Fill;
            layout.Controls.Add(summary, 0, 4);
            var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
            buttons.Controls.Add(MakeButton("검색 결과 체크", MarkFiltered));
            buttons.Controls.Add(MakeButton("체크 해제", delegate
            {
                marked.Clear();
                ShowPage();
            }));
            buttons.Controls.Add(MakeButton("체크 항목 복원", RestoreMarked));
            buttons.Controls.Add(MakeButton("전체 복원", delegate
            {
                if (store.RestoreAll())
                {
                    marked.Clear();
                    RefreshItems();
                }
                else
                {
                    ShowError();
                }
            }));
            var close = MakeButton("닫기", Close);
            buttons.Controls.Add(close);
            CancelButton = close;
            layout.Controls.Add(buttons, 0, 5);
            var preview = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
            preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            preview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            preview.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
            split.Panel2.Controls.Add(preview);
            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill };
            toolbar.Controls.Add(MakeButton("맞춤", canvas.Fit));
            toolbar.Controls.Add(MakeButton("1:1", () => canvas.SetZoom(1)));
            toolbar.Controls.Add(MakeButton("전체 보기", () => { if (selected != null) { StartPreview(selected, null); } }));
            stop = MakeButton("중지", () => { if (previewCancel != null) { previewCancel.Cancel(); } });
            stop.Enabled = false;
            toolbar.Controls.Add(stop);
            preview.Controls.Add(toolbar, 0, 0);
            var imagePanel = new Panel { Dock = DockStyle.Fill };
            imagePanel.Controls.Add(canvas);
            imagePanel.Controls.Add(progress);
            preview.Controls.Add(imagePanel, 0, 1);
            detail.Dock = DockStyle.Fill;
            detail.AutoEllipsis = true;
            detail.Text = "숨김 항목을 선택하면 이미지가 표시됩니다.";
            preview.Controls.Add(detail, 0, 2);
            canvas.ImageDoubleClicked = delegate (Point imagePoint)
            {
                if (individual || current == null || selected == null || previewCancel != null)
                {
                    return;
                }

                int textureIndex = cells.FindIndex(bounds => bounds.Contains(imagePoint));
                if (textureIndex >= 0)
                {
                    var texture = current.Textures[textureIndex];
                    var entry = selected;
                    BeginInvoke((Action)delegate
                    {
                        if (!IsDisposed)
                        {
                            StartPreview(entry, texture);
                        }
                    });
                }
            };
            progressTimer.Tick += delegate
            {
                detail.Text = progressText;
            };
            FormClosing += delegate
            {
                CancelPreview();
            };
            store.Reload();
            RefreshItems();
        }
        Button MakeButton(string text, Action action)
        {
            var button = new Button { Text = text, AutoSize = true, Height = 30, FlatStyle = FlatStyle.Flat, BackColor = Color.FromArgb(44, 51, 62), ForeColor = ForeColor };
            button.Click += delegate
            {
                action();
            };
            return button;
        }
        void UpdateSummary()
        {
            summary.Text = store.LastError.Length > 0 ? "숨김 목록 오류: " + store.LastError : "숨김 " + store.Count + "개 · 검색 결과 " + filtered.Count + "개 · 체크 " + marked.Count + "개";
        }
        internal void RefreshItems()
        {
            all = store.Entries();
            marked.IntersectWith(all.Select(hiddenEntry => hiddenEntry.Key));
            ApplyFilter();
        }
        void ApplyFilter()
        {
            searchTimer.Stop();
            string query = search.Text.Trim();
            filtered = all.Where(hiddenEntry => query.Length == 0 || ((hiddenEntry.Locator ?? "") + " " + hiddenEntry.SourcePath + " " + hiddenEntry.Format).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            page = 0;
            ShowPage();
        }
        internal void MovePage(int delta)
        {
            page = Math.Max(0, Math.Min(Math.Max(0, (filtered.Count - 1) / PageSize), page + delta));
            ShowPage();
        }
        internal void MarkFiltered()
        {
            marked.UnionWith(filtered.Select(hiddenEntry => hiddenEntry.Key));
            ShowPage();
        }
        void ShowPage()
        {
            CancelPreview();
            ClearImage();
            selected = null;
            detail.Text = "숨김 항목을 선택하면 이미지가 표시됩니다.";
            rebuilding = true;
            Items.BeginUpdate();
            try
            {
                Items.Items.Clear();
                foreach (var entry in filtered.Skip(page * PageSize).Take(PageSize))
                {
                    var listItem = new ListViewItem(new[] { entry.Locator, entry.Format, entry.SourcePath, new DateTime(entry.HiddenUtcTicks, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm") });
                    listItem.Tag = entry.Key;
                    listItem.Checked = marked.Contains(entry.Key);
                    Items.Items.Add(listItem);
                }
            }
            finally { Items.EndUpdate(); rebuilding = false; }
            previous.Enabled = page > 0;
            next.Enabled = (page + 1) * PageSize < filtered.Count;
            pageLabel.Text = (page + 1) + " / " + Math.Max(1, (filtered.Count + PageSize - 1) / PageSize) + " 페이지 · 200개씩";
            UpdateSummary();
        }
        void ClearImage()
        {
            current = null;
            cells.Clear();
            var previousImage = canvas.Image;
            canvas.Image = null;
            if (previousImage != null)
            {
                previousImage.Dispose();
            }

            canvas.Selection = Rectangle.Empty;
            canvas.Invalidate();
        }
        void CancelPreview()
        {
            generation++;
            if (previewCancel != null)
            {
                previewCancel.Cancel();
                previewCancel = null;
            }
            progressTimer.Stop();
            progress.Visible = false;
            if (stop != null)
            {
                stop.Enabled = false;
            }
        }
        internal async void StartPreview(HiddenImageEntry entry, Texture texture)
        {
            CancelPreview();
            ClearImage();
            selected = entry;
            individual = texture != null;
            var cancellationSource = new CancellationTokenSource();
            previewCancel = cancellationSource;
            int version = generation;
            progressText = "읽는 중 · " + entry.Locator;
            detail.Text = progressText;
            progress.Visible = true;
            stop.Enabled = true;
            progressTimer.Start();
            ResourceGroup group = null;
            ResourcePreview loaded = null;
            try
            {
                loaded = await Task.Run(() => { loadGate.Wait(cancellationSource.Token); try { group = texture == null ? resolver.Resolve(entry, cancellationSource.Token) : new ResourceGroup { Textures = new List<Texture> { texture } }; return ResourcePreview.Load(group, loader, cancellationSource.Token, statusMessage => progressText = entry.Locator + "\n" + statusMessage); } finally { loadGate.Release(); } }, cancellationSource.Token);
                if (IsDisposed || version != generation || cancellationSource.IsCancellationRequested)
                {
                    return;
                }

                current = group;
                canvas.Image = loaded.Image;
                loaded.Image = null;
                cells = loaded.Cells;
                canvas.Fit();
                detail.Text = entry.Locator + "\n" + entry.SourcePath + "\n이미지 " + group.Textures.Count + "개 · 더블클릭: 개별 확대" + (loaded.Errors.Count == 0 ? "" : "\n" + string.Join(" / ", loaded.Errors));
            }
            catch (OperationCanceledException) { if (!IsDisposed && version == generation)
                {
                    detail.Text = "이미지 읽기를 중지했습니다.";
                }
            }
            catch (Exception exception) { if (!IsDisposed && version == generation)
                {
                    detail.Text = entry.Locator + "\n미리보기 오류: " + exception.Message;
                }
            }
            finally { if (loaded != null) { loaded.Dispose(); } if (previewCancel == cancellationSource) { previewCancel = null; if (!IsDisposed) { progressTimer.Stop(); progress.Visible = false; stop.Enabled = false; } } cancellationSource.Dispose(); }
        }
        internal void RestoreMarked()
        {
            if (marked.Count == 0)
            {
                return;
            }

            if (store.Restore(marked))
            {
                marked.Clear();
                RefreshItems();
            }
            else
            {
                ShowError();
            }
        }
        void ShowError()
        {
            MessageBox.Show(this, store.LastError, "숨김 목록 저장 오류");
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && !IsDisposed)
            {
                CancelPreview();
                ClearImage();
                searchTimer.Dispose();
                progressTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
