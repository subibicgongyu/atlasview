using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace AtlasView {
internal sealed class ViewerSettings {
    public string LastFolder;
    internal static string ReadFolder(string path){
        if(!File.Exists(path))return "";
        Bin.Need(new FileInfo(path).Length<=65536,"뷰어 설정 파일 크기 초과");
        var value=new JavaScriptSerializer().Deserialize<ViewerSettings>(File.ReadAllText(path,Encoding.UTF8));
        return value==null?"":value.LastFolder??"";
    }
    internal static void SaveFolder(string path,string folder){
        string directory=Path.GetDirectoryName(path);Directory.CreateDirectory(directory);
        Bin.Need((File.GetAttributes(directory)&FileAttributes.ReparsePoint)==0,"뷰어 설정 폴더 링크는 사용할 수 없습니다.");
        if(File.Exists(path))Bin.Need((File.GetAttributes(path)&FileAttributes.ReparsePoint)==0,"뷰어 설정 파일 링크는 사용할 수 없습니다.");
        string temp=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
        try{
            File.WriteAllText(temp,new JavaScriptSerializer().Serialize(new ViewerSettings{LastFolder=Path.GetFullPath(folder)}),Encoding.UTF8);
            if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
}
