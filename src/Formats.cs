using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace AtlasView {
public interface ITextureReader {
    string Name { get; }
    bool Matches(byte[] header);
    List<Texture> Parse(Source source,byte[] data);
}
public static class Formats {
    public static readonly ITextureReader[] Readers={new DdsReader(),new GtfReader(),new G1tReader()};
    public static List<Texture> Parse(Source source,byte[] b) { foreach(var r in Readers)if(r.Matches(b))return r.Parse(source,b);throw new InvalidDataException("지원되는 텍스처 헤더가 없습니다."); }
    public static void Validate(Texture t,byte[] b) {
        if(!t.Supported)return;
        try {int n=Pixels.ByteCount(t);Bin.Range(b,t.DataOffset,n);Bin.Need(n<=t.DataLength,"픽셀 데이터가 서브리소스 범위를 넘습니다.");}
        catch(Exception e){t.Issue=e.Message;}
    }
}
public sealed class DdsReader : ITextureReader {
    public string Name { get {return "DDS";} }
    public bool Matches(byte[] b){return Bin.Magic(b,0,"DDS ");}
    public List<Texture> Parse(Source s,byte[] b) {
        Bin.Range(b,0,128);Bin.Need(Bin.U32(b,4,false)==124 && Bin.U32(b,76,false)==32,"DDS 헤더 크기 오류");
        var t=new Texture{Source=s,Name="texture 0",Container="DDS",Width=Bin.I32(b,16,false),Height=Bin.I32(b,12,false),DataOffset=128,Note="최상위 밉 / 첫 면만 표시"};
        uint flags=Bin.U32(b,80,false);string fourcc=Encoding.ASCII.GetString(b,84,4);
        if((flags&4)!=0){t.Format=fourcc=="DXT1"?"BC1":fourcc=="DXT3"?"BC2":fourcc=="DXT5"?"BC3":fourcc;
            if(t.Format!="BC1" && t.Format!="BC2" && t.Format!="BC3")t.Issue="미지원 DDS FourCC: "+fourcc+" (DX10/BC7 등은 별도 디코더 필요)";
        } else if((flags&0x40)!=0) {
            t.Format="MASK";t.BytesPerPixel=Bin.I32(b,88,false)/8;
            t.RMask=Bin.U32(b,92,false);t.GMask=Bin.U32(b,96,false);t.BMask=Bin.U32(b,100,false);t.AMask=(flags&1)!=0?Bin.U32(b,104,false):0;
            if((Bin.U32(b,8,false)&8)!=0)t.Pitch=Bin.I32(b,20,false);
            if(t.BytesPerPixel<2 || t.BytesPerPixel>4)t.Issue="DDS RGB는 16/24/32비트만 지원";
        } else if((flags&0x20000)!=0 && Bin.U32(b,88,false)==8) {t.Format="L8";t.BytesPerPixel=1;if((Bin.U32(b,8,false)&8)!=0)t.Pitch=Bin.I32(b,20,false);}
        else {t.Format="DDS unknown";t.Issue="미지원 DDS 픽셀 형식";}
        if(Bin.U32(b,24,false)>1)t.Issue="3D DDS는 미지원";
        t.DataLength=b.Length-t.DataOffset;Formats.Validate(t,b);return new List<Texture>{t};
    }
}
public sealed class GtfReader : ITextureReader {
    public string Name { get {return "GTF";} }
    public bool Matches(byte[] b){return b.Length>=12 && b[0]==2 && (Bin.U32(b,0)==0x020200ff || Bin.U32(b,0)==0x020100ff || Bin.U32(b,0)==0x020000ff);}
    public List<Texture> Parse(Source s,byte[] b) {
        int count=Bin.I32(b,8);Bin.Need(count>0 && count<=4096,"GTF 항목 수 오류");Bin.Range(b,12,count*36);var list=new List<Texture>();
        for(int i=0;i<count;i++) {
            int p=12+i*36,g=p+12,fmt=b[g]&0x9f;
            var t=new Texture{Source=s,Name="texture "+i,Container="GTF",Width=Bin.U16(b,g+8),Height=Bin.U16(b,g+10),DataOffset=Bin.I32(b,p+4),DataLength=Bin.I32(b,p+8),Pitch=Bin.I32(b,g+16),BigEndian=true,Note="GTF ID "+Bin.U32(b,p)+" / 밉 "+b[g+1]+" / remap 0x"+Bin.U32(b,g+4).ToString("X8")+" / 최상위 밉·첫 면 / remap 미적용"};
            t.Swizzle=(b[g]&0x20)==0;t.BytesPerPixel=fmt==0x81?1:fmt==0x85?4:2;
            switch(fmt){case 0x81:t.Format="L8";break;case 0x85:t.Format="ARGB8";break;case 0x86:t.Format="BC1";break;case 0x87:t.Format="BC2";break;case 0x88:t.Format="BC3";break;
                case 0x82:t.Format="MASK";t.RMask=0x7c00;t.GMask=0x3e0;t.BMask=31;t.AMask=0x8000;break;
                case 0x83:t.Format="MASK";t.RMask=0xf00;t.GMask=0xf0;t.BMask=15;t.AMask=0xf000;break;
                case 0x84:t.Format="MASK";t.RMask=0xf800;t.GMask=0x7e0;t.BMask=31;break;
                default:t.Format="GCM 0x"+fmt.ToString("X2");t.Issue="미지원 GCM 포맷";break;}
            // RSX DXT payloads are block-linear, independent of the uncompressed swizzle flag.
            if(fmt>=0x86 && fmt<=0x88){t.Swizzle=false;t.Pitch=0;}
            if(b[g+2]!=2 || Bin.U16(b,g+12)>1)t.Issue="2D GTF만 지원";
            if(t.DataOffset<12+36*count || (long)t.DataOffset+t.DataLength>b.Length)t.Issue="GTF 데이터 범위 오류";
            Formats.Validate(t,b);list.Add(t);
        }return list;
    }
}
public sealed class G1tReader : ITextureReader {
    public string Name { get {return "G1T";} }
    public bool Matches(byte[] b){return Bin.Magic(b,0,"G1TG");}
    public List<Texture> Parse(Source s,byte[] b) {
        Bin.Range(b,0,28);string version=Encoding.ASCII.GetString(b,4,4);
        Bin.Need(version=="0050" || version=="0060","G1T 버전 "+version+" 미지원");
        int total=Bin.I32(b,8),table=Bin.I32(b,12),count=Bin.I32(b,16);uint platform=Bin.U32(b,20);
        Bin.Need(total<=b.Length && total>=28 && table>=28 && count>0 && count<=16384 && (long)table+count*4<=total,"G1T 인덱스 범위 오류");
        var offsets=new int[count+1];for(int i=0;i<count;i++){offsets[i]=checked(table+Bin.I32(b,table+i*4));Bin.Need(offsets[i]>=table+count*4 && offsets[i]<=total-8 && (i==0 || offsets[i]>offsets[i-1]),"G1T 텍스처 오프셋 오류");}offsets[count]=total;
        var list=new List<Texture>();for(int i=0;i<count;i++) {
            int p=offsets[i],fmt=b[p+1],dim=b[p+2];
            var t=new Texture{Source=s,Name="texture "+i.ToString("D3"),Container="G1T "+version,Width=1<<(dim>>4),Height=1<<(dim&15),DataOffset=p+8,Format=fmt==6?"BC1":fmt==7?"BC2":fmt==8?"BC3":"G1T 0x"+fmt.ToString("X2"),Note="플랫폼 "+platform+" / texture "+i+" / 최상위 밉"};
            if(version=="0060") {int extra=Bin.I32(b,p+8);Bin.Need(extra>=4 && extra<=256 && (long)p+8+extra<=offsets[i+1],"G1T 확장 헤더 범위 오류");t.DataOffset+=extra;if(extra!=12)t.Issue="미지원 G1T 확장 헤더 길이 "+extra;}
            t.DataLength=offsets[i+1]-t.DataOffset;
            if(platform!=1)t.Issue="G1T 플랫폼 "+platform+" 미지원 (PS3 프로필 1만 지원)";
            if(fmt==1 && version=="0060") {t.Format="ARGB8";t.BytesPerPixel=4;t.Note+=" / 선형 ARGB";}
            else if(fmt!=6 && fmt!=7 && fmt!=8)t.Issue="미지원 G1T 포맷 0x"+fmt.ToString("X2");
            Formats.Validate(t,b);list.Add(t);
        }return list;
    }
}

public interface IContainerReader {
    bool Matches(byte[] header);
    IEnumerable<Source> Entries(string file,byte[] header);
}
public sealed class BnsReader : IContainerReader {
    public bool Matches(byte[] b){return b.Length>=16 && Bin.U32(b,0)==0x19dad;}
    public IEnumerable<Source> Entries(string path,byte[] h) {
        int count=Bin.I32(h,4),unit=Bin.I32(h,8);long length=new FileInfo(path).Length;
        Bin.Need(count>0 && count<=100000 && unit>=16 && unit<=65536 && (unit&(unit-1))==0,"미지원 BNS 인덱스 프로필");
        var table=Bin.Read(path,16,count*8);long end=16+table.Length;
        for(int i=0;i<count;i++) {
            long offset=(long)Bin.U32(table,i*8)*unit;int n=Bin.I32(table,i*8+4);if(n==0)continue;
            Bin.Need(offset>=end && offset<=length-n,"BNS 항목 범위 또는 중첩 오류: "+i);end=offset+n;
            yield return new Source{Path=path,Offset=offset,Length=n,Label=System.IO.Path.GetFileName(path)+" / entry "+i.ToString("D6")};
        }
    }
}

public sealed class ScanResult {
    public List<Texture> Items=new List<Texture>(); public List<string> Messages=new List<string>(); public int Files;public string Root,Game=""; public bool Cancelled;
}
public static class Scanner {
    public static readonly IContainerReader[] Containers={new BnsReader()};
    public static Texture Unknown(Source s,string reason) {return new Texture{Source=s,Name="미지원 / RAW 가능",Container="Unknown",Format="—",Issue=reason};}
    static IEnumerable<string> Files(string root,ScanResult result,CancellationToken token) {
        var dirs=new Stack<string>();dirs.Push(root);
        while(dirs.Count>0){token.ThrowIfCancellationRequested();string d=dirs.Pop();string[] sub,files;
            try{files=Directory.GetFiles(d);sub=Directory.GetDirectories(d);}catch(Exception e){result.Messages.Add(d+": "+e.Message);continue;}
            Array.Sort(files,StringComparer.OrdinalIgnoreCase);foreach(var f in files)yield return f;
            foreach(var child in sub) {try{if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0)dirs.Push(child);else result.Messages.Add("링크 폴더 제외: "+child);}catch(Exception e){result.Messages.Add(child+": "+e.Message);}}
        }
    }
    public static ScanResult Scan(string root,bool deep,CancellationToken token,Action<string> progress) {
        var r=new ScanResult{Root=System.IO.Path.GetFullPath(root)};
        try {foreach(var path in Files(r.Root,r,token)) {
            token.ThrowIfCancellationRequested();r.Files++;progress("파일 "+r.Files+" · "+System.IO.Path.GetFileName(path));
            long length=new FileInfo(path).Length;var s=new Source{Path=path,Offset=0,Length=(int)Math.Min(length,Bin.MaxBytes),Label=path.Substring(r.Root.TrimEnd('\\').Length).TrimStart('\\')};
            try {
                var head=Bin.Read(path,0,(int)Math.Min(128,length));
                if(System.IO.Path.GetFileName(path).Equals("PARAM.SFO",StringComparison.OrdinalIgnoreCase)) {r.Game=ReadSfo(Bin.Read(path,0,(int)Math.Min(length,1048576)));continue;}
                bool container=false;foreach(var c in Containers)if(c.Matches(head)) {
                    container=true;int n=0;foreach(var entry in c.Entries(path,head)){token.ThrowIfCancellationRequested();if((n++%10)==0)progress(s.Label+" · "+entry.Label);
                        try{ParseSource(entry,r,token);}catch(OperationCanceledException){throw;}catch(Exception e){r.Items.Add(Unknown(entry,e.Message));}}
                    break;
                }if(container)continue;
                if(IsStandard(head)){if(length>Bin.MaxBytes){r.Items.Add(Unknown(s,"이미지 파일 128 MiB 한도 초과"));continue;}
                    using(var stream=File.OpenRead(path))using(var im=System.Drawing.Image.FromStream(stream,false,false)) {var t=new Texture{Source=s,Name="image",Container="Image",Format=System.IO.Path.GetExtension(path).TrimStart('.').ToUpperInvariant(),StandardImage=true,Width=im.Width,Height=im.Height};if((long)t.Width*t.Height>16777216)t.Issue="16M 픽셀 한도 초과";r.Items.Add(t);}continue;}
                if(Formats.Readers.Any(x=>x.Matches(head)) || Bin.Magic(head,0,"LZP2")){Bin.Need(length<=Bin.MaxBytes,"텍스처 컨테이너 128 MiB 한도 초과");ParseSource(s,r,token);continue;}
                r.Items.Add(Unknown(s,length>Bin.MaxBytes?"대형 파일: RAW는 지정 오프셋에서 필요한 범위만 읽습니다.":"자동 디코더 없음. RAW 또는 내부 검색을 사용하세요."));
                string ext=System.IO.Path.GetExtension(path).ToLowerInvariant();
                if(deep && ext!=".pam" && ext!=".at3" && ext!=".g1l" && ext!=".pup")Deep(path,r,token,progress);
            } catch(OperationCanceledException){throw;}catch(Exception e){r.Items.Add(Unknown(s,e.Message));r.Messages.Add(s.Label+": "+e.Message);}
        }}catch(OperationCanceledException){r.Cancelled=true;}
        return r;
    }
    static bool IsStandard(byte[] h){return Bin.Magic(h,0,"\u0089PNG\r\n\u001a\n") || Bin.Magic(h,0,"BM") || Bin.Magic(h,0,"GIF8") || (h.Length>=3 && h[0]==255 && h[1]==216 && h[2]==255);}
    static void ParseSource(Source s,ScanResult r,CancellationToken token) {
        Bin.Need(s.Length<=Bin.MaxBytes,"항목당 128 MiB 한도 초과");var h=Bin.Read(s.Path,s.Offset,Math.Min(16,s.Length));s.Lzp=Bin.Magic(h,0,"LZP2");
        if(!s.Lzp && !Formats.Readers.Any(x=>x.Matches(h))){r.Items.Add(Unknown(s,"지원 텍스처 시그니처 없음"));return;}
        byte[] b=s.Read(token);var found=Formats.Parse(s,b);r.Items.AddRange(found);
    }
    static void Deep(string path,ScanResult result,CancellationToken token,Action<string> progress) {
        // Fixed-size windows and a three-byte overlap: no whole-archive allocation.
        const int chunk=4*1024*1024;int candidates=0;long next=1;
        using(var f=File.OpenRead(path)){byte[] buf=new byte[chunk+3];long start=0;
            while(start<f.Length){token.ThrowIfCancellationRequested();f.Position=start;int n=f.Read(buf,0,(int)Math.Min(buf.Length,f.Length-start));
                progress("내부 검색 · "+System.IO.Path.GetFileName(path)+" · "+(start*100/Math.Max(1,f.Length))+"%");
                for(int i=0;i<n-3;i++){if((i&65535)==0)token.ThrowIfCancellationRequested();long pos=start+i;if(pos<next)continue;byte first=buf[i];bool magic=(first==68 && buf[i+1]==68 && buf[i+2]==83 && buf[i+3]==32) || (first==71 && buf[i+1]==49 && buf[i+2]==84 && buf[i+3]==71) || (first==2 && buf[i+3]==255 && buf[i+1]<=2 && buf[i+2]==0);if(!magic)continue;
                    if(++candidates>20000){result.Messages.Add(path+": 내부 검색 후보 20,000개 한도 도달");return;}
                    try{token.ThrowIfCancellationRequested();var h=Bin.Read(path,pos,(int)Math.Min(128,f.Length-pos));long length=0;
                        if(Bin.Magic(h,0,"G1TG"))length=Bin.U32(h,8);
                        else if(Bin.Magic(h,0,"DDS ")){int w=Bin.I32(h,16,false),hh=Bin.I32(h,12,false);Bin.Need(w>0 && hh>0 && w<=8192 && hh<=8192,"크기 오류");length=Math.Min(f.Length-pos,128L+(long)w*hh*4+4096);}
                        else {int count=Bin.I32(h,8);Bin.Need(count>0 && count<=4096,"항목 수 오류");var index=Bin.Read(path,pos,12+count*36);for(int j=0;j<count;j++)length=Math.Max(length,(long)Bin.U32(index,16+j*36)+Bin.U32(index,20+j*36));}
                        Bin.Need(length>=32 && length<=Bin.MaxBytes && pos<=f.Length-length,"내부 후보 범위 오류");var s=new Source{Path=path,Offset=pos,Length=(int)length,Label=System.IO.Path.GetFileName(path)+" @0x"+pos.ToString("X")};var textures=Formats.Parse(s,s.Read(token));
                        if(textures.Any(t=>t.Supported)){result.Items.AddRange(textures);next=pos+4;}
                    }catch(OperationCanceledException){throw;}catch(Exception){/* A signature is only a candidate until structural validation succeeds. */}
                }if(n<=3)break;start+=chunk;
            }
        }
    }
    static string ReadSfo(byte[] b) {
        Bin.Need(Bin.Magic(b,0,"\0PSF"),"SFO 시그니처 오류");int key=Bin.I32(b,8,false),data=Bin.I32(b,12,false),count=Bin.I32(b,16,false);Bin.Need(count<=4096,"SFO 항목 수 오류");var fields=new Dictionary<string,string>();
        for(int i=0;i<count;i++){int p=20+i*16;Bin.Range(b,p,16);int k=key+Bin.U16(b,p,false),v=checked(data+Bin.I32(b,p+12,false)),len=Bin.I32(b,p+4,false);Bin.Range(b,k,1);int end=Array.IndexOf(b,(byte)0,k);Bin.Need(end>=k,"SFO 키 종료 오류");string name=Encoding.UTF8.GetString(b,k,end-k);Bin.Range(b,v,len);if(name=="TITLE" || name=="TITLE_ID" || name=="APP_VER")fields[name]=Encoding.UTF8.GetString(b,v,len).TrimEnd('\0');}
        return string.Join(" · ",new[]{"TITLE","TITLE_ID","APP_VER"}.Where(fields.ContainsKey).Select(k=>fields[k]).ToArray());
    }
}
}
