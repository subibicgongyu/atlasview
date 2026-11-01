using System;
using System.Collections.Generic;
using System.Linq;

namespace AtlasView {
// Bound the native ListView/accessibility tree independently of the total scan size.
internal sealed class TexturePage {
    internal const int Size=200;
    List<Texture> matches=new List<Texture>();
    internal List<Texture> Items=new List<Texture>();
    internal int Number {get;private set;}
    internal int Count {get{return Math.Max(1,(matches.Count+Size-1)/Size);}}
    internal void Reset(List<Texture> values){matches=values;Move(0);}
    internal void Move(int page){Number=Math.Max(0,Math.Min(Count-1,page));Items=matches.Skip(Number*Size).Take(Size).ToList();}
}
}
