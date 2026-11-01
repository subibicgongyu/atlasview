using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AtlasView {
// Do not enter a modal message loop inside a native ListView selection notification.
internal sealed class SelectionPreviewQueue {
    readonly Control owner;readonly Action preview;int version;
    internal SelectionPreviewQueue(Control control,Action action){owner=control;preview=action;}
    internal void Schedule(){int requested=++version;if(owner.IsDisposed || !owner.IsHandleCreated)return;owner.BeginInvoke((Action)delegate{if(!owner.IsDisposed && requested==version)preview();});}
}
// Managed buffers only: each caller owns its Bitmap independently of eviction.
internal sealed class BufferCache {
    sealed class Entry { public string Key;public byte[] Bytes; }
    readonly long budget;long used;readonly object gate=new object();
    readonly Dictionary<string,LinkedListNode<Entry>> entries=new Dictionary<string,LinkedListNode<Entry>>();
    readonly LinkedList<Entry> order=new LinkedList<Entry>();
    public BufferCache(long bytes){budget=bytes;}
    internal int Count {get{lock(gate)return entries.Count;}}
    internal long UsedBytes {get{lock(gate)return used;}}
    public byte[] Get(string key){lock(gate){LinkedListNode<Entry> node;if(!entries.TryGetValue(key,out node))return null;order.Remove(node);order.AddFirst(node);return node.Value.Bytes;}}
    public void Put(string key,byte[] bytes){lock(gate){if(bytes.LongLength>budget)return;LinkedListNode<Entry> node;if(entries.TryGetValue(key,out node)){used-=node.Value.Bytes.LongLength;order.Remove(node);entries.Remove(key);}while(used+bytes.LongLength>budget && order.Count>0){var last=order.Last;used-=last.Value.Bytes.LongLength;entries.Remove(last.Value.Key);order.RemoveLast();}entries[key]=order.AddFirst(new Entry{Key=key,Bytes=bytes});used+=bytes.LongLength;}}
    public void Clear(){lock(gate){entries.Clear();order.Clear();used=0;}}
}

internal sealed class PreviewLoader {
    readonly BufferCache sources=new BufferCache(64L*1024*1024),pixels=new BufferCache(128L*1024*1024);
    internal int Reads,Decodes,SourceHits,ImageHits;internal long BytesRead;
    internal string LastOrigin="";
    internal string MemorySummary {get{return string.Format("메모리 이미지 {0}장 · {1:0.#}/128 MiB · 압축 해제 {2:0.#}/64 MiB",pixels.Count,pixels.UsedBytes/1048576.0,sources.UsedBytes/1048576.0);}}
    public PreviewLoader(){}
    internal static string SourceKey(Source s){var f=new FileInfo(s.Path);return string.Join("|",new object[]{f.FullName.ToUpperInvariant(),f.Length,f.LastWriteTimeUtc.Ticks,s.Offset,s.Length,s.Lzp});}
    static string ImageKey(Texture t,string source){return string.Join("|",new object[]{source,t.StandardImage,t.Format,t.Width,t.Height,t.DataOffset,t.DataLength,t.Pitch,t.BytesPerPixel,t.Swizzle,t.BigEndian,t.RMask,t.GMask,t.BMask,t.AMask});}
    public void Clear(){sources.Clear();pixels.Clear();}
    public Bitmap Load(Texture t,CancellationToken token,Action<string> progress){
        LastOrigin="";token.ThrowIfCancellationRequested();Bin.Need(t.Supported,t.Issue);progress("파일 정보 확인 중…");
        string source=SourceKey(t.Source),key=ImageKey(t,source);byte[] rgba=pixels.Get(key);int w=t.Width,h=t.Height;
        // Standard-image cache includes dimensions in its buffer header.
        if(rgba!=null){ImageHits++;LastOrigin="메모리 캐시";progress("메모리 캐시에서 이미지 읽는 중…");token.ThrowIfCancellationRequested();return FromBuffer(rgba);}
        LastOrigin="원본 디코딩";
        if(t.StandardImage){Reads++;BytesRead+=t.Source.Length;progress("이미지 파일 읽는 중…");using(var bm=Pixels.Load(t,token)){w=bm.Width;h=bm.Height;rgba=Pixels.Bgra(bm);}Decodes++;}
        else{
            byte[] data;Texture decoded=t;
            if(t.Source.Lzp){data=sources.Get(source);if(data==null){Reads++;BytesRead+=t.Source.Length;progress("압축 데이터 읽는 중…");data=Bin.Read(t.Source.Path,t.Source.Offset,t.Source.Length,token);progress("LZP2 압축 해제 중…");data=Lzp2.Decode(data,token);token.ThrowIfCancellationRequested();if(source==SourceKey(t.Source))sources.Put(source,data);}else{SourceHits++;LastOrigin="압축 해제 메모리 캐시 + 픽셀 디코딩";}}
            else{int length=Pixels.ByteCount(t);Bin.Need(t.DataOffset>=0 && (long)t.DataOffset+length<=t.Source.Length,"소스 범위 초과");Reads++;BytesRead+=length;progress("픽셀 구간 읽는 중…");data=Bin.Read(t.Source.Path,checked(t.Source.Offset+t.DataOffset),length,token);decoded=t.Copy();decoded.DataOffset=0;}
            progress("텍스처 디코딩 중…");rgba=Pixels.Decode(data,decoded,token);Decodes++;
        }
        token.ThrowIfCancellationRequested();progress("화면 이미지 준비 중…");
        byte[] buffer=ToBuffer(rgba,w,h);
        token.ThrowIfCancellationRequested();if(source==SourceKey(t.Source))pixels.Put(key,buffer);
        return Pixels.Bitmap(rgba,w,h);
    }
    static byte[] ToBuffer(byte[] rgba,int w,int h){byte[] buffer=new byte[rgba.Length+8];Buffer.BlockCopy(BitConverter.GetBytes(w),0,buffer,0,4);Buffer.BlockCopy(BitConverter.GetBytes(h),0,buffer,4,4);Buffer.BlockCopy(rgba,0,buffer,8,rgba.Length);return buffer;}
    static Bitmap FromBuffer(byte[] buffer){int w=BitConverter.ToInt32(buffer,0),h=BitConverter.ToInt32(buffer,4);byte[] rgba=new byte[buffer.Length-8];Buffer.BlockCopy(buffer,8,rgba,0,rgba.Length);return Pixels.Bitmap(rgba,w,h);}
}

internal sealed class LoadingDialog : Form {
    readonly Label detail=new Label();readonly Button cancel=new Button();readonly System.Windows.Forms.Timer timer=new System.Windows.Forms.Timer();
    readonly CancellationTokenSource cancellation;string latest="작업 준비 중…";bool finished;
    readonly ProgressBar bar=new ProgressBar();
    internal LoadingDialog(string title,string item,CancellationTokenSource cts){
        cancellation=cts;Text=title;ClientSize=new Size(520,195);Font=new Font("Malgun Gothic",10);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;
        BackColor=Color.FromArgb(28,32,40);ForeColor=Color.FromArgb(223,230,237);
        var name=new Label{Text=item,AutoEllipsis=true,Location=new Point(20,18),Size=new Size(480,27)};
        detail.Location=new Point(20,52);detail.Size=new Size(480,48);detail.AutoEllipsis=true;
        bar.Location=new Point(20,105);bar.Size=new Size(480,20);bar.Style=ProgressBarStyle.Marquee;bar.MarqueeAnimationSpeed=25;
        cancel.Text="취소";cancel.Location=new Point(400,145);cancel.Size=new Size(100,32);cancel.Click+=delegate{if(finished)Close();else RequestCancel();};
        Controls.AddRange(new Control[]{name,detail,bar,cancel});timer.Interval=80;timer.Tick+=delegate{detail.Text=cancellation.IsCancellationRequested?"취소 중… 현재 작업을 정리하고 있습니다.":Volatile.Read(ref latest);};timer.Start();
        FormClosing+=delegate(object sender,FormClosingEventArgs e){if(!finished){e.Cancel=true;RequestCancel();}};
    }
    internal void Report(string message){Volatile.Write(ref latest,message);}
    string Latest {get{return Volatile.Read(ref latest);}}
    internal void RequestCancel(){cancellation.Cancel();cancel.Enabled=false;detail.Text="취소 중…";}
    protected override void Dispose(bool disposing){if(disposing)timer.Dispose();base.Dispose(disposing);}
    internal static T Run<T>(IWin32Window owner,string title,string item,CancellationTokenSource cts,Func<CancellationToken,Action<string>,T> work){
        T value=default(T);Exception error=null;
        using(var dialog=new LoadingDialog(title,item,cts)){
            dialog.Shown+=async delegate{
                var watch=System.Diagnostics.Stopwatch.StartNew();
                try{dialog.detail.Text="읽는 중…";dialog.Update();value=await Task.Run(()=>work(cts.Token,dialog.Report));
                    dialog.finished=true;dialog.timer.Stop();dialog.detail.Text=cts.IsCancellationRequested?"취소 완료":"완료 · "+dialog.Latest;dialog.bar.Style=ProgressBarStyle.Blocks;dialog.bar.Value=100;dialog.cancel.Text="닫기";dialog.Update();
                    int remaining=300-(int)watch.ElapsedMilliseconds;if(!cts.IsCancellationRequested && remaining>0)await Task.Delay(remaining);
                }catch(Exception e){error=e;}finally{dialog.finished=true;if(!dialog.IsDisposed)dialog.Close();}
            };
            dialog.ShowDialog(owner);
        }
        if(error!=null)System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();return value;
    }
}
}
