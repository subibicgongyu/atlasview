using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;

namespace AtlasView {
internal sealed class HiddenPreviewResolver {
    readonly Dictionary<string,ResourceGroup> known;
    readonly Dictionary<string,ResourceGroup> resolved=new Dictionary<string,ResourceGroup>();
    readonly Queue<string> order=new Queue<string>();
    internal HiddenPreviewResolver(IEnumerable<Texture> textures){known=ResourceGroup.Group(textures??Enumerable.Empty<Texture>()).ToDictionary(g=>g.Id);}
    internal ResourceGroup Resolve(HiddenImageEntry entry,CancellationToken token){
        token.ThrowIfCancellationRequested();string key=ResourceGroup.Key(new Source{Path=entry.SourcePath,Offset=entry.SourceOffset});ResourceGroup group;
        if(known.TryGetValue(key,out group))return SelectEntry(entry,group);
        var file=new FileInfo(entry.SourcePath);long remaining=file.Length-entry.SourceOffset;
        Bin.Need(entry.SourceOffset>=0 && remaining>0,"숨김 리소스 위치가 파일 범위를 벗어났습니다.");
        string revision=key+"|"+file.Length+"|"+file.LastWriteTimeUtc.Ticks;
        if(resolved.TryGetValue(revision,out group))return SelectEntry(entry,group);
        if(entry.PreviewTexture!=null){Bin.Need(ResourceGroup.Key(entry.PreviewTexture.Source)==key,"숨김 미리보기 위치가 일치하지 않습니다.");group=new ResourceGroup{Textures=new List<Texture>{entry.PreviewTexture}};}
        else{
            byte[] head=Bin.Read(entry.SourcePath,entry.SourceOffset,(int)Math.Min(128,remaining),token);
            var source=new Source{Path=entry.SourcePath,Offset=entry.SourceOffset,Label=Path.GetFileName(entry.SourcePath)+" @0x"+entry.SourceOffset.ToString("X")};
            bool standard=Bin.Magic(head,0,"\u0089PNG\r\n\u001a\n")||Bin.Magic(head,0,"BM")||Bin.Magic(head,0,"GIF8")||(head.Length>=3&&head[0]==255&&head[1]==216&&head[2]==255);
            if(standard){Bin.Need(remaining<=Bin.MaxBytes,"이미지 파일 128 MiB 한도 초과");source.Length=(int)remaining;using(var stream=new MemoryStream(source.Read(token)))using(var im=Image.FromStream(stream,false,false)){
                Bin.Need(im.Width<=8192 && im.Height<=8192 && (long)im.Width*im.Height<=16777216,"이미지 크기 한도 초과");group=new ResourceGroup{Textures=new List<Texture>{new Texture{Source=source,Name="image",Container="Image",Format=Path.GetExtension(entry.SourcePath).TrimStart('.').ToUpperInvariant(),StandardImage=true,Width=im.Width,Height=im.Height}}};
            }}else{
                long length;
                if(Bin.Magic(head,0,"LZP2")){source.Lzp=true;length=16L+Bin.U32(head,12,false);}
                else if(Bin.Magic(head,0,"G1TG"))length=Bin.U32(head,8);
                else if(Bin.Magic(head,0,"DDS ")){int w=Bin.I32(head,16,false),h=Bin.I32(head,12,false);Bin.Need(w>0&&h>0&&w<=8192&&h<=8192,"DDS 크기 오류");length=Math.Min(remaining,128L+(long)w*h*4+4096);}
                else if(new GtfReader().Matches(head)){int count=Bin.I32(head,8);Bin.Need(count>0&&count<=4096,"GTF 항목 수 오류");var table=Bin.Read(entry.SourcePath,entry.SourceOffset,12+count*36,token);length=12+count*36;for(int i=0;i<count;i++)length=Math.Max(length,(long)Bin.U32(table,16+i*36)+Bin.U32(table,20+i*36));}
                else throw new InvalidDataException("저장 위치에서 지원 이미지 헤더를 찾지 못했습니다. 원본 변경 또는 과거 RAW 설정을 확인하세요.");
                Bin.Need(length>=16 && length<=Bin.MaxBytes && length<=remaining,"숨김 리소스 길이 오류");source.Length=(int)length;group=new ResourceGroup{Textures=Formats.Parse(source,source.Read(token))};
            }
        }
        token.ThrowIfCancellationRequested();if(resolved.Count>=128)resolved.Remove(order.Dequeue());resolved.Add(revision,group);order.Enqueue(revision);return SelectEntry(entry,group);
    }
    static ResourceGroup SelectEntry(HiddenImageEntry entry,ResourceGroup group){
        if(entry.Key==group.Id)return group;
        var texture=group.Textures.FirstOrDefault(t=>HiddenImageStore.Key(t)==entry.Key);
        Bin.Need(texture!=null,"이전 개별 이미지 기록과 일치하는 이미지를 찾지 못했습니다.");return new ResourceGroup{Textures=new List<Texture>{texture}};
    }
}
}
