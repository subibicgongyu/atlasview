using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace AtlasView {
internal static class HideNavigation {
    internal static Texture Next(List<Texture> items,int index,Func<Texture,bool> hidden){
        for(int i=Math.Max(0,index+1);i<items.Count;i++)if(items[i].Supported && !hidden(items[i]))return items[i];
        for(int i=Math.Min(index-1,items.Count-1);i>=0;i--)if(items[i].Supported && !hidden(items[i]))return items[i];
        return null;
    }
    internal static bool ShortcutAllowed(Keys keys,Control active){
        if(keys!=Keys.H)return false;
        while(active is ContainerControl && ((ContainerControl)active).ActiveControl!=null)active=((ContainerControl)active).ActiveControl;
        for(Control c=active;c!=null;c=c.Parent)if(c is TextBoxBase || c is ComboBox || c is UpDownBase)return false;
        return true;
    }
}
public sealed class HiddenImageEntry {
    public string Key,SourcePath,Name,Locator,Format;
    public int Width,Height,DataOffset;
    public long SourceOffset,HiddenUtcTicks;
    public Texture PreviewTexture;
}
public sealed class HiddenImageIndex {
    public int Version=1;
    public Dictionary<string,HiddenImageEntry> Images=new Dictionary<string,HiddenImageEntry>();
}
internal sealed class HiddenImageStore {
    internal readonly string FilePath;HiddenImageIndex index=new HiddenImageIndex();readonly bool resources;
    internal string LastError="";
    internal HiddenImageStore(string path,bool resourceMode=false){resources=resourceMode;FilePath=Path.GetFullPath(path);Reload();}
    // Stable across disk-cache eviction, process restarts and source timestamp changes.
    internal static string Key(Texture t){return Bin.TextHash(string.Join("|",new object[]{"hidden-v1",Path.GetFullPath(t.Source.Path).ToUpperInvariant(),t.Source.Offset,t.DataOffset,t.StandardImage,t.Format,t.Width,t.Height,t.Pitch,t.BytesPerPixel,t.Swizzle,t.BigEndian,t.RMask,t.GMask,t.BMask,t.AMask}));}
    internal bool Contains(Texture texture){return index.Images.ContainsKey(resources?ResourceGroup.Key(texture.Source):Key(texture));}
    internal int Count {get{return index.Images.Count;}}
    internal List<HiddenImageEntry> Entries(){return index.Images.Values.OrderByDescending(e=>e.HiddenUtcTicks).ToList();}
    static void Plain(string path){if(File.Exists(path)||Directory.Exists(path))Bin.Need((File.GetAttributes(path)&FileAttributes.ReparsePoint)==0,"숨김 설정 경로 링크는 사용할 수 없습니다.");}
    HiddenImageIndex Read(){
        Plain(Path.GetDirectoryName(FilePath));Plain(FilePath);if(!File.Exists(FilePath))return new HiddenImageIndex();
        Bin.Need(new FileInfo(FilePath).Length<=16*1024*1024,"숨김 목록 파일 크기 초과");
        var value=new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Deserialize<HiddenImageIndex>(File.ReadAllText(FilePath,Encoding.UTF8));
        Bin.Need(value!=null && value.Version==1 && value.Images!=null,"숨김 목록 형식 오류");
        foreach(var item in value.Images)Bin.Need(item.Key.Length==64 && item.Value!=null && item.Value.Key==item.Key && item.Value.SourcePath!=null && item.Value.HiddenUtcTicks>0 && item.Value.HiddenUtcTicks<=DateTime.MaxValue.Ticks,"숨김 항목 형식 오류");return value;
    }
    internal bool Reload(){try{index=Read();LastError="";return true;}catch(Exception e){LastError=e.Message;return false;}}
    bool Change(Action<HiddenImageIndex> edit){
        try{using(var mutex=new Mutex(false,"Local\\PS3AtlasHidden_"+Bin.TextHash(FilePath.ToUpperInvariant()))){bool held=false;try{
            try{held=mutex.WaitOne(2000);}catch(AbandonedMutexException){held=true;}if(!held)throw new IOException("다른 뷰어가 숨김 목록을 저장 중입니다.");
            var next=Read();edit(next);string directory=Path.GetDirectoryName(FilePath);Directory.CreateDirectory(directory);Plain(directory);
            byte[] bytes=Encoding.UTF8.GetBytes(new JavaScriptSerializer{MaxJsonLength=16*1024*1024}.Serialize(next));Bin.Need(bytes.Length<=16*1024*1024,"숨김 목록 파일 크기 초과");string temp=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
            try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}if(File.Exists(FilePath))File.Replace(temp,FilePath,null);else File.Move(temp,FilePath);}finally{if(File.Exists(temp))File.Delete(temp);}
            index=next;LastError="";return true;
        }finally{if(held)mutex.ReleaseMutex();}}}catch(Exception e){LastError=e.Message;return false;}
    }
    internal bool Hide(Texture t){return HideMany(new[]{t});}
    internal bool HideMany(IEnumerable<Texture> textures){var items=textures.ToList();return Change(next=>{foreach(var t in items){string key=resources?ResourceGroup.Key(t.Source):Key(t);if(!next.Images.ContainsKey(key))next.Images.Add(key,new HiddenImageEntry{Key=key,SourcePath=Path.GetFullPath(t.Source.Path),Name=resources?"리소스 전체":t.Name,Locator=resources?Path.GetFileName(t.Source.Path)+" @0x"+t.Source.Offset.ToString("X")+" / 리소스 전체":t.Locator,Format=resources?"전체":t.Format,Width=resources?0:t.Width,Height=resources?0:t.Height,SourceOffset=t.Source.Offset,DataOffset=t.DataOffset,PreviewTexture=t.Container=="수동 해석"?t.Copy():null,HiddenUtcTicks=DateTime.UtcNow.Ticks});}});}
    internal bool Restore(IEnumerable<string> keys){var remove=keys.ToArray();return Change(next=>{foreach(string key in remove)next.Images.Remove(key);});}
    internal bool RestoreAll(){return Change(next=>next.Images.Clear());}
}

}
