using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;

namespace AtlasView {
public sealed class ResourceGroup {
    public List<Texture> Textures=new List<Texture>();
    public Texture First {get{return Textures[0];}}
    public string Id {get{return Key(First.Source);}}
    public string Label {get{return Path.GetFileName(First.Source.Path)+" @0x"+First.Source.Offset.ToString("X");}}
    public bool Supported {get{return Textures.Any(t=>t.Supported);}}
    public static string Key(Source s){return Bin.TextHash("resource-v1|"+Path.GetFullPath(s.Path).ToUpperInvariant()+"|"+s.Offset);}
    public static List<ResourceGroup> Group(IEnumerable<Texture> textures){return textures.GroupBy(t=>Key(t.Source)).Select(g=>new ResourceGroup{Textures=g.OrderBy(t=>t.DataOffset).ThenBy(t=>t.Name,StringComparer.Ordinal).ToList()}).ToList();}
    internal bool Matches(string query){return query.Length==0 || (First.Source.Path+" "+Label+" "+string.Join(" ",Textures.Select(t=>t.Locator+" "+t.Format+" "+t.Container))).IndexOf(query,StringComparison.OrdinalIgnoreCase)>=0;}
}
internal sealed class ResourcePage {
    internal const int Size=200;List<ResourceGroup> all=new List<ResourceGroup>();
    internal List<ResourceGroup> Items=new List<ResourceGroup>();internal int Number {get;private set;}
    internal int Count {get{return Math.Max(1,(all.Count+Size-1)/Size);}}
    internal void Reset(List<ResourceGroup> values){all=values;Move(0);}
    internal void Move(int page){Number=Math.Max(0,Math.Min(Count-1,page));Items=all.Skip(Number*Size).Take(Size).ToList();}
}
internal sealed class ResourcePreview : IDisposable {
    internal Bitmap Image;internal List<Rectangle> Cells=new List<Rectangle>();internal List<string> Errors=new List<string>();internal string SourceStamp;
    internal static ResourcePreview Load(ResourceGroup group,PreviewLoader loader,CancellationToken token,Action<string> progress){
        var result=new ResourcePreview();try{
            string stamp=PreviewLoader.SourceKey(group.First.Source);
            int count=group.Textures.Count,columns=Math.Min(8,(int)Math.Ceiling(Math.Sqrt(count))),rows=(count+columns-1)/columns;
            int cell=Math.Min(256,Math.Min(8192/columns,8192/rows));Bin.Need(cell>=16,"리소스 이미지 수가 미리보기 한도를 초과했습니다.");
            if(count>1)result.Image=new Bitmap(columns*cell,rows*cell,PixelFormat.Format32bppArgb);
            for(int i=0;i<count;i++){token.ThrowIfCancellationRequested();Texture t=group.Textures[i];Rectangle rect=new Rectangle(i%columns*cell,i/columns*cell,cell,cell);result.Cells.Add(rect);string error="";
                try{using(var bitmap=loader.Load(t,token,s=>progress((i+1)+" / "+count+" · "+t.Name+" · "+s))){
                    if(count==1)result.Image=bitmap.Clone(new Rectangle(0,0,bitmap.Width,bitmap.Height),PixelFormat.Format32bppArgb);
                    else using(var g=Graphics.FromImage(result.Image)){g.ClearCell(rect);int caption=Math.Min(28,cell/4);float scale=Math.Min((cell-8f)/bitmap.Width,(cell-caption-8f)/bitmap.Height);g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;g.DrawImage(bitmap,new RectangleF(rect.X+4,rect.Y+caption,bitmap.Width*scale,bitmap.Height*scale));}
                }}catch(OperationCanceledException){throw;}catch(Exception e){error=e.Message;result.Errors.Add(t.Name+": "+error);}
                if(count>1)using(var g=Graphics.FromImage(result.Image))using(var font=new Font("Malgun Gothic",Math.Max(6,Math.Min(10,cell/20f))))using(var brush=new SolidBrush(error.Length>0?Color.OrangeRed:Color.White)){g.DrawString(t.Name+" · "+t.Width+"×"+t.Height,font,brush,new RectangleF(rect.X+3,rect.Y+2,cell-6,Math.Min(28,cell/4)));if(error.Length>0)g.DrawString("미지원 / 읽기 오류",font,brush,new RectangleF(rect.X+4,rect.Y+cell/2,cell-8,cell/2));}
            }
            if(stamp==PreviewLoader.SourceKey(group.First.Source))result.SourceStamp=stamp;
            return result;
        }catch{result.Dispose();throw;}
    }
    public void Dispose(){if(Image!=null){Image.Dispose();Image=null;}}
}
internal static class ResourceDrawing {
    internal static void ClearCell(this Graphics g,Rectangle r){using(var fill=new SolidBrush(Color.FromArgb(34,39,48)))g.FillRectangle(fill,r);using(var pen=new Pen(Color.FromArgb(90,100,110)))g.DrawRectangle(pen,r.X,r.Y,r.Width-1,r.Height-1);}
}
}
