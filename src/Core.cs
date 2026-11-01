using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;

namespace AtlasView {
public static class Bin {
    public const int MaxBytes = 128 * 1024 * 1024;
    public static void Need(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
    public static uint U32(byte[] b,int p,bool be=true) { Range(b,p,4); return be ? ((uint)b[p]<<24|(uint)b[p+1]<<16|(uint)b[p+2]<<8|b[p+3]) : ((uint)b[p+3]<<24|(uint)b[p+2]<<16|(uint)b[p+1]<<8|b[p]); }
    public static int I32(byte[] b,int p,bool be=true) { uint n=U32(b,p,be); Need(n<=int.MaxValue,"정수 범위 초과"); return (int)n; }
    public static int U16(byte[] b,int p,bool be=true) { Range(b,p,2); return be ? b[p]*256+b[p+1] : b[p]+b[p+1]*256; }
    public static void Range(byte[] b,int p,int n) { Need(p>=0 && n>=0 && (long)p+n<=b.Length,"데이터 범위를 벗어났습니다."); }
    public static bool Magic(byte[] b,int p,string s) { if(p<0 || p+s.Length>b.Length)return false; for(int i=0;i<s.Length;i++)if(b[p+i]!=(byte)s[i])return false; return true; }
    public static byte[] Read(string path,long offset,int length) { return Read(path,offset,length,CancellationToken.None); }
    public static byte[] Read(string path,long offset,int length,CancellationToken token) { token.ThrowIfCancellationRequested();Need(length>=0 && length<=MaxBytes,"읽기 한도 128 MiB 초과"); using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)) { Need(offset>=0 && offset<=f.Length-length,"파일 범위 초과"); f.Position=offset; var b=new byte[length]; int n=0; while(n<length){token.ThrowIfCancellationRequested();int r=f.Read(b,n,Math.Min(1024*1024,length-n));if(r==0)throw new EndOfStreamException();n+=r;}token.ThrowIfCancellationRequested();return b; } }
    public static string Hash(byte[] b) { using(var s=SHA256.Create())return BitConverter.ToString(s.ComputeHash(b)).Replace("-","").ToLowerInvariant(); }
    public static string TextHash(string text) { return Hash(System.Text.Encoding.UTF8.GetBytes(text)); }
    public static string FileHash(string p) { using(var s=SHA256.Create())using(var f=File.OpenRead(p))return BitConverter.ToString(s.ComputeHash(f)).Replace("-","").ToLowerInvariant(); }
}

public sealed class Source {
    public string Path,Label; public long Offset; public int Length; public bool Lzp;
    public byte[] Read(CancellationToken token) { token.ThrowIfCancellationRequested(); var b=Bin.Read(Path,Offset,Length,token); return Lzp ? Lzp2.Decode(b,token) : b; }
}
public sealed class Texture {
    public Source Source;
    public string Name,Container,Format,Issue="",Note="";
    public int Width,Height,DataOffset,DataLength,Pitch,BytesPerPixel;
    public bool Swizzle,BigEndian,StandardImage;
    public uint RMask,GMask,BMask,AMask;
    public bool Supported { get { return Issue.Length==0; } }
    public string Locator { get { return Source.Label+" / "+Name; } }
    public Texture Copy() { return (Texture)MemberwiseClone(); }
}

public static class Lzp2 {
    public static byte[] Decode(byte[] b,CancellationToken token) {
        Bin.Need(b.Length>=16 && Bin.Magic(b,0,"LZP2") && Bin.U32(b,4)==0x5c8f823f,"미지원 LZP2 프로필");
        int size=Bin.I32(b,8,false); Bin.Need(size<=64*1024*1024 && Bin.U32(b,12,false)+16L==b.Length && b.Length%16==0,"LZP2 길이 오류");
        var o=new byte[size]; int p=16,n=0,ticks=0;
        while(n<size) {
            if((ticks++ & 4095)==0)token.ThrowIfCancellationRequested();
            Bin.Range(b,p,1); int c=b[p++],len;
            if(c<64) { len=c; Bin.Range(b,p,len);Bin.Need(n+len<=size,"LZP2 리터럴 범위 오류"); Buffer.BlockCopy(b,p,o,n,len);p+=len;n+=len; }
            else if(c<128) { Bin.Range(b,p,2);len=((c&63)<<8)+b[p++]+4; byte v=b[p++];Bin.Need(n+len<=size,"LZP2 반복 범위 오류");for(int j=0;j<len;j++)o[n++]=v; }
            else { Bin.Range(b,p,1);int distance=((c&7)<<8)+b[p++]+1;len=((c>>3)&15)+3;Bin.Need(distance<=n && n+len<=size,"LZP2 참조 범위 오류");for(int j=0;j<len;j++,n++)o[n]=o[n-distance]; }
        }
        Bin.Need(b.Length-p>=1 && b.Length-p<=16,"LZP2 패딩 길이 오류");for(;p<b.Length;p++)Bin.Need(b[p]==0,"LZP2 패딩 오류");return o;
    }
}

public static class Pixels {
    public static int ByteCount(Texture t) {
        Bin.Need(t.Width>0 && t.Height>0 && t.Width<=8192 && t.Height<=8192 && (long)t.Width*t.Height<=16777216,"이미지 한도: 최대 변 8192 / 16M 픽셀");
        int bpp=t.BytesPerPixel;
        if(t.Format=="BC1" || t.Format=="BC2" || t.Format=="BC3")return checked(((t.Width+3)/4)*((t.Height+3)/4)*(t.Format=="BC1"?8:16));
        Bin.Need(bpp>=1 && bpp<=4,"미지원 픽셀 크기");
        int pitch=t.Pitch==0?t.Width*bpp:t.Pitch; Bin.Need(pitch>=t.Width*bpp,"피치가 행 길이보다 작습니다.");
        if(t.Swizzle) { Bin.Need((t.Width&(t.Width-1))==0 && (t.Height&(t.Height-1))==0,"Morton은 2의 거듭제곱 크기만 지원");return checked(t.Width*t.Height*bpp); }
        return checked(pitch*(t.Height-1)+t.Width*bpp);
    }
    public static int Morton(int x,int y,int w,int h) {
        int address=0,bit=0;
        for(int mask=1;mask<w || mask<h;mask<<=1){if(mask<w){if((x&mask)!=0)address|=1<<bit;bit++;}if(mask<h){if((y&mask)!=0)address|=1<<bit;bit++;}}
        return address;
    }
    static int Channel(uint v,uint mask,int fallback) { if(mask==0)return fallback;int s=0;while((mask&1)==0){s++;mask>>=1;}return (int)(((v>>s)&mask)*255UL/mask); }
    static int[] Rgb565(int v) { int r=v>>11,g=(v>>5)&63,b=v&31;return new[]{(r<<3)|(r>>2),(g<<2)|(g>>4),(b<<3)|(b>>2),255}; }
    public static byte[] Decode(byte[] b,Texture t,CancellationToken token) {
        int bytes=ByteCount(t); Bin.Range(b,t.DataOffset,bytes); if(t.DataLength>0)Bin.Need(bytes<=t.DataLength,"텍스처 선언 범위보다 픽셀 데이터가 큽니다.");
        var o=new byte[t.Width*t.Height*4];
        if(t.Format=="BC1" || t.Format=="BC2" || t.Format=="BC3") {
            Bin.Need(!t.Swizzle,"블록 Morton은 자동 적용하지 않습니다.");
            int blocks=(t.Width+3)/4,blockSize=t.Format=="BC1"?8:16;
            for(int by=0;by<(t.Height+3)/4;by++) { token.ThrowIfCancellationRequested();for(int bx=0;bx<blocks;bx++) {
                int p=t.DataOffset+(by*blocks+bx)*blockSize,cp=p+(blockSize==8?0:8);
                int c0=Bin.U16(b,cp,false),c1=Bin.U16(b,cp+2,false);int[][] colors={Rgb565(c0),Rgb565(c1),new int[4],new int[4]};
                for(int k=0;k<3;k++) { if(c0>c1 || blockSize==16){colors[2][k]=(2*colors[0][k]+colors[1][k])/3;colors[3][k]=(colors[0][k]+2*colors[1][k])/3;}else colors[2][k]=(colors[0][k]+colors[1][k])/2; }
                colors[2][3]=255;colors[3][3]=(c0>c1 || blockSize==16)?255:0;
                uint codes=Bin.U32(b,cp+4,false);int[] alpha=new int[8];ulong acodes=0;
                if(t.Format=="BC3") {alpha[0]=b[p];alpha[1]=b[p+1]; if(alpha[0]>alpha[1])for(int a=2;a<8;a++)alpha[a]=((8-a)*alpha[0]+(a-1)*alpha[1])/7;else{for(int a=2;a<6;a++)alpha[a]=((6-a)*alpha[0]+(a-1)*alpha[1])/5;alpha[6]=0;alpha[7]=255;}for(int a=0;a<6;a++)acodes|=(ulong)b[p+2+a]<<(8*a);}
                for(int n=0;n<16;n++) {int x=bx*4+n%4,y=by*4+n/4;if(x>=t.Width || y>=t.Height)continue;int[] c=colors[(codes>>(2*n))&3];int a=c[3];if(t.Format=="BC2")a=((b[p+n/2]>>((n%2)*4))&15)*17;else if(t.Format=="BC3")a=alpha[(acodes>>(3*n))&7];int d=(y*t.Width+x)*4;o[d]=(byte)c[2];o[d+1]=(byte)c[1];o[d+2]=(byte)c[0];o[d+3]=(byte)a;}
            }}return o;
        }
        int stride=t.Pitch==0?t.Width*t.BytesPerPixel:t.Pitch;
        for(int y=0;y<t.Height;y++) {token.ThrowIfCancellationRequested();for(int x=0;x<t.Width;x++) {
            int p=t.DataOffset+(t.Swizzle?Morton(x,y,t.Width,t.Height)*t.BytesPerPixel:y*stride+x*t.BytesPerPixel),d=(y*t.Width+x)*4;
            int r=0,g=0,bl=0,a=255;
            if(t.Format=="L8" || t.Format=="A8") {r=g=bl=t.Format=="A8"?255:b[p];if(t.Format=="A8")a=b[p];}
            else if(t.Format=="RGBA8") {r=b[p];g=b[p+1];bl=b[p+2];a=b[p+3];}
            else if(t.Format=="BGRA8") {bl=b[p];g=b[p+1];r=b[p+2];a=b[p+3];}
            else if(t.Format=="ARGB8") {a=b[p];r=b[p+1];g=b[p+2];bl=b[p+3];}
            else if(t.Format=="MASK") {uint v=0;for(int k=0;k<t.BytesPerPixel;k++)v|=(uint)b[p+k]<<((t.BigEndian?t.BytesPerPixel-1-k:k)*8);r=Channel(v,t.RMask,0);g=Channel(v,t.GMask,0);bl=Channel(v,t.BMask,0);a=Channel(v,t.AMask,255);}
            else throw new InvalidDataException("미지원 픽셀 포맷: "+t.Format);
            o[d]=(byte)bl;o[d+1]=(byte)g;o[d+2]=(byte)r;o[d+3]=(byte)a;
        }}return o;
    }
    public static Bitmap Bitmap(byte[] pixels,int w,int h) { var bm=new Bitmap(w,h,PixelFormat.Format32bppArgb); var bits=bm.LockBits(new Rectangle(0,0,w,h),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);try{for(int y=0;y<h;y++)Marshal.Copy(pixels,y*w*4,IntPtr.Add(bits.Scan0,y*bits.Stride),w*4);}finally{bm.UnlockBits(bits);}return bm; }
    public static byte[] Bgra(Bitmap bm) { var r=new byte[bm.Width*bm.Height*4];var bits=bm.LockBits(new Rectangle(0,0,bm.Width,bm.Height),ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);try{for(int y=0;y<bm.Height;y++)Marshal.Copy(IntPtr.Add(bits.Scan0,y*bits.Stride),r,y*bm.Width*4,bm.Width*4);}finally{bm.UnlockBits(bits);}return r; }
    public static Bitmap Load(Texture t,CancellationToken token) {
        Bin.Need(t.Supported,t.Issue);byte[] b;
        if(!t.StandardImage && !t.Source.Lzp){int size=ByteCount(t);Bin.Need(t.DataOffset>=0 && (long)t.DataOffset+size<=t.Source.Length,"소스 범위 초과");b=Bin.Read(t.Source.Path,checked(t.Source.Offset+t.DataOffset),size,token);t=t.Copy();t.DataOffset=0;}
        else b=t.Source.Read(token);
        if(!t.StandardImage)return Bitmap(Decode(b,t,token),t.Width,t.Height);
        using(var ms=new MemoryStream(b))using(var im=Image.FromStream(ms,false,true)){Bin.Need((long)im.Width*im.Height<=16777216,"이미지 픽셀 한도 초과");return ((Bitmap)im).Clone(new Rectangle(0,0,im.Width,im.Height),PixelFormat.Format32bppArgb);}
    }
}
}
