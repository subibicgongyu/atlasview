using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Drawing;
using System.Drawing.Imaging;
using System.Web.Script.Serialization;
using System.Collections.Generic;

namespace AtlasView {
public static class Program {
    [STAThread] public static int Main(string[] args) {
        try {
            if(args.Length>0 && args[0]=="--selftest"){Tests.Run(args.Length>1?args[1]:"artifacts");return 0;}
            if(args.Length>=3 && args[0]=="--scan") {
                string output=Path.GetFullPath(args[2]);var watch=System.Diagnostics.Stopwatch.StartNew();
                Bin.Need(!output.StartsWith(Path.GetFullPath(args[1]).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase) && !output.Equals(Path.GetFullPath(args[1]),StringComparison.OrdinalIgnoreCase),"보고서 폴더는 입력 폴더 밖에 지정하세요.");
                Directory.CreateDirectory(output);
                var r=Scanner.Scan(args[1],args.Contains("--deep"),CancellationToken.None,s=>Console.WriteLine(s));Reports.Csv(r,Path.Combine(output,"index.csv"),args.Contains("--overwrite"));
                var decoded=new List<object>();int failures=0;
                foreach(var t in r.Items.Where(t=>t.Supported).Where((t,i)=>i<24 || i%83==0)) {
                    try{using(var bm=Pixels.Load(t,CancellationToken.None)) {string pixelHash=Bin.Hash(Pixels.Bgra(bm));decoded.Add(new{locator=t.Locator,width=bm.Width,height=bm.Height,bgraSha256=pixelHash});
                        if(decoded.Count<=12)bm.Save(Path.Combine(output,"preview-"+decoded.Count.ToString("D2")+".png"),ImageFormat.Png);
                    }}catch(Exception e){failures++;r.Messages.Add(t.Locator+": "+e.Message);}
                }
                var report=new {game=r.Game,root=r.Root,files=r.Files,items=r.Items.Count,supported=r.Items.Count(t=>t.Supported),unsupported=r.Items.Count(t=>!t.Supported),formats=r.Items.Where(t=>t.Supported).GroupBy(t=>t.Container+" / "+t.Format).ToDictionary(g=>g.Key,g=>g.Count()),sampleDecodeCount=decoded.Count,sampleDecodeFailures=failures,decoded=decoded,messages=r.Messages,elapsedSeconds=watch.Elapsed.TotalSeconds,gameExecuted=false};
                File.WriteAllText(Path.Combine(output,"scan-report.json"),new JavaScriptSerializer{MaxJsonLength=int.MaxValue}.Serialize(report),Encoding.UTF8);Console.WriteLine("RESULT "+new JavaScriptSerializer().Serialize(new{r.Files,items=r.Items.Count,decoded=decoded.Count,failures}));return failures==0?0:1;
            }
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);Application.Run(new MainForm(args.Length>0?args[0]:null));return 0;
        }catch(Exception e){if(args.Length>0 && args[0].StartsWith("--")){Console.Error.WriteLine(e);return 1;}MessageBox.Show(e.ToString(),"PS3 Atlas Viewer");return 1;}
    }
}
public static class Tests {
    static int count;static List<string> passed=new List<string>();
    static void Check(bool ok,string name){if(!ok)throw new Exception("FAIL: "+name);count++;passed.Add(name);}
    static void Reject(Action action,string name){try{action();}catch(InvalidDataException){Check(true,name);return;}throw new Exception("Expected rejection: "+name);}
    static void U(byte[] b,int p,uint n,bool be=true){for(int i=0;i<4;i++)b[p+i]=(byte)(n>>(8*(be?3-i:i)));}
    static void S(byte[] b,int p,string text){Encoding.ASCII.GetBytes(text).CopyTo(b,p);}
    static byte[] Dds(string fmt,int w=4,int h=4){int n=fmt=="DXT1"?8:16;var b=new byte[128+n*((w+3)/4)*((h+3)/4)];S(b,0,"DDS ");U(b,4,124,false);U(b,8,0x81007,false);U(b,12,(uint)h,false);U(b,16,(uint)w,false);U(b,20,(uint)(b.Length-128),false);U(b,76,32,false);U(b,80,4,false);S(b,84,fmt);U(b,108,0x1000,false);return b;}
    static Texture Parse(byte[] b){return Formats.Parse(new Source{Label="fixture",Length=b.Length},b)[0];}
    static byte[] Decode(byte[] b){return Pixels.Decode(b,Parse(b),CancellationToken.None);}
    public static void Run(string output){Directory.CreateDirectory(output);count=0;passed.Clear();
        var bc1=Dds("DXT1");bc1[128]=0;bc1[129]=0xf8;bc1[130]=0xe0;bc1[131]=7;bc1[132]=0xe4;
        var p=Decode(bc1);Check(p[2]==255 && p[1]==0 && p[3]==255,"BC1 red endpoint");Check(p[4]==0 && p[5]==255 && p[6]==0,"BC1 green endpoint");Check(p[10]==170 && p[9]==85 && p[14]==85 && p[13]==170,"BC1 both interpolation indices");
        var alpha=Dds("DXT1");alpha[132]=255;p=Decode(alpha);Check(p[3]==0,"BC1 transparent mode");
        var bc2=Dds("DXT3");bc2[128]=0xF1;bc2[137]=0xf8;p=Decode(bc2);Check(p[3]==17 && p[7]==255 && p[2]==255,"BC2 explicit 4-bit alpha");
        var bc3=Dds("DXT5");bc3[128]=240;bc3[129]=30;bc3[130]=0x88;bc3[131]=0xc6;bc3[132]=0xfa;bc3[137]=0xf8;p=Decode(bc3);Check(p[3]==240 && p[7]==30 && p[11]==210,"BC3 eight-alpha interpolation");
        bc3[128]=10;bc3[129]=110;p=Decode(bc3);Check(p[11]==30 && p[27]==0 && p[31]==255,"BC3 six-alpha mode and extremes");
        var odd=Dds("DXT1",3,2);odd[129]=0xf8;p=Decode(odd);Check(p.Length==24 && p[22]==255,"Non-multiple-of-four BC dimensions");
        var raw=new Texture{Width=2,Height=2,Format="ARGB8",BytesPerPixel=4,Pitch=12,DataLength=20};byte[] rb={255,255,0,0,128,0,255,0,99,99,99,99,64,0,0,255,0,7,8,9};p=Pixels.Decode(rb,raw,CancellationToken.None);Check(p[2]==255 && p[7]==128 && p[8]==255 && p[15]==0,"ARGB order and padded pitch");
        var morton=new Texture{Width=4,Height=2,Format="L8",BytesPerPixel=1,Swizzle=true,DataLength=8};p=Pixels.Decode(new byte[]{1,2,5,6,3,4,7,8},morton,CancellationToken.None);Check(Enumerable.Range(0,8).All(i=>p[i*4]==i+1),"Rectangular Morton independent known ordering");
        var mask=new Texture{Width=1,Height=1,Format="MASK",BytesPerPixel=2,BigEndian=true,RMask=0xf800,GMask=0x7e0,BMask=31,DataLength=2};p=Pixels.Decode(new byte[]{0xf8,0},mask,CancellationToken.None);Check(p[2]==255 && p[0]==0,"Big-endian RGB565");
        var gtf=new byte[160];U(gtf,0,0x020200ff);U(gtf,4,32);U(gtf,8,1);U(gtf,16,128);U(gtf,20,32);gtf[24]=0xa5;gtf[25]=1;gtf[26]=2;gtf[33]=2;gtf[35]=2;gtf[37]=1;U(gtf,40,16);gtf[128]=255;gtf[129]=255;p=Decode(gtf);Check(p[2]==255 && Parse(gtf).Pitch==16,"GTF big-endian metadata and linear ARGB");
        var g1t=new byte[60];S(g1t,0,"G1TG0050");U(g1t,8,60);U(g1t,12,28);U(g1t,16,1);U(g1t,20,1);U(g1t,28,4);g1t[33]=8;g1t[34]=0x22;g1t[40]=255;g1t[49]=0xf8;p=Decode(g1t);Check(p[2]==255 && p[3]==255,"G1T 0050 texture offset and BC3");
        var lz=new byte[32];S(lz,0,"LZP2");U(lz,4,0x5c8f823f);U(lz,8,8,false);U(lz,12,16,false);lz[16]=2;lz[17]=65;lz[18]=66;lz[19]=0x98;lz[20]=1;
        Check(Encoding.ASCII.GetString(Lzp2.Decode(lz,CancellationToken.None))=="ABABABAB","LZP2 overlapping back-reference");
        var run=(byte[])lz.Clone();U(run,8,4,false);Array.Clear(run,16,16);run[16]=64;run[18]=120;Check(Lzp2.Decode(run,CancellationToken.None).All(v=>v==120),"LZP2 run length");
        var bad=(byte[])lz.Clone();bad[20]=200;Reject(()=>Lzp2.Decode(bad,CancellationToken.None),"Reject LZ reference before output");bad=(byte[])lz.Clone();bad[31]=1;Reject(()=>Lzp2.Decode(bad,CancellationToken.None),"Reject nonzero terminal padding");
        Reject(()=>Pixels.Decode(new byte[7],morton,CancellationToken.None),"Reject truncated texture");raw.Width=8193;Reject(()=>Pixels.ByteCount(raw),"Reject excessive image dimension");
        var truncated=bc1.Take(130).ToArray();Check(!Parse(truncated).Supported,"DDS truncation reported as unsupported");var huge=(byte[])bc1.Clone();U(huge,16,uint.MaxValue,false);Reject(()=>Parse(huge),"Reject overflowing DDS dimension");
        var dx10=Dds("DX10");Check(!Parse(dx10).Supported,"Unknown FourCC remains explicitly unsupported");var foreign=(byte[])g1t.Clone();U(foreign,20,2);Check(!Parse(foreign).Supported,"G1T platform mismatch is not silently decoded");
        var cts=new CancellationTokenSource();cts.Cancel();try{Pixels.Decode(bc1,Parse(bc1),cts.Token);throw new Exception("Cancellation missed");}catch(OperationCanceledException){Check(true,"Decoder cancellation");}cts.Dispose();
        string fixtures=Path.Combine(output,"fixtures");Directory.CreateDirectory(fixtures);File.WriteAllBytes(Path.Combine(fixtures,"sample.dds"),bc1);File.WriteAllBytes(Path.Combine(fixtures,"sample.gtf"),gtf);File.WriteAllBytes(Path.Combine(fixtures,"sample.g1t"),g1t);File.WriteAllBytes(Path.Combine(fixtures,"unsupported.dds"),dx10);
        byte[] packed=new byte[4096+bc1.Length];Buffer.BlockCopy(bc1,0,packed,4096,bc1.Length);File.WriteAllBytes(Path.Combine(fixtures,"embedded.bin"),packed);
        var before=Directory.GetFiles(fixtures).ToDictionary(x=>x,x=>Bin.FileHash(x));var scan=Scanner.Scan(fixtures,true,CancellationToken.None,s=>{});Check(scan.Items.Count(t=>t.Supported && new[]{"sample.dds","sample.gtf","sample.g1t","embedded.bin"}.Contains(Path.GetFileName(t.Source.Path)))==4,"Deep scan finds embedded DDS with extensionless container");Check(before.All(x=>Bin.FileHash(x.Key)==x.Value),"Input bytes unchanged by scan and decode");
        using(var bm=Pixels.Bitmap(Decode(bc1),4,4)){bm.Save(Path.Combine(output,"bc1.png"),ImageFormat.Png);using(var loaded=new Bitmap(Path.Combine(output,"bc1.png")))Check(loaded.GetPixel(0,0).R==255 && loaded.GetPixel(1,0).G==255,"PNG round trip independent GDI read");}
        using(var bm=Pixels.Bitmap(new byte[]{3,7,11,0,55,66,77,128},2,1)){string file=Path.Combine(fixtures,"alpha.png");bm.Save(file,ImageFormat.Png);var tex=new Texture{Source=new Source{Path=file,Length=(int)new FileInfo(file).Length},StandardImage=true};using(var loaded=Pixels.Load(tex,CancellationToken.None))Check(Pixels.Bgra(loaded).SequenceEqual(Pixels.Bgra(bm)),"PNG hidden RGB and partial alpha are preserved");}
        var g1argb=new byte[68];S(g1argb,0,"G1TG0060");U(g1argb,8,68);U(g1argb,12,28);U(g1argb,16,1);U(g1argb,20,1);U(g1argb,28,4);g1argb[33]=1;g1argb[34]=0x11;U(g1argb,40,12);g1argb[52]=127;g1argb[53]=255;p=Decode(g1argb);Check(p[2]==255 && p[3]==127,"G1T 0060 extended header and ARGB");
        // Exercise the real display path, including alpha projection, without opening a game.
        using(var canvas=new AtlasCanvas())using(var target=new Bitmap(2,1))using(var bm=Pixels.Bitmap(new byte[]{3,7,11,0,55,66,77,128},2,1)){canvas.Size=new Size(2,1);canvas.Image=bm;canvas.Zoom=1;canvas.PanX=canvas.PanY=0;canvas.Channel=2;canvas.DrawToBitmap(target,new Rectangle(0,0,2,1));var first=target.GetPixel(0,0);var second=target.GetPixel(1,0);Check(first.R==0 && second.R==128 && second.A==255,"Alpha display projects alpha into opaque grayscale");canvas.Image=null;}
        var rawInput=Parse(bc1);rawInput.Source.Path=Path.Combine(fixtures,"sample.dds");using(var dialog=new RawDialog(rawInput)){dialog.Build();Check(dialog.Result.Source.Offset==128 && dialog.Result.Source.Length==8,"RAW dialog creates bounded source from DDS offset");using(var bm=Pixels.Load(dialog.Result,CancellationToken.None))Check(Pixels.Bgra(bm).SequenceEqual(Decode(bc1)),"RAW dialog preview matches automatic DDS pixels");}
        using(var dialog=new RawDialog(new Texture{Source=new Source{Path=Path.Combine(fixtures,"sample.dds"),Length=136},Name="unknown"})){Check(dialog!=null,"RAW dialog opens for unknown input with 256px defaults");}
        LoadingTests(output,bc1,lz);

        HiddenImagesTests.Run(output,Check);
        HiddenPreviewTests.Run(output,Check);
        TranslationQueueTests.Run(output,Check);
        ResourceTests.Run(output,Check);
        ResourceBatchTests.Run(output,Check);
        var page=new TexturePage();var allTextures=Enumerable.Range(0,13989).Select(i=>new Texture{Name="image "+i}).ToList();page.Reset(allTextures);
        Check(page.Items.Count==200 && page.Count==70,"Large scan exposes at most 200 native list rows and all 70 pages");page.Move(69);Check(page.Items.Count==189 && page.Items.Last()==allTextures.Last(),"Final page retains every remainder image");page.Move(999);Check(page.Number==69,"Page index clamps to final page");page.Move(-1);Check(page.Number==0,"Page index clamps to first page");page.Reset(allTextures.Where(t=>t.Name=="image 13000").ToList());Check(page.Items.Single().Name=="image 13000" && page.Number==0,"Global search finds items outside current page and resets page");
        File.WriteAllText(Path.Combine(output,"tests.json"),new JavaScriptSerializer().Serialize(new{passed=count,cases=passed,gameExecuted=false}),Encoding.UTF8);Console.WriteLine("PASS "+count+" tests");
    }
    static void LoadingTests(string output,byte[] dds,byte[] lzp){
        string path=Path.Combine(output,"cache.dds");File.WriteAllBytes(path,dds);var t=Parse(dds);t.Source.Path=path;var loader=new PreviewLoader();var phases=new List<string>();
        using(var bm=loader.Load(t,CancellationToken.None,phases.Add))Check(Pixels.Bgra(bm).SequenceEqual(Decode(dds)) && loader.BytesRead==8,"Preview reads only base-mip payload and preserves pixels");
        using(var bm=loader.Load(t,CancellationToken.None,phases.Add)){Check(loader.Reads==1 && loader.Decodes==1 && loader.ImageHits==1,"Repeat preview bypasses file read and decode");bm.SetPixel(0,0,Color.Blue);}
        using(var bm=loader.Load(t,CancellationToken.None,phases.Add))Check(bm.GetPixel(0,0).R==255,"Caller bitmap modification and disposal cannot corrupt cache");
        Check(loader.LastOrigin=="메모리 캐시" && loader.MemorySummary.Contains("1장"),"Preview reports actual memory cache origin and retained image count");
        var changed=(byte[])dds.Clone();changed[128]=31;changed[129]=0;File.WriteAllBytes(path,changed);File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddMinutes(1));
        using(var bm=loader.Load(t,CancellationToken.None,phases.Add))Check(loader.Reads==2 && Pixels.Bgra(bm).SequenceEqual(Decode(changed)),"File timestamp change invalidates cached pixels");
        loader.Clear();using(var bm=loader.Load(t,CancellationToken.None,phases.Add))Check(loader.Reads==3,"Rescan clears cached preview buffers");
        var cache=new BufferCache(4);cache.Put("a",new byte[2]);cache.Put("b",new byte[2]);cache.Get("a");cache.Put("c",new byte[2]);Check(cache.Get("a")!=null && cache.Get("b")==null && cache.Get("c")!=null,"Cache evicts least recently used buffer within byte budget");cache.Put("large",new byte[5]);Check(cache.Get("large")==null,"Oversized buffer is not retained");
        path=Path.Combine(output,"cache.lzp");File.WriteAllBytes(path,lzp);t=new Texture{Source=new Source{Path=path,Length=lzp.Length,Lzp=true},Width=4,Height=2,Format="L8",BytesPerPixel=1,DataLength=8};loader=new PreviewLoader();
        using(var bm=loader.Load(t,CancellationToken.None,phases.Add))Check(bm.GetPixel(0,0).R==65,"Compressed preview preserves LZP2 output");t=t.Copy();t.Width=1;t.Height=1;t.DataOffset=1;t.DataLength=1;
        using(var bm=loader.Load(t,CancellationToken.None,phases.Add))Check(bm.GetPixel(0,0).R==66 && loader.Reads==1 && loader.SourceHits==1,"Different texture reuses decompressed source");
        using(var cancelled=new CancellationTokenSource()){cancelled.Cancel();try{loader.Load(t,cancelled.Token,phases.Add);throw new Exception("Cancellation missed");}catch(OperationCanceledException){Check(loader.Reads==1,"Cancelled cached load stops before IO");}}
        using(var cts=new CancellationTokenSource())using(var timer=new System.Windows.Forms.Timer{Interval=100}){
            bool modal=false;timer.Tick+=delegate{foreach(Form form in Application.OpenForms)if(form is LoadingDialog){modal=form.Modal;((LoadingDialog)form).RequestCancel();timer.Stop();break;}};timer.Start();
            try{LoadingDialog.Run<int>(null,"읽는 중 — 취소 검사","자동 검사",cts,(token,report)=>{report("취소 동작 확인 중…");if(!token.WaitHandle.WaitOne(5000))throw new Exception("Modal timer did not cancel");token.ThrowIfCancellationRequested();return 0;});throw new Exception("Cancellation missed");}catch(OperationCanceledException){Check(modal,"Modal stays responsive and propagates cancellation");}
        }
        using(var cts=new CancellationTokenSource()){var watch=System.Diagnostics.Stopwatch.StartNew();int result=LoadingDialog.Run<int>(null,"읽는 중 — 완료 검사","자동 검사",cts,(token,report)=>7);Check(result==7,"Modal closes automatically on worker completion");Check(watch.ElapsedMilliseconds>=250,"Instant work retains visible completed progress feedback for minimum display time");}
        using(var cts=new CancellationTokenSource()){Reject(()=>LoadingDialog.Run<int>(null,"읽는 중 — 오류 검사","자동 검사",cts,(token,report)=>{throw new InvalidDataException("test");}),"Modal closes and propagates worker error");}
        using(var host=new Form())using(var items=new ListView{VirtualMode=true,View=View.Details,Dock=DockStyle.Fill}){
            host.Controls.Add(items);items.Columns.Add("fixture",150);items.RetrieveVirtualItem+=delegate(object sender,RetrieveVirtualItemEventArgs e){e.Item=new ListViewItem("image "+e.ItemIndex);};items.VirtualListSize=14000;
            int calls=0,last=-1;bool notification=false,reentered=false;var queue=new SelectionPreviewQueue(host,delegate{calls++;last=items.SelectedIndices.Count==0?-1:items.SelectedIndices[0];reentered|=notification;});
            items.SelectedIndexChanged+=delegate{notification=true;queue.Schedule();notification=false;};host.Show();Application.DoEvents();calls=0;
            items.SelectedIndices.Add(13999);items.SelectedIndices.Clear();items.SelectedIndices.Add(12000);
            Check(calls==0,"14000-item list defers preview until native selection notification returns");Application.DoEvents();Check(calls==1 && last==12000 && !reentered,"Queued selection coalesces rapid changes and previews only final item");host.Close();
        }
    }
}
}
