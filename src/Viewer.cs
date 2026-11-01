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

namespace AtlasView {
public sealed class AtlasCanvas : Control {
    public Bitmap Image; public float Zoom=1,PanX=24,PanY=24;public int Channel,BackgroundMode,CellW=32,CellH=32;public bool Grid;
    public Rectangle Selection {get;set;} public Action<string> Hover;public Action<Point> ImageDoubleClicked,ImageClicked;public List<Rectangle> Highlights=new List<Rectangle>();private Point start,last,clickStart;private bool selecting,panning;
    public AtlasCanvas(){DoubleBuffered=true;Dock=DockStyle.Fill;BackColor=Color.FromArgb(20,23,29);SetStyle(ControlStyles.Selectable,true);TabStop=true;}
    public void Fit(){if(Image==null)return;Zoom=Math.Min(8,Math.Min((Width-48f)/Image.Width,(Height-48f)/Image.Height));Zoom=Math.Max(0.025f,Zoom);PanX=(Width-Image.Width*Zoom)/2;PanY=(Height-Image.Height*Zoom)/2;Invalidate();}
    public void SetZoom(float value){float old=Zoom;Zoom=Math.Max(0.025f,Math.Min(32,value));PanX=Width/2f-(Width/2f-PanX)*Zoom/old;PanY=Height/2f-(Height/2f-PanY)*Zoom/old;Invalidate();}
    Point Pixel(Point p){return new Point((int)Math.Floor((p.X-PanX)/Zoom),(int)Math.Floor((p.Y-PanY)/Zoom));}
    protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);Focus();if(Image==null)return;last=e.Location;if(e.Button==MouseButtons.Right || e.Button==MouseButtons.Middle)panning=true;else if(e.Button==MouseButtons.Left){selecting=true;clickStart=e.Location;start=Pixel(e.Location);Selection=Rectangle.Empty;}Capture=true;}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(Image==null)return;Point p=Pixel(e.Location);
        if(panning){PanX+=e.X-last.X;PanY+=e.Y-last.Y;last=e.Location;Invalidate();}
        if(selecting){int x=Math.Max(0,Math.Min(start.X,p.X)),y=Math.Max(0,Math.Min(start.Y,p.Y)),r=Math.Min(Image.Width,Math.Max(start.X,p.X)+1),b=Math.Min(Image.Height,Math.Max(start.Y,p.Y)+1);Selection=r>x && b>y?Rectangle.FromLTRB(x,y,r,b):Rectangle.Empty;Invalidate();}
        if(Hover!=null && p.X>=0 && p.Y>=0 && p.X<Image.Width && p.Y<Image.Height){var c=Image.GetPixel(p.X,p.Y);Hover(string.Format("X {0}   Y {1}    RGBA {2}, {3}, {4}, {5}    확대 {6:0.#}%    선택 {7},{8}  {9}×{10}",p.X,p.Y,c.R,c.G,c.B,c.A,Zoom*100,Selection.X,Selection.Y,Selection.Width,Selection.Height));}
    }
    protected override void OnMouseUp(MouseEventArgs e){bool click=selecting && e.Button==MouseButtons.Left && Math.Abs(e.X-clickStart.X)<=3 && Math.Abs(e.Y-clickStart.Y)<=3;selecting=panning=false;Capture=false;base.OnMouseUp(e);if(click && Image!=null && ImageClicked!=null)ImageClicked(Pixel(e.Location));}
    protected override void OnMouseDoubleClick(MouseEventArgs e){base.OnMouseDoubleClick(e);if(e.Button==MouseButtons.Left && Image!=null && ImageDoubleClicked!=null)ImageDoubleClicked(Pixel(e.Location));}
    protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);float old=Zoom;Zoom=Math.Max(0.025f,Math.Min(32,Zoom*(e.Delta>0?1.25f:0.8f)));PanX=e.X-(e.X-PanX)*Zoom/old;PanY=e.Y-(e.Y-PanY)*Zoom/old;Invalidate();}
    protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);var g=e.Graphics;
        if(Image==null){using(var f=new Font("Malgun Gothic",15))using(var br=new SolidBrush(Color.FromArgb(160,175,191)))g.DrawString("PS3 ATLAS VIEWER\n\n복호화된 폴더를 열고 텍스처를 선택하세요.\n\n휠: 확대  ·  오른쪽 드래그: 이동\n왼쪽 드래그: 영역 선택",f,br,new RectangleF(40,60,Math.Max(1,Width-80),Height-80));return;}
        var dest=new RectangleF(PanX,PanY,Image.Width*Zoom,Image.Height*Zoom);
        var clip=RectangleF.Intersect(dest,ClientRectangle);g.SetClip(clip);
        Color bg=BackgroundMode==1?Color.Black:BackgroundMode==2?Color.White:Color.FromArgb(48,53,63);using(var br=new SolidBrush(bg))g.FillRectangle(br,clip);
        if(BackgroundMode==0){using(var br=new SolidBrush(Color.FromArgb(64,70,81)))for(int y=(int)clip.Top/16*16;y<clip.Bottom;y+=16)for(int x=(int)clip.Left/16*16;x<clip.Right;x+=16)if(((x/16+y/16)&1)==0)g.FillRectangle(br,x,y,16,16);}
        g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;
        using(var ia=new ImageAttributes()) {
            var m=new ColorMatrix();if(Channel==1)m.Matrix33=0;m.Matrix43=Channel==1?1:0;
            if(Channel>=2){m=new ColorMatrix(new float[][]{new float[5],new float[5],new float[5],new float[5],new float[]{0,0,0,1,1}});int src=Channel==2?3:Channel-3;m[src,0]=1;m[src,1]=1;m[src,2]=1;}
            ia.SetColorMatrix(m);g.DrawImage(Image,Rectangle.Round(dest),0,0,Image.Width,Image.Height,GraphicsUnit.Pixel,ia);
        }
        if(Grid && CellW*Zoom>=4 && CellH*Zoom>=4)using(var pen=new Pen(Color.FromArgb(150,65,215,194))){int sx=Math.Max(0,(int)((clip.Left-PanX)/Zoom/CellW)),sy=Math.Max(0,(int)((clip.Top-PanY)/Zoom/CellH));for(int x=sx*CellW;x<=Image.Width && PanX+x*Zoom<=clip.Right;x+=CellW)g.DrawLine(pen,PanX+x*Zoom,clip.Top,PanX+x*Zoom,clip.Bottom);for(int y=sy*CellH;y<=Image.Height && PanY+y*Zoom<=clip.Bottom;y+=CellH)g.DrawLine(pen,clip.Left,PanY+y*Zoom,clip.Right,PanY+y*Zoom);}
        using(var pen=new Pen(Color.FromArgb(95,222,201),3))foreach(var r in Highlights)g.DrawRectangle(pen,PanX+r.X*Zoom,PanY+r.Y*Zoom,Math.Max(1,r.Width*Zoom-2),Math.Max(1,r.Height*Zoom-2));
        if(!Selection.IsEmpty)using(var pen=new Pen(Color.FromArgb(255,202,104),2))g.DrawRectangle(pen,PanX+Selection.X*Zoom,PanY+Selection.Y*Zoom,Selection.Width*Zoom,Selection.Height*Zoom);
        g.ResetClip();
    }
}

public sealed class RawDialog : Form {
    public Texture Result;TextBox offset=new TextBox{Text="0x0",Width=220};NumericUpDown w=Number(256,1,8192),h=Number(256,1,8192),pitch=Number(0,0,1048576);ComboBox fmt=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=220};CheckBox swizzle=new CheckBox{Text="Morton 스위즐 (비압축)",AutoSize=true};Texture input;
    static NumericUpDown Number(int v,int min,int max){return new NumericUpDown{Minimum=min,Maximum=max,Value=v,Width=220};}
    public RawDialog(Texture selected){input=selected;Text="RAW 해석 — 원본 수정 없음";Size=new Size(480,410);FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;Font=new Font("Malgun Gothic",9);
        fmt.Items.AddRange(new object[]{"RGBA8","ARGB8","BGRA8","L8","A8","BC1","BC2","BC3"});fmt.SelectedIndex=0;
        var table=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=2,RowCount=8};table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,150));table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        var note=new Label{Text="오프셋은 선택 항목 기준입니다.\nLZP2 항목에서는 해제된 데이터 기준입니다.",AutoSize=true};table.Controls.Add(note,0,0);table.SetColumnSpan(note,2);
        string[] names={"오프셋 (16진/10진)","너비","높이","포맷","행 피치 (0=자동)"};Control[] cs={offset,w,h,fmt,pitch};for(int i=0;i<cs.Length;i++){table.Controls.Add(new Label{Text=names[i],AutoSize=true,Padding=new Padding(0,5,0,0)},0,i+1);table.Controls.Add(cs[i],1,i+1);}table.Controls.Add(swizzle,1,6);
        var ok=new Button{Text="미리보기",AutoSize=true};ok.Click+=delegate{try{Build();DialogResult=DialogResult.OK;}catch(Exception e){MessageBox.Show(this,e.Message,"RAW 설정");}};table.Controls.Add(ok,1,7);Controls.Add(table);AcceptButton=ok;
        if(selected.Width>0 && selected.Width<=8192)w.Value=selected.Width;if(selected.Height>0 && selected.Height<=8192)h.Value=selected.Height;
        offset.Text="0x"+selected.DataOffset.ToString("X");if(selected.Format!=null && fmt.Items.Contains(selected.Format))fmt.SelectedItem=selected.Format;
    }
    internal void Build(){string text=offset.Text.Trim();long at=text.StartsWith("0x",StringComparison.OrdinalIgnoreCase)?Convert.ToInt64(text.Substring(2),16):long.Parse(text);Bin.Need(at>=0,"음수 오프셋은 사용할 수 없습니다.");string format=(string)fmt.SelectedItem;
        var t=new Texture{Source=input.Source,Name="RAW",Container="수동 해석",Format=format,Width=(int)w.Value,Height=(int)h.Value,Pitch=(int)pitch.Value,BytesPerPixel=(format=="L8" || format=="A8")?1:4,Swizzle=swizzle.Checked,Note="사용자가 지정한 RAW 해석 · 자동 검증된 포맷 아님"};
        int length=Pixels.ByteCount(t);Bin.Need(!t.Swizzle || !format.StartsWith("BC"),"BC 블록 스위즐은 미지원");
        if(input.Source.Lzp){Bin.Need(at<=int.MaxValue,"해제 버퍼 오프셋 초과");t.DataOffset=(int)at;t.DataLength=length;}
        else{long fileLength=new FileInfo(input.Source.Path).Length;Bin.Need(at<=fileLength-input.Source.Offset-length,"RAW 읽기 범위 초과");t.Source=new Source{Path=input.Source.Path,Offset=checked(input.Source.Offset+at),Length=length,Label=input.Source.Label+" RAW @0x"+at.ToString("X")};t.DataLength=length;}
        Result=t;
    }
}

public sealed class MainForm : Form {
    TextBox folder=new TextBox(),filter=new TextBox(),info=new TextBox();Button open,scan,cancel;CheckBox deep=new CheckBox{Text="파일 내부 검색",AutoSize=true,Checked=true},unsupported=new CheckBox{Text="미지원 포함",AutoSize=true};
    ListView list=new ListView();AtlasCanvas canvas=new AtlasCanvas();Label status=new Label(),position=new Label(),heading=new Label();ComboBox channel=new ComboBox(),bg=new ComboBox();
    ScanResult result;List<ResourceGroup> visible=new List<ResourceGroup>();CancellationTokenSource scanCancel,previewCancel;int previewGeneration;Texture current;ResourceGroup currentResource;bool overview;List<Rectangle> resourceCells=new List<Rectangle>();string initial;readonly PreviewLoader loader=new PreviewLoader();
    readonly HiddenImageStore hidden=new HiddenImageStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings","hidden-images.json"),true);
    readonly TranslationStore translations=new TranslationStore(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"untranslated"));string currentSourceStamp;
    HashSet<string> recordedResources=new HashSet<string>();
    readonly string settingsPath=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings","viewer.json");
    readonly NumericUpDown batchSize=new NumericUpDown{Minimum=1,Maximum=32,Value=8,Width=55};
    ResourceBatchPreview batchPreview;bool batchOverview,syncingSelection;Button batchPrevious,batchNext,batchHide,batchRecord;
    Label batchLabel=new Label{AutoSize=true,Padding=new Padding(4,8,0,0)};
    readonly ResourcePage pages=new ResourcePage();Label pageLabel=new Label();Button previousPage,nextPage;readonly CheckBox hideCurrent=new CheckBox{Text="리소스 전체 숨기기 (H)",AutoSize=true,Enabled=false,Margin=new Padding(8,9,4,3)};
    public MainForm(string path){initial=path;Text="PS3 Atlas Viewer";Size=new Size(1440,940);MinimumSize=new Size(1024,700);StartPosition=FormStartPosition.CenterScreen;Font=new Font("Malgun Gothic",9);BackColor=Color.FromArgb(28,32,40);ForeColor=Color.FromArgb(223,230,237);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5,Padding=new Padding(16)};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,55));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,25));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,25));Controls.Add(layout);
        var title=new Label{Text="ATLAS / PS3",Font=new Font("Segoe UI",23,FontStyle.Bold),Dock=DockStyle.Fill,ForeColor=Color.FromArgb(95,222,201)};layout.Controls.Add(title,0,0);
        var top=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=6};top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,115));top.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,85));top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));top.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,10));
        open=Button("폴더 선택",Choose);scan=Button("다시 검색",delegate{BeginScan(folder.Text);});cancel=Button("중지",delegate{if(scanCancel!=null)scanCancel.Cancel();});cancel.Enabled=false;folder.Dock=DockStyle.Fill;folder.Margin=new Padding(4,6,8,0);deep.Margin=new Padding(10,8,0,0);top.Controls.Add(open,0,0);top.Controls.Add(folder,1,0);top.Controls.Add(scan,2,0);top.Controls.Add(cancel,3,0);top.Controls.Add(deep,4,0);layout.Controls.Add(top,0,1);
        var split=new SplitContainer{Dock=DockStyle.Fill,SplitterWidth=8,BackColor=BackColor,Size=new Size(1380,720),SplitterDistance=430,Panel1MinSize=310,Panel2MinSize=500};layout.Controls.Add(split,0,2);
        var left=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5};left.RowStyles.Add(new RowStyle(SizeType.Absolute,30));left.RowStyles.Add(new RowStyle(SizeType.Absolute,32));left.RowStyles.Add(new RowStyle(SizeType.Percent,100));left.RowStyles.Add(new RowStyle(SizeType.Absolute,34));left.RowStyles.Add(new RowStyle(SizeType.Absolute,80));split.Panel1.Controls.Add(left);
        heading.Text="리소스 탐색";heading.Dock=DockStyle.Fill;left.Controls.Add(heading,0,0);
        var search=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};search.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));search.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,115));filter.Dock=DockStyle.Fill;filter.TextChanged+=delegate{Refilter();};unsupported.CheckedChanged+=delegate{Refilter();};search.Controls.Add(filter,0,0);search.Controls.Add(unsupported,1,0);left.Controls.Add(search,0,1);
        list.Dock=DockStyle.Fill;list.View=View.Details;list.FullRowSelect=true;list.HideSelection=false;list.MultiSelect=false;list.VirtualMode=true;list.BackColor=Color.FromArgb(34,39,48);list.ForeColor=ForeColor;list.BorderStyle=BorderStyle.None;list.Columns.Add("파일 / 내부 리소스 위치",265);list.Columns.Add("이미지 수",75);list.Columns.Add("포맷",100);list.RetrieveVirtualItem+=delegate(object sender,RetrieveVirtualItemEventArgs e){if(e.ItemIndex>=pages.Items.Count){e.Item=new ListViewItem();return;}var group=pages.Items[e.ItemIndex];e.Item=new ListViewItem(new[]{group.Label,group.Textures.Count+"개",string.Join(", ",group.Textures.Select(t=>t.Format).Distinct())});if(!group.Supported)e.Item.ForeColor=Color.FromArgb(230,172,111);};var previewQueue=new SelectionPreviewQueue(this,delegate{if(!syncingSelection)PreviewSelected(false);});list.SelectedIndexChanged+=delegate{if(!syncingSelection)previewQueue.Schedule();};left.Controls.Add(list,0,2);
        var bottom=new FlowLayoutPanel{Dock=DockStyle.Fill};bottom.Controls.Add(Button("목록 CSV",SaveList));bottom.Controls.Add(Button("RAW 해석",Raw));var manage=Button("숨김 목록",ManageHidden);bottom.Controls.Add(manage);bottom.Controls.Add(hideCurrent);hideCurrent.CheckedChanged+=delegate{if(hideCurrent.Checked)HideCurrent();};left.Controls.Add(bottom,0,4);
        var paging=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3};paging.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,80));paging.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));paging.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,80));previousPage=Button("이전",delegate{MovePage(-1);});nextPage=Button("다음",delegate{MovePage(1);});pageLabel.Dock=DockStyle.Fill;pageLabel.TextAlign=ContentAlignment.MiddleCenter;paging.Controls.Add(previousPage,0,0);paging.Controls.Add(pageLabel,1,0);paging.Controls.Add(nextPage,2,0);left.Controls.Add(paging,0,3);UpdatePage();
        var right=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=6};right.RowStyles.Add(new RowStyle(SizeType.Absolute,40));right.RowStyles.Add(new RowStyle(SizeType.Percent,100));right.RowStyles.Add(new RowStyle(SizeType.Absolute,40));right.RowStyles.Add(new RowStyle(SizeType.Absolute,40));right.RowStyles.Add(new RowStyle(SizeType.Absolute,125));right.RowStyles.Add(new RowStyle(SizeType.Absolute,76));split.Panel2.Controls.Add(right);
        var toolbar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};toolbar.Controls.Add(Button("맞춤",delegate{canvas.Fit();}));toolbar.Controls.Add(Button("1:1",delegate{canvas.SetZoom(1);}));toolbar.Controls.Add(Button("−",delegate{canvas.SetZoom(canvas.Zoom/1.25f);}));toolbar.Controls.Add(Button("+",delegate{canvas.SetZoom(canvas.Zoom*1.25f);}));SetupCombo(channel,new[]{"RGBA","RGB (불투명)","알파","R","G","B"},120);SetupCombo(bg,new[]{"체커 배경","검정 배경","흰색 배경"},110);channel.SelectedIndexChanged+=delegate{canvas.Channel=channel.SelectedIndex;canvas.Invalidate();};bg.SelectedIndexChanged+=delegate{canvas.BackgroundMode=bg.SelectedIndex;canvas.Invalidate();};toolbar.Controls.Add(channel);toolbar.Controls.Add(bg);right.Controls.Add(toolbar,0,0);right.Controls.Add(canvas,0,1);
        var options=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};var grid=new CheckBox{Text="격자",AutoSize=true,Margin=new Padding(3,9,3,3)};var cw=new NumericUpDown{Minimum=1,Maximum=4096,Value=32,Width=60};var ch=new NumericUpDown{Minimum=1,Maximum=4096,Value=32,Width=60};grid.CheckedChanged+=delegate{canvas.Grid=grid.Checked;canvas.Invalidate();};cw.ValueChanged+=delegate{canvas.CellW=(int)cw.Value;canvas.Invalidate();};ch.ValueChanged+=delegate{canvas.CellH=(int)ch.Value;canvas.Invalidate();};options.Controls.Add(grid);options.Controls.Add(cw);options.Controls.Add(new Label{Text="×",AutoSize=true,Padding=new Padding(0,7,0,0)});options.Controls.Add(ch);options.Controls.Add(Button("PNG 저장",delegate{Export(false);}));options.Controls.Add(Button("선택 영역 저장",delegate{Export(true);}));right.Controls.Add(options,0,2);
        var translationBar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};translationBar.Controls.Add(Button("리소스 전체 보기",delegate{if(currentResource!=null)PreviewResource(currentResource);}));translationBar.Controls.Add(Button("리소스 미번역 기록",RecordUntranslated));translationBar.Controls.Add(Button("미번역 목록 / 전달",ManageTranslations));right.Controls.Add(translationBar,0,3);
        var batchBar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=true};batchBar.Controls.Add(new Label{Text="묶음 크기",AutoSize=true,Padding=new Padding(0,8,0,0)});batchBar.Controls.Add(batchSize);batchSize.Margin=new Padding(3,6,3,3);
        batchPrevious=Button("이전 묶음",()=>MoveBatch(-1));batchNext=Button("다음 묶음",()=>MoveBatch(1));batchBar.Controls.Add(batchPrevious);batchBar.Controls.Add(batchNext);var showBatch=Button("묶음 보기",()=>PreviewSelected(true));batchBar.Controls.Add(showBatch);batchBar.SetFlowBreak(showBatch,true);
        batchHide=Button("묶음 전체 숨기기",HideBatch);batchRecord=Button("묶음 전체 미번역 기록",RecordBatch);batchBar.Controls.Add(batchHide);batchBar.Controls.Add(batchRecord);batchBar.Controls.Add(batchLabel);right.Controls.Add(batchBar,0,5);UpdateBatchControls();
        batchSize.ValueChanged+=delegate{if(previewCancel==null && scanCancel==null)PreviewSelected(true);};
        canvas.ImageClicked=delegate(Point p){if(!batchOverview || batchPreview==null || previewCancel!=null)return;var cell=batchPreview.Cells.FirstOrDefault(c=>c.Bounds.Contains(p));if(cell!=null){ActivateBatchResource(cell.Group);SelectResource(cell.Group,false);}};
        canvas.ImageDoubleClicked=delegate(Point p){if(batchOverview && batchPreview!=null){var cell=batchPreview.Cells.FirstOrDefault(c=>c.Bounds.Contains(p));if(cell!=null)BeginInvoke((Action)delegate{ActivateBatchResource(cell.Group);SelectResource(cell.Group,false);overview=false;batchOverview=false;Preview(cell.Texture);});return;}if(!overview || currentResource==null)return;int i=resourceCells.FindIndex(r=>r.Contains(p));if(i>=0){Texture t=currentResource.Textures[i];BeginInvoke((Action)delegate{overview=false;Preview(t);});}};
        info.Dock=DockStyle.Fill;info.Multiline=true;info.ReadOnly=true;info.ScrollBars=ScrollBars.Vertical;info.BorderStyle=BorderStyle.None;info.BackColor=Color.FromArgb(34,39,48);info.ForeColor=ForeColor;right.Controls.Add(info,0,4);
        status.Text="폴더를 선택하세요. 모든 입력 파일은 읽기 전용으로 엽니다.";status.Dock=DockStyle.Fill;position.Dock=DockStyle.Fill;position.ForeColor=Color.FromArgb(140,162,182);canvas.Hover=delegate(string s){position.Text=s;};layout.Controls.Add(status,0,3);layout.Controls.Add(position,0,4);
        AllowDrop=true;DragEnter+=delegate(object s,DragEventArgs e){if(e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};DragDrop+=delegate(object s,DragEventArgs e){var paths=(string[])e.Data.GetData(DataFormats.FileDrop);if(paths.Length>0 && Directory.Exists(paths[0]))BeginScan(paths[0]);};
        FormClosing+=delegate{if(scanCancel!=null)scanCancel.Cancel();if(previewCancel!=null)previewCancel.Cancel();previewGeneration++;};FormClosed+=delegate{if(canvas.Image!=null)canvas.Image.Dispose();};Shown+=delegate{ReloadRecordedResources();if(!string.IsNullOrWhiteSpace(initial))BeginScan(initial);else{try{folder.Text=ViewerSettings.ReadFolder(settingsPath);}catch(Exception e){MessageBox.Show(this,e.Message,"이전 폴더 설정 읽기 오류");}status.Text=folder.Text.Length==0?"폴더를 선택하세요.":"이전 폴더 경로를 불러왔습니다. 다시 검색을 누르세요.";}};
    }
    static Button Button(string text,Action action){var b=new Button{Text=text,AutoSize=true,Height=30,FlatStyle=FlatStyle.Flat,BackColor=Color.FromArgb(44,51,62),ForeColor=Color.FromArgb(223,230,237),Margin=new Padding(3)};b.FlatAppearance.BorderColor=Color.FromArgb(66,78,91);b.Click+=delegate{action();};return b;}
    static void SetupCombo(ComboBox c,string[] values,int width){c.DropDownStyle=ComboBoxStyle.DropDownList;c.Items.AddRange(values);c.SelectedIndex=0;c.Width=width;c.Margin=new Padding(3,5,3,3);}
    void Choose(){using(var d=new FolderBrowserDialog{Description="복호화된 PS3 게임 폴더 또는 리소스 폴더",ShowNewFolderButton=false,SelectedPath=Directory.Exists(folder.Text)?folder.Text:""})if(d.ShowDialog(this)==DialogResult.OK)BeginScan(d.SelectedPath);}
    void SelectResource(ResourceGroup group,bool preview){int at=visible.FindIndex(g=>g.Id==group.Id);if(at<0)return;
        syncingSelection=true;try{if(pages.Number!=at/ResourcePage.Size){pages.Move(at/ResourcePage.Size);UpdatePage();}list.SelectedIndices.Clear();list.SelectedIndices.Add(at%ResourcePage.Size);list.EnsureVisible(at%ResourcePage.Size);}finally{syncingSelection=false;}
        if(preview && IsHandleCreated)BeginInvoke((Action)delegate{if(!IsDisposed)PreviewSelected(false);});
    }
    List<ResourceGroup> BatchTargets(){if(canvas.Image==null)return new List<ResourceGroup>();return (batchPreview==null?(currentResource==null?new List<ResourceGroup>():new List<ResourceGroup>{currentResource}):batchPreview.Groups).Where(g=>!Excluded(g)).ToList();}
    void UpdateBatchControls(){if(batchPrevious==null)return;int at=currentResource==null?-1:visible.FindIndex(g=>g.Id==currentResource.Id),size=(int)batchSize.Value;bool busy=previewCancel!=null||scanCancel!=null;
        batchPrevious.Enabled=!busy && at>=size;batchNext.Enabled=!busy && at>=0 && (at/size+1)*size<visible.Count;batchSize.Enabled=!busy;
        int count=BatchTargets().Count;batchHide.Enabled=batchRecord.Enabled=!busy && count>0;
        batchLabel.Text=at<0?"":(at/size+1)+" / "+Math.Max(1,(visible.Count+size-1)/size)+" 묶음 · 표시 "+count+"개";
    }
    void MoveBatch(int delta){if(previewCancel!=null || scanCancel!=null || visible.Count==0)return;int size=(int)batchSize.Value,at=currentResource==null?0:visible.FindIndex(g=>g.Id==currentResource.Id);int start=Math.Max(0,Math.Min((visible.Count-1)/size,Math.Max(0,at)/size+delta))*size;SelectResource(visible[start],true);}
    void PreviewSelected(bool force){if(previewCancel!=null || scanCancel!=null || list.SelectedIndices.Count==0 || list.SelectedIndices[0]>=pages.Items.Count)return;
        var selected=pages.Items[list.SelectedIndices[0]];int at=visible.FindIndex(g=>g.Id==selected.Id);var groups=ResourceBatch.Slice(visible,at,(int)batchSize.Value);if(groups.Count==0)return;
        if(groups.Count==1){PreviewResource(selected);return;}
        if(!force && batchOverview && batchPreview!=null && groups.Select(g=>g.Id).SequenceEqual(batchPreview.Groups.Select(g=>g.Id))){ActivateBatchResource(selected);return;}
        ClearPreview();previewCancel=new CancellationTokenSource();var cts=previewCancel;UpdateBatchControls();
        try{var loaded=LoadingDialog.Run(this,"읽는 중 — 리소스 묶음",groups.Count+"개 리소스를 함께 표시합니다.",cts,(token,progress)=>ResourceBatchPreview.Load(groups,loader,token,progress));
            if(IsDisposed || cts.IsCancellationRequested){loaded.Dispose();return;}batchPreview=loaded;canvas.Image=loaded.Image;loaded.Image=null;batchOverview=true;ActivateBatchResource(selected);canvas.Fit();
            status.Text=groups.Count+"개 리소스 · 이미지 "+loaded.Cells.Count+"개 · "+loader.MemorySummary;
        }catch(OperationCanceledException){position.Text="묶음 읽기 취소됨";}catch(Exception e){if(!IsDisposed)info.Text="묶음 읽기 오류: "+e.Message;}finally{cts.Dispose();if(previewCancel==cts)previewCancel=null;UpdateBatchControls();}
    }
    void ActivateBatchResource(ResourceGroup group){if(batchPreview==null)return;currentResource=group;current=group.First;overview=false;hideCurrent.Enabled=true;currentSourceStamp=null;batchPreview.Stamps.TryGetValue(group.Id,out currentSourceStamp);
        canvas.Highlights=batchPreview.Cells.Where(c=>c.Group.Id==group.Id).Select(c=>c.Bounds).ToList();canvas.Selection=Rectangle.Empty;canvas.Invalidate();
        info.Text="선택 대상: "+group.Label+" · 이미지 "+group.Textures.Count+"개\r\n"+group.First.Source.Path+"\r\n리소스 숨기기(H)·미번역 기록은 선택 대상에만 적용합니다. 묶음 전체 버튼은 표시 중인 "+batchPreview.Groups.Count+"개 리소스에 적용합니다.";
        if(batchPreview.Errors.Count>0)info.AppendText("\r\n"+string.Join("\r\n",batchPreview.Errors));position.Text="클릭: 리소스 선택 · 더블클릭: 개별 확대 · 청록색 테두리: 선택 대상";UpdateBatchControls();
    }
    void HideBatch(){if(previewCancel!=null || scanCancel!=null)return;var groups=BatchTargets();if(groups.Count==0)return;
        if(!hidden.HideMany(groups.Select(g=>g.First))){MessageBox.Show(this,hidden.LastError,"묶음 숨김 저장 오류");return;}
        AdvanceAfterRemoval(groups.Last());status.Text=groups.Count+"개 리소스를 숨겼습니다. 숨김 목록에서 각각 복원할 수 있습니다.";
    }
    void RecordBatch(){if(previewCancel!=null || scanCancel!=null)return;var groups=BatchTargets();if(groups.Count==0)return;
        var stamps=batchPreview==null?new Dictionary<string,string>{{groups[0].Id,currentSourceStamp}}:new Dictionary<string,string>(batchPreview.Stamps);
        try{using(var dialog=new TranslationNoteDialog("",groups.Count)){if(dialog.ShowDialog(this)!=DialogResult.OK)return;string note=dialog.Note,root=result==null?"":result.Root,game=result==null?"":result.Game;
            using(var cts=new CancellationTokenSource())LoadingDialog.Run(this,"미번역 묶음 기록",groups.Count+"개 리소스를 각각 기록합니다.",cts,(token,progress)=>{translations.SaveResources(groups,loader,note,root,game,stamps,token,progress);return true;});
            foreach(var group in groups)recordedResources.Add(group.Id);AdvanceAfterRemoval(groups.Last());status.Text=groups.Count+"개 리소스 미번역 기록 완료 · 목록에서 각각 관리할 수 있습니다.";
        }}catch(OperationCanceledException){status.Text="묶음 기록 취소 · 미번역 목록은 변경되지 않았습니다.";}catch(Exception e){MessageBox.Show(this,e.Message,"미번역 묶음 저장 오류");}
    }
    void MovePage(int delta){pages.Move(pages.Number+delta);UpdatePage();}
    void UpdatePage(){list.SelectedIndices.Clear();list.VirtualListSize=pages.Items.Count;list.Invalidate();if(pages.Items.Count>0)list.EnsureVisible(0);pageLabel.Text=(pages.Number+1)+" / "+pages.Count+" 페이지 · 200개씩";if(previousPage!=null){previousPage.Enabled=pages.Number>0;nextPage.Enabled=pages.Number+1<pages.Count;}}
    void ClearPreview(){current=null;currentResource=null;overview=false;batchOverview=false;if(batchPreview!=null){batchPreview.Dispose();batchPreview=null;}resourceCells.Clear();canvas.Highlights.Clear();hideCurrent.Checked=false;hideCurrent.Enabled=false;var old=canvas.Image;canvas.Image=null;if(old!=null)old.Dispose();canvas.Selection=Rectangle.Empty;canvas.Invalidate();info.Clear();position.Text="";UpdateBatchControls();}
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData){
        if(HideNavigation.ShortcutAllowed(keyData,ActiveControl) && previewCancel==null && scanCancel==null){
            // A held key must not hide successive images as their previews finish.
            if((msg.LParam.ToInt64()&(1L<<30))==0)HideCurrent();return true;
        }
        return base.ProcessCmdKey(ref msg,keyData);
    }
    void HideCurrent(){hideCurrent.Checked=false;if(previewCancel!=null || scanCancel!=null)return;ResourceGroup target=list.SelectedIndices.Count>0 && list.SelectedIndices[0]<pages.Items.Count?pages.Items[list.SelectedIndices[0]]:currentResource;if(target==null)return;
        if(!hidden.Hide(target.First)){MessageBox.Show(this,hidden.LastError,"숨김 저장 오류");return;}
        AdvanceAfterRemoval(target);status.Text="내부 리소스 전체를 숨겼습니다. ‘숨김 목록’에서 복원할 수 있습니다.";
    }
    bool Excluded(ResourceGroup group){return hidden.Contains(group.First)||recordedResources.Contains(group.Id);}
    void AdvanceAfterRemoval(ResourceGroup target){
        int index=visible.FindIndex(g=>g.Id==target.Id);
        ResourceGroup next=visible.Skip(Math.Max(0,index+1)).FirstOrDefault(g=>!Excluded(g))??visible.Take(Math.Max(0,index)).LastOrDefault(g=>!Excluded(g));ClearPreview();Refilter();
        if(next!=null){int at=visible.FindIndex(g=>g.Id==next.Id);if(at>=0){pages.Move(at/ResourcePage.Size);UpdatePage();int row=at%ResourcePage.Size;list.SelectedIndices.Add(row);list.EnsureVisible(row);list.Focus();}}
    }
    void ReloadRecordedResources(){try{recordedResources=translations.ResourceIds();}catch(Exception e){MessageBox.Show(this,e.Message,"미번역 목록 읽기 오류");}}
    void ManageHidden(){using(var dialog=new HiddenImagesDialog(hidden,result==null?null:result.Items))dialog.ShowDialog(this);Refilter();}
    void ManageTranslations(){try{using(var dialog=new TranslationDialog(translations))dialog.ShowDialog(this);}catch(Exception e){MessageBox.Show(this,e.Message,"미번역 목록");}finally{ReloadRecordedResources();Refilter();}}
    void RecordUntranslated(){if(currentResource==null || canvas.Image==null || previewCancel!=null || scanCancel!=null){MessageBox.Show(this,"먼저 기록할 리소스를 여세요.");return;}
        try{var existing=translations.Entries().FirstOrDefault(e=>e.Id==currentResource.Id);using(var dialog=new TranslationNoteDialog(existing==null?"":existing.Note)){
            if(dialog.ShowDialog(this)!=DialogResult.OK)return;var group=currentResource;string stamp=currentSourceStamp,note=dialog.Note,root=result==null?"":result.Root,game=result==null?"":result.Game;
            using(var cts=new CancellationTokenSource())LoadingDialog.Run(this,"미번역 리소스 기록","리소스 전체 이미지와 원본 위치를 저장하고 있습니다.",cts,(token,progress)=>{translations.SaveResource(group,loader,note,root,game,stamp,token,progress);return true;});
            recordedResources.Add(group.Id);AdvanceAfterRemoval(group);
            status.Text="리소스 전체 미번역 기록 저장 완료 · ‘미번역 목록 / 전달’에서 확인하세요.";
        }}catch(OperationCanceledException){}catch(Exception e){MessageBox.Show(this,e.Message,"미번역 기록 저장 오류");}
    }
    void BeginScan(string path){if(scanCancel!=null)return;if(!Directory.Exists(path)){MessageBox.Show(this,"존재하는 폴더를 지정하세요.");return;}folder.Text=Path.GetFullPath(path);try{ViewerSettings.SaveFolder(settingsPath,folder.Text);}catch(Exception e){MessageBox.Show(this,e.Message,"이전 폴더 설정 저장 오류");}scanCancel=new CancellationTokenSource();open.Enabled=scan.Enabled=folder.Enabled=deep.Enabled=false;cancel.Enabled=true;loader.Clear();
        try{bool internalSearch=deep.Checked;var r=LoadingDialog.Run(this,"읽는 중 — 리소스 검색",folder.Text,scanCancel,(token,progress)=>Scanner.Scan(path,internalSearch,token,progress));if(IsDisposed)return;result=r;Refilter();status.Text=(r.Cancelled?"중지됨 · ":"검색 완료 · ")+r.Files+" 파일 · "+r.Items.Count(t=>t.Supported)+" 표시 가능 · "+r.Items.Count(t=>!t.Supported)+" 미지원 · "+r.Messages.Count+" 기록";Text="PS3 Atlas Viewer"+(r.Game.Length>0?" — "+r.Game:"");}
        catch(Exception e){if(!IsDisposed)MessageBox.Show(this,e.Message,"검색 오류");}
        finally{scanCancel.Dispose();scanCancel=null;if(!IsDisposed){open.Enabled=scan.Enabled=folder.Enabled=deep.Enabled=true;cancel.Enabled=false;}}
        if(!IsDisposed && result!=null && !result.Cancelled && visible.Count>0){list.SelectedIndices.Clear();list.SelectedIndices.Add(0);}
    }
    void Refilter(){if(result==null)return;string q=filter.Text.Trim();var groups=ResourceGroup.Group(result.Items);int hiddenCount=groups.Count(g=>hidden.Contains(g.First)),recordedCount=groups.Count(g=>recordedResources.Contains(g.Id));visible=groups.Where(g=>!Excluded(g) && (unsupported.Checked || g.Supported) && g.Matches(q)).ToList();pages.Reset(visible);UpdatePage();heading.Text="리소스 "+visible.Count+" / "+groups.Count+" · 이미지 "+visible.Sum(g=>g.Textures.Count)+" · 숨김 "+hiddenCount+" · 미번역 "+recordedCount;if((currentResource!=null && Excluded(currentResource)) || (batchPreview!=null && batchPreview.Groups.Any(Excluded)))ClearPreview();UpdateBatchControls();}
    void PreviewResource(ResourceGroup group){if(previewCancel!=null || Excluded(group))return;ClearPreview();currentResource=group;current=group.First;overview=group.Textures.Count>1;hideCurrent.Enabled=true;currentSourceStamp=null;previewCancel=new CancellationTokenSource();var cts=previewCancel;
        info.Text=group.First.Source.Path+"\r\n"+group.Label+" · 이미지 "+group.Textures.Count+"개\r\n원본 오프셋 0x"+group.First.Source.Offset.ToString("X")+" · 저장 길이 "+group.First.Source.Length;
        try{var loaded=LoadingDialog.Run(this,"읽는 중 — 리소스 전체",group.Label,cts,(token,progress)=>ResourcePreview.Load(group,loader,token,progress));using(loaded){if(IsDisposed || cts.IsCancellationRequested)return;canvas.Image=loaded.Image;loaded.Image=null;resourceCells=loaded.Cells;currentSourceStamp=loaded.SourceStamp;canvas.Fit();canvas.Invalidate();if(loaded.Errors.Count>0)info.AppendText("\r\n"+string.Join("\r\n",loaded.Errors));}
            position.Text=overview?"내부 이미지 전체 · 더블클릭: 개별 확대 · 숨김/미번역 기록: 리소스 전체":"내부 이미지 1개 · 숨김/미번역 기록: 리소스 전체";status.Text=group.Textures.Count+"개 이미지 표시 · "+loader.MemorySummary;
        }catch(OperationCanceledException){position.Text="리소스 읽기 취소됨";}catch(Exception e){if(!IsDisposed)info.AppendText("\r\n리소스 읽기 오류: "+e.Message);}finally{cts.Dispose();if(previewCancel==cts)previewCancel=null;UpdateBatchControls();}
    }
    void Preview(Texture t){if(previewCancel!=null || hidden.Contains(t))return;batchOverview=false;canvas.Highlights.Clear();int generation=++previewGeneration;previewCancel=new CancellationTokenSource();var cts=previewCancel;current=t;hideCurrent.Checked=false;hideCurrent.Enabled=t.Supported;var old=canvas.Image;canvas.Image=null;if(old!=null)old.Dispose();canvas.Selection=Rectangle.Empty;canvas.Invalidate();
        info.Text=t.Source.Path+"\r\n"+t.Locator+"\r\n"+t.Container+" · "+t.Format+" · "+t.Width+"×"+t.Height+"\r\n원본 오프셋 0x"+t.Source.Offset.ToString("X")+" / 저장 길이 "+t.Source.Length+" / "+(t.Source.Lzp?"LZP2 해제 후 ":"")+"픽셀 오프셋 0x"+t.DataOffset.ToString("X")+"\r\n"+t.Note+(t.Supported?"":"\r\n미지원: "+t.Issue);
        if(!t.Supported){cts.Dispose();if(previewCancel==cts)previewCancel=null;return;}
        try{double loadMs=0;string loadedStamp=null;currentSourceStamp=null;var bitmap=LoadingDialog.Run(this,"읽는 중 — 이미지 미리보기",t.Locator,cts,(token,progress)=>{var watch=System.Diagnostics.Stopwatch.StartNew();string before=PreviewLoader.SourceKey(t.Source);var loaded=loader.Load(t,token,progress);if(before==PreviewLoader.SourceKey(t.Source))loadedStamp=before;loadMs=watch.Elapsed.TotalMilliseconds;progress(loader.LastOrigin+" · 이미지 준비 완료");return loaded;});if(IsDisposed || generation!=previewGeneration || cts.IsCancellationRequested){bitmap.Dispose();position.Text="이미지 읽기 취소됨";return;}currentSourceStamp=loadedStamp;canvas.Image=bitmap;canvas.Fit();position.Text="휠: 확대 · 오른쪽 드래그: 이동 · 왼쪽 드래그: 영역 선택";status.Text=loader.LastOrigin+" · 처리 "+loadMs.ToString("0.#")+" ms · "+loader.MemorySummary;info.AppendText("\r\n불러오기: "+loader.LastOrigin);}
        catch(OperationCanceledException){position.Text="이미지 읽기 취소됨";}catch(Exception e){if(!IsDisposed && generation==previewGeneration)info.AppendText("\r\n미리보기 오류: "+e.Message);}
        finally{cts.Dispose();if(previewCancel==cts)previewCancel=null;UpdateBatchControls();}
    }
    void Raw(){Texture t=current;if(t==null){MessageBox.Show(this,"먼저 리소스를 선택하세요.");return;}using(var d=new RawDialog(t))if(d.ShowDialog(this)==DialogResult.OK){ClearPreview();currentResource=new ResourceGroup{Textures=new List<Texture>{d.Result}};overview=false;Preview(d.Result);}}
    void Export(bool crop){if(canvas.Image==null || current==null)return;if(batchOverview){if(crop)MessageBox.Show(this,"이미지를 더블클릭해 개별 확대한 뒤 영역을 선택하세요.");else ExportResource();return;}if(overview){if(crop){MessageBox.Show(this,"이미지를 더블클릭해 개별 확대한 뒤 영역을 선택하세요.");return;}ExportResource();return;}if(crop && canvas.Selection.IsEmpty){MessageBox.Show(this,"왼쪽 드래그로 저장할 영역을 선택하세요.");return;}using(var d=new SaveFileDialog{Filter="PNG 이미지|*.png",FileName=SafeName(current.Source.Label+"_"+current.Name)+(crop?"_crop":"")+".png",OverwritePrompt=false})if(d.ShowDialog(this)==DialogResult.OK)try{
        // CreateNew rejects every existing path, including a source file or prior export.
        using(var stream=new FileStream(d.FileName,FileMode.CreateNew,FileAccess.Write)) {if(crop){using(var bm=canvas.Image.Clone(canvas.Selection,PixelFormat.Format32bppArgb))bm.Save(stream,ImageFormat.Png);}else canvas.Image.Save(stream,ImageFormat.Png);}
        var metadata=new {source=current.Source.Path,sourceOffset=current.Source.Offset,storedLength=current.Source.Length,lzp2=current.Source.Lzp,payloadOffset=current.DataOffset,format=current.Format,width=current.Width,height=current.Height,selection=crop?canvas.Selection:new Rectangle(0,0,canvas.Image.Width,canvas.Image.Height),exportSha256=Bin.FileHash(d.FileName),note="원본 RGBA 출력. 화면 채널·배경·격자는 저장에 포함되지 않습니다."};
        using(var stream=new FileStream(d.FileName+".json",FileMode.CreateNew,FileAccess.Write))using(var writer=new StreamWriter(stream,Encoding.UTF8))writer.Write(new JavaScriptSerializer().Serialize(metadata));status.Text="저장 완료: "+d.FileName;
    }catch(Exception e){MessageBox.Show(this,e.Message,"저장 결과 확인");}}
    static string SafeName(string s){foreach(char c in Path.GetInvalidFileNameChars())s=s.Replace(c,'_');return s.Length>150?s.Substring(s.Length-150):s;}
    void ExportResource(){if(currentResource==null)return;using(var d=new FolderBrowserDialog{Description="리소스의 전체 원본 PNG를 저장할 위치"})if(d.ShowDialog(this)==DialogResult.OK)try{string destination=Path.Combine(d.SelectedPath,SafeName(currentResource.Label)+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(destination);var group=currentResource;using(var cts=new CancellationTokenSource())LoadingDialog.Run(this,"리소스 PNG 저장",group.Label,cts,(token,progress)=>{for(int i=0;i<group.Textures.Count;i++){token.ThrowIfCancellationRequested();var t=group.Textures[i];if(!t.Supported)continue;using(var bitmap=loader.Load(t,token,progress))using(var f=new FileStream(Path.Combine(destination,i.ToString("D4")+"-"+SafeName(t.Name)+".png"),FileMode.CreateNew))bitmap.Save(f,ImageFormat.Png);}File.WriteAllText(Path.Combine(destination,"resource.json"),new JavaScriptSerializer{MaxJsonLength=32*1024*1024}.Serialize(group),Encoding.UTF8);return true;});status.Text="리소스 PNG 저장 완료: "+destination;}catch(Exception e){MessageBox.Show(this,e.Message,"리소스 PNG 저장");}}
    void SaveList(){if(result==null)return;using(var d=new SaveFileDialog{Filter="CSV 목록|*.csv",FileName="atlas-index.csv",OverwritePrompt=false})if(d.ShowDialog(this)==DialogResult.OK)try{Reports.Csv(result,d.FileName);status.Text="전체 목록 저장: "+d.FileName;}catch(Exception e){MessageBox.Show(this,e.Message);}}
}
public static class Reports {
    static string Q(string s){s=s??"";if(s.Length>0 && "=+-@".IndexOf(s[0])>=0)s="'"+s;return "\""+s.Replace("\"","\"\"")+"\"";}
    public static void Csv(ScanResult r,string path,bool replace=false){using(var f=new FileStream(path,replace?FileMode.Create:FileMode.CreateNew,FileAccess.Write))using(var w=new StreamWriter(f,new UTF8Encoding(true))){w.WriteLine("source,entry,container,format,width,height,source_offset,payload_offset,stored_length,lzp2,supported,issue,note");foreach(var t in r.Items)w.WriteLine(string.Join(",",new[]{Q(t.Source.Path),Q(t.Locator),Q(t.Container),Q(t.Format),t.Width.ToString(),t.Height.ToString(),t.Source.Offset.ToString(),t.DataOffset.ToString(),t.Source.Length.ToString(),t.Source.Lzp.ToString(),t.Supported.ToString(),Q(t.Issue),Q(t.Note)}));foreach(var s in r.Messages)w.WriteLine(Q(s)+",,,,,,,,,,,scan-message,");}}
}
}
