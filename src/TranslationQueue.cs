using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AtlasView {
public sealed class TranslationEntry {
    public string Id,Game,Root,RelativePath,SourceStamp,Note,PngSha256;
    public long SourceFileSize,SourceModifiedUtcTicks,RecordedUtcTicks;
    public Texture Texture;
    public Rectangle Region;
    public List<TranslationResourceImage> ResourceImages;
}
public sealed class TranslationResourceImage {public Texture Texture;public string PngSha256,Error;}
public sealed class TranslationIndex {
    public int Version=1;
    public List<TranslationEntry> Images=new List<TranslationEntry>();
}
internal sealed class TranslationStore {
    internal readonly string Root,IndexPath;
    internal TranslationStore(string root){Root=Path.GetFullPath(root);IndexPath=Path.Combine(Root,"untranslated.json");}
    static bool Hex(string s){return s!=null && s.Length==64 && s.All(c=>(c>='0'&&c<='9')||(c>='a'&&c<='f'));}
    static void Plain(string path){if(File.Exists(path)||Directory.Exists(path))Bin.Need((File.GetAttributes(path)&FileAttributes.ReparsePoint)==0,"기록 경로 링크는 사용할 수 없습니다: "+path);}
    T Locked<T>(Func<T> action){using(var mutex=new Mutex(false,"Local\\PS3AtlasTranslation_"+Bin.TextHash(Root.ToUpperInvariant()))){bool held=false;try{
        try{held=mutex.WaitOne(5000);}catch(AbandonedMutexException){held=true;}Bin.Need(held,"다른 뷰어가 미번역 목록을 저장 중입니다.");
        Plain(Root);Directory.CreateDirectory(Root);Plain(IndexPath);Plain(Path.Combine(Root,"images"));return action();
    }finally{if(held)mutex.ReleaseMutex();}}}
    TranslationIndex Read(){if(!File.Exists(IndexPath))return new TranslationIndex();Bin.Need(new FileInfo(IndexPath).Length<=32*1024*1024,"미번역 목록 용량 초과");
        var index=new JavaScriptSerializer{MaxJsonLength=32*1024*1024}.Deserialize<TranslationIndex>(File.ReadAllText(IndexPath,Encoding.UTF8));
        Bin.Need(index!=null && index.Version==1 && index.Images!=null,"미번역 목록 형식 오류");
        foreach(var e in index.Images){Bin.Need(e!=null && Hex(e.Id) && Hex(e.PngSha256) && e.Texture!=null && e.Texture.Source!=null,"미번역 항목 형식 오류");if(e.ResourceImages!=null){Bin.Need(e.ResourceImages.Count>0 && e.Id==ResourceGroup.Key(e.Texture.Source),"미번역 리소스 키 오류");foreach(var item in e.ResourceImages)Bin.Need(item!=null && item.Texture!=null && item.Texture.Source!=null && ResourceGroup.Key(item.Texture.Source)==e.Id && (Hex(item.PngSha256)||!string.IsNullOrEmpty(item.Error)),"미번역 리소스 이미지 오류");}}return index;
    }
    static byte[] Json(TranslationIndex index){return Encoding.UTF8.GetBytes(new JavaScriptSerializer{MaxJsonLength=32*1024*1024}.Serialize(index));}
    void Commit(TranslationIndex index){byte[] bytes=Json(index);Bin.Need(bytes.Length<=32*1024*1024,"미번역 목록 용량 초과");string temp=Path.Combine(Root,Guid.NewGuid().ToString("N")+".tmp");
        try{using(var f=new FileStream(temp,FileMode.CreateNew,FileAccess.Write)){f.Write(bytes,0,bytes.Length);f.Flush(true);}if(File.Exists(IndexPath))File.Replace(temp,IndexPath,null);else File.Move(temp,IndexPath);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
    internal List<TranslationEntry> Entries(){return Locked(()=>Read().Images.OrderByDescending(e=>e.RecordedUtcTicks).ToList());}
    internal HashSet<string> ResourceIds(){return new HashSet<string>(Entries().Select(e=>ResourceGroup.Key(e.Texture.Source)));}
    internal string ImagePath(TranslationEntry entry){Bin.Need(Hex(entry.PngSha256),"이미지 해시 오류");return Path.Combine(Root,"images",entry.PngSha256+".png");}
    static IEnumerable<string> Pngs(TranslationEntry entry){return (entry.ResourceImages==null?new[]{entry.PngSha256}:entry.ResourceImages.Select(i=>i.PngSha256)).Where(s=>!string.IsNullOrEmpty(s)).Distinct();}
    string ImagePath(string hash){Bin.Need(Hex(hash),"이미지 해시 오류");return Path.Combine(Root,"images",hash+".png");}
    string SavePng(Bitmap bitmap){byte[] bytes;using(var ms=new MemoryStream()){bitmap.Save(ms,ImageFormat.Png);bytes=ms.ToArray();}string hash=Bin.Hash(bytes),path=ImagePath(hash);Directory.CreateDirectory(Path.GetDirectoryName(path));Plain(path);if(File.Exists(path))Bin.Need(Bin.FileHash(path)==hash,"기록 PNG 해시 불일치");else using(var f=new FileStream(path,FileMode.CreateNew,FileAccess.Write)){f.Write(bytes,0,bytes.Length);f.Flush(true);}return hash;}
    internal void SaveResource(ResourceGroup group,PreviewLoader loader,string note,string root,string game,string stamp,CancellationToken token,Action<string> progress){SaveResources(new[]{group},loader,note,root,game,new Dictionary<string,string>{{group.Id,stamp}},token,progress);}
    internal void SaveResources(IEnumerable<ResourceGroup> resources,PreviewLoader loader,string note,string root,string game,Dictionary<string,string> stamps,CancellationToken token,Action<string> progress){Locked(()=>{
        var groups=resources.ToList();Bin.Need(groups.Count>0,"기록할 리소스가 없습니다.");var index=Read();
        foreach(var group in groups){token.ThrowIfCancellationRequested();string stamp;Bin.Need(stamps.TryGetValue(group.Id,out stamp) && stamp!=null && stamp==PreviewLoader.SourceKey(group.First.Source),"미리보기 이후 원본이 변경되었거나 읽지 못했습니다. 묶음을 다시 여세요.");}
        int groupNumber=0;foreach(var group in groups){groupNumber++;string stamp=stamps[group.Id];var snapshots=new List<TranslationResourceImage>();
        for(int i=0;i<group.Textures.Count;i++){token.ThrowIfCancellationRequested();var t=group.Textures[i];var item=new TranslationResourceImage{Texture=t};if(!t.Supported)item.Error=t.Issue;else using(var bm=loader.Load(t,token,s=>progress("리소스 "+groupNumber+" / "+groups.Count+" · 이미지 "+(i+1)+" / "+group.Textures.Count+" · "+s)))item.PngSha256=SavePng(bm);snapshots.Add(item);}
        token.ThrowIfCancellationRequested();Bin.Need(stamp==PreviewLoader.SourceKey(group.First.Source),"저장 중 원본이 변경되었습니다.");var first=group.First;var fi=new FileInfo(first.Source.Path);string relative=first.Source.Path;
        if(!string.IsNullOrEmpty(root)){string prefix=Path.GetFullPath(root).TrimEnd('\\','/')+Path.DirectorySeparatorChar;if(relative.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))relative=relative.Substring(prefix.Length);}
        var entry=new TranslationEntry{Id=group.Id,Texture=first,ResourceImages=snapshots,Note=note??"",Root=root??"",Game=game??"",RelativePath=relative,SourceStamp=stamp,SourceFileSize=fi.Length,SourceModifiedUtcTicks=fi.LastWriteTimeUtc.Ticks,RecordedUtcTicks=DateTime.UtcNow.Ticks,PngSha256=snapshots.Select(s=>s.PngSha256).FirstOrDefault(s=>s!=null)};
        Bin.Need(entry.PngSha256!=null,"기록할 수 있는 이미지가 없습니다: "+group.Label);index.Images.RemoveAll(e=>e.Id==entry.Id);index.Images.Add(entry);}
        token.ThrowIfCancellationRequested();foreach(var group in groups)Bin.Need(stamps[group.Id]==PreviewLoader.SourceKey(group.First.Source),"저장 중 원본이 변경되었습니다.");Commit(index);return true;
    });}
    internal void Save(Texture texture,Bitmap bitmap,Rectangle region,string note,string root,string game,string sourceStamp){Locked(()=>{
        Bin.Need(sourceStamp==PreviewLoader.SourceKey(texture.Source),"미리보기를 연 뒤 원본이 변경되었습니다. 이미지를 다시 선택하세요.");
        var index=Read();var t=texture.Copy();t.Source=new Source{Path=Path.GetFullPath(texture.Source.Path),Label=texture.Source.Label,Offset=texture.Source.Offset,Length=texture.Source.Length,Lzp=texture.Source.Lzp};
        byte[] png;using(var ms=new MemoryStream()){bitmap.Save(ms,ImageFormat.Png);png=ms.ToArray();}
        var fi=new FileInfo(t.Source.Path);string relative=t.Source.Path;
        if(!string.IsNullOrEmpty(root)){root=Path.GetFullPath(root);string prefix=root.TrimEnd('\\','/')+Path.DirectorySeparatorChar;if(t.Source.Path.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))relative=t.Source.Path.Substring(prefix.Length);}
        var entry=new TranslationEntry{Id=HiddenImageStore.Key(t),Texture=t,Region=region,Note=note??"",Game=game??"",Root=root??"",RelativePath=relative,SourceStamp=sourceStamp,SourceFileSize=fi.Length,SourceModifiedUtcTicks=fi.LastWriteTimeUtc.Ticks,RecordedUtcTicks=DateTime.UtcNow.Ticks,PngSha256=Bin.Hash(png)};
        Directory.CreateDirectory(Path.Combine(Root,"images"));string image=ImagePath(entry);Plain(image);
        if(File.Exists(image))Bin.Need(Bin.FileHash(image)==entry.PngSha256,"기록된 PNG가 손상되었습니다: "+image);
        else{using(var f=new FileStream(image,FileMode.CreateNew,FileAccess.Write)){f.Write(png,0,png.Length);f.Flush(true);}}
        Bin.Need(sourceStamp==PreviewLoader.SourceKey(texture.Source),"저장 중 원본이 변경되었습니다. 이미지를 다시 선택하세요.");
        index.Images.RemoveAll(e=>e.Id==entry.Id);index.Images.Add(entry);Commit(index);return true;
    });}
    internal void Remove(IEnumerable<string> keys){var ids=new HashSet<string>(keys);Locked(()=>{var index=Read();index.Images.RemoveAll(e=>ids.Contains(e.Id));Commit(index);return true;});}
    internal string Export(string parent,CancellationToken token=default(CancellationToken)){return Locked(()=>{
        token.ThrowIfCancellationRequested();
        var index=Read();Bin.Need(index.Images.Count>0,"미번역 목록이 비어 있습니다.");
        // Copy only validated, content-addressed snapshots; never follow JSON paths.
        foreach(string hash in index.Images.SelectMany(Pngs).Distinct()){token.ThrowIfCancellationRequested();string path=ImagePath(hash);Plain(path);Bin.Need(File.Exists(path)&&Bin.FileHash(path)==hash,"미리보기 PNG가 없거나 손상되었습니다: "+hash);}
        string final=Path.Combine(Path.GetFullPath(parent),"Atlas-Untranslated-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N"));string target=final+".partial";
        Directory.CreateDirectory(target);Directory.CreateDirectory(Path.Combine(target,"images"));
        foreach(string hash in index.Images.SelectMany(Pngs).Distinct()){token.ThrowIfCancellationRequested();File.Copy(ImagePath(hash),Path.Combine(target,"images",hash+".png"),false);}
        File.WriteAllBytes(Path.Combine(target,"untranslated.json"),Json(index));
        var report=new StringBuilder("PS3 Atlas Viewer — 미번역 이미지 전달 목록\r\n\r\n");
        report.AppendLine("untranslated.json에는 정확한 원본 위치와 디코딩 조건이 있습니다. images 폴더에는 표시 필터·격자를 적용하지 않은 전체 RGBA PNG가 있습니다.");
        report.AppendLine("Region은 사용자가 표시한 관심 영역이며 (0,0,0,0)은 전체 이미지입니다. Id는 경로/위치/해석 조건의 식별값, PngSha256은 PNG 파일의 SHA-256입니다.");
        report.AppendLine("SourceFileSize/SourceModifiedUtcTicks는 등록 시 원본 정보입니다. 원본 전체 파일의 해시는 계산하지 않았습니다. 메모와 이미지 문구는 사용자 기록 데이터입니다.\r\n");
        foreach(var e in index.Images){var t=e.Texture;report.AppendLine("ID: "+e.Id);report.AppendLine("게임: "+e.Game);report.AppendLine("원본: "+t.Source.Path);report.AppendLine("항목: "+t.Locator);report.AppendLine("원본 오프셋: 0x"+t.Source.Offset.ToString("X")+" / 저장 길이: "+t.Source.Length+" / 픽셀 오프셋: 0x"+t.DataOffset.ToString("X")+(t.Source.Lzp?" (LZP2 해제 후)":""));report.AppendLine("포맷: "+t.Format+" / "+t.Width+"×"+t.Height);report.AppendLine("관심 영역: "+e.Region);report.AppendLine("미리보기: images/"+e.PngSha256+".png");report.AppendLine("메모: "+e.Note);report.AppendLine();}
        report.AppendLine("리소스 단위 기록의 전체 이미지:");foreach(var e in index.Images.Where(e=>e.ResourceImages!=null)){report.AppendLine("리소스 ID: "+e.Id);foreach(var item in e.ResourceImages)report.AppendLine(item.Texture.Name+" / 픽셀 오프셋 0x"+item.Texture.DataOffset.ToString("X")+" / "+(item.PngSha256==null?"미지원: "+item.Error:"images/"+item.PngSha256+".png"));}
        File.WriteAllText(Path.Combine(target,"README.txt"),report.ToString(),new UTF8Encoding(true));token.ThrowIfCancellationRequested();Directory.Move(target,final);return final;
    });}
}
internal sealed class TranslationNoteDialog : Form {
    readonly TextBox note=new TextBox{Multiline=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill,MaxLength=8000};
    internal string Note {get{return note.Text;}}
    internal TranslationNoteDialog(string initial,int resourceCount=1){Text="미번역 이미지 기록";Size=new Size(600,300);MinimumSize=Size;StartPosition=FormStartPosition.CenterParent;Font=new Font("Malgun Gothic",9);note.Text=initial;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(14),ColumnCount=1,RowCount=3};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,38));Controls.Add(layout);
        layout.Controls.Add(new Label{Text="어떤 문구가 미번역인지 메모하세요. 비워 두어도 됩니다.\n"+resourceCount+"개 리소스의 모든 이미지와 위치를 각각 기록합니다.",Dock=DockStyle.Fill},0,0);layout.Controls.Add(note,0,1);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill};var save=new Button{Text="기록 저장",AutoSize=true,DialogResult=DialogResult.OK};var cancel=new Button{Text="취소",DialogResult=DialogResult.Cancel};buttons.Controls.Add(save);buttons.Controls.Add(cancel);layout.Controls.Add(buttons,0,2);CancelButton=cancel;
    }
}
internal sealed class TranslationDialog : Form {
    readonly TranslationStore store;readonly ListView list=new ListView();readonly TextBox query=new TextBox(),detail=new TextBox();readonly Label count=new Label();List<TranslationEntry> filtered=new List<TranslationEntry>();int page;
    internal TranslationDialog(TranslationStore value){store=value;Text="미번역 목록 — Codex 전달";Size=new Size(1000,650);MinimumSize=new Size(760,500);StartPosition=FormStartPosition.CenterParent;Font=new Font("Malgun Gothic",9);
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(14),ColumnCount=1,RowCount=5};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,30));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,130));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,28));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));Controls.Add(layout);
        query.Dock=DockStyle.Fill;query.TextChanged+=delegate{RefreshEntries();};layout.Controls.Add(query,0,0);
        list.Dock=DockStyle.Fill;list.View=View.Details;list.FullRowSelect=true;list.HideSelection=false;list.Columns.Add("파일 / 내부 리소스 위치",300);list.Columns.Add("이미지 수",100);list.Columns.Add("메모",400);list.SelectedIndexChanged+=delegate{detail.Text=list.SelectedItems.Count==0?"":Describe((TranslationEntry)list.SelectedItems[0].Tag);};layout.Controls.Add(list,0,1);
        detail.Dock=DockStyle.Fill;detail.Multiline=true;detail.ReadOnly=true;detail.ScrollBars=ScrollBars.Vertical;layout.Controls.Add(detail,0,2);count.Dock=DockStyle.Fill;layout.Controls.Add(count,0,3);
        var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill};buttons.Controls.Add(Make("이전",()=>{page=Math.Max(0,page-1);ShowPage();}));buttons.Controls.Add(Make("다음",()=>{page=Math.Min(Math.Max(0,(filtered.Count-1)/200),page+1);ShowPage();}));
        buttons.Controls.Add(Make("선택 기록 제거",()=>{store.Remove(list.SelectedItems.Cast<ListViewItem>().Select(i=>((TranslationEntry)i.Tag).Id));RefreshEntries();}));buttons.Controls.Add(Make("Codex 전달 폴더 저장",Export));buttons.Controls.Add(Make("목록 경로 복사",()=>Clipboard.SetText(store.IndexPath)));var close=Make("닫기",Close);buttons.Controls.Add(close);CancelButton=close;layout.Controls.Add(buttons,0,4);RefreshEntries();
    }
    Button Make(string text,Action action){var b=new Button{Text=text,AutoSize=true};b.Click+=delegate{try{action();}catch(Exception e){MessageBox.Show(this,e.Message,"미번역 목록");}};return b;}
    static string ResourceLabel(TranslationEntry e){return Path.GetFileName(e.Texture.Source.Path)+" @0x"+e.Texture.Source.Offset.ToString("X");}
    static string Describe(TranslationEntry e){return e.Texture.Source.Path+"\r\n"+ResourceLabel(e)+"\r\nID: "+e.Id+"\r\n"+e.Note+(e.ResourceImages==null?"":"\r\n"+string.Join(" / ",e.ResourceImages.Select(i=>i.Texture.Name)));}
    void RefreshEntries(){filtered=store.Entries().Where(e=>(e.Texture.Source.Path+" "+e.Texture.Locator+" "+e.Note+" "+e.Id).IndexOf(query.Text.Trim(),StringComparison.OrdinalIgnoreCase)>=0).ToList();page=0;ShowPage();}
    void ShowPage(){list.BeginUpdate();try{list.Items.Clear();foreach(var e in filtered.Skip(page*200).Take(200)){var row=new ListViewItem(new[]{ResourceLabel(e),(e.ResourceImages==null?1:e.ResourceImages.Count)+"개 이미지",e.Note});row.Tag=e;list.Items.Add(row);}}finally{list.EndUpdate();}detail.Clear();count.Text="리소스 "+filtered.Count+"개 · "+(page+1)+" / "+Math.Max(1,(filtered.Count+199)/200)+" 페이지";}
    void Export(){using(var d=new FolderBrowserDialog{Description="전체 미번역 목록을 Codex에 전달할 폴더를 저장할 위치"})if(d.ShowDialog(this)==DialogResult.OK){string path,parent=d.SelectedPath;try{using(var cts=new CancellationTokenSource())path=LoadingDialog.Run(this,"미번역 목록 저장","전달 파일을 저장하고 있습니다.",cts,(token,progress)=>store.Export(parent,token));}catch(OperationCanceledException){detail.Text="저장 취소 · 생성 중이던 .partial 폴더는 미완성입니다.";return;}detail.Text="저장 완료: "+path+"\r\nCodex에 이 폴더 경로를 알려주세요.";Clipboard.SetText(path);}}
}
}
