using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
namespace OQNTools
{
    public static class TextKeys
    {
        public static string Name(string text)=> (text??"").Normalize(NormalizationForm.FormC).Replace('\u00a0',' ').Trim().ToUpperInvariant();
        public static List<string> Parse(string text)=>(text??"").Replace('\uff1b',';').Split(new[]{';','\r','\n'},StringSplitOptions.RemoveEmptyEntries).Select(x=>x.Replace('\u00a0',' ').Trim()).Where(x=>x.Length>0).ToList();
    }
}
