using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace OQNTools
{
    public sealed class XlsxOptions
    { public string Border="thin",HeaderColor="FFCEE5ED";public bool HeaderFill=true,HeaderBold=true,AutoWidth=true,Numbers=true; }
    public sealed class NumberCell { public double Value;public string Format; }
    public sealed class SheetData
    { public string Name; public List<List<string>> Rows=new List<List<string>>(); public int HeaderRows=1;
      public HashSet<int> Headers=new HashSet<int>();public Dictionary<int,int> Titles=new Dictionary<int,int>();
      public Dictionary<string,NumberCell> Numbers=new Dictionary<string,NumberCell>();
      public static string Key(int row,int col)=>row+":"+col;
    }
    public static class Xlsx
    {
        const string Main="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string Rel="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        const string PackageRel="http://schemas.openxmlformats.org/package/2006/relationships";
        const string ContentTypes="http://schemas.openxmlformats.org/package/2006/content-types";
        static void Xml(ZipArchive zip,string path,Action<XmlWriter> write)
        { using(var s=zip.CreateEntry(path,CompressionLevel.Optimal).Open())using(var w=XmlWriter.Create(s,new XmlWriterSettings { Encoding=new UTF8Encoding(false),CloseOutput=false,CheckCharacters=true })) {w.WriteStartDocument();write(w);w.WriteEndDocument();} }
        static void Empty(XmlWriter w,string name,params string[] attrs)
        {string ns=w.LookupPrefix(Main)==""?Main:w.LookupPrefix(PackageRel)==""?PackageRel:ContentTypes;w.WriteStartElement(name,ns);for(int i=0;i<attrs.Length;i+=2)w.WriteAttributeString(attrs[i],attrs[i+1]);w.WriteEndElement();}
        public static string Column(int i)
        {string s="";for(i++;i>0;i=(i-1)/26)s=(char)('A'+(i-1)%26)+s;return s;}
        public static List<string> Names(IEnumerable<string> names)
        {
            var used=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var result=new List<string>();
            foreach(var raw in names){string name=Regex.Replace(raw??"Tabela",@"[\[\]:*?/\\]","_").Trim('\'');if(name.Length==0)name="Tabela";name=name.Substring(0,Math.Min(name.Length,31));string next=name;int i=2;while(!used.Add(next)){string suffix=" ("+i+++ ")";next=name.Substring(0,Math.Min(name.Length,31-suffix.Length))+suffix;}result.Add(next);}return result;
        }
        public static void Write(string path,IList<SheetData> sheets,XlsxOptions options=null)
        {
            options=options??new XlsxOptions();
            var formats=sheets.SelectMany(x=>x.Numbers.Values).Select(x=>x.Format).Distinct().ToList();
            if(sheets.Count==0)throw new ArgumentException("Nenhuma tabela selecionada.");
            if(sheets.Any(s=>s.Rows.Count>1048576||s.Rows.Any(r=>r.Count>16384||r.Any(t=>t!=null&&t.Length>32767))))throw new ArgumentException("A tabela excede um limite do formato XLSX.");
            string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                using(var fs=File.Create(tmp))using(var zip=new ZipArchive(fs,ZipArchiveMode.Create))
                {
                    Xml(zip,"[Content_Types].xml",w=>{w.WriteStartElement("Types","http://schemas.openxmlformats.org/package/2006/content-types");Empty(w,"Default","Extension","rels","ContentType","application/vnd.openxmlformats-package.relationships+xml");Empty(w,"Default","Extension","xml","ContentType","application/xml");Empty(w,"Override","PartName","/xl/workbook.xml","ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");Empty(w,"Override","PartName","/xl/styles.xml","ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");for(int i=0;i<sheets.Count;i++)Empty(w,"Override","PartName","/xl/worksheets/sheet"+(i+1)+".xml","ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");w.WriteEndElement();});
                    Xml(zip,"_rels/.rels",w=>{w.WriteStartElement("Relationships","http://schemas.openxmlformats.org/package/2006/relationships");Empty(w,"Relationship","Id","rId1","Type",Rel+"/officeDocument","Target","xl/workbook.xml");w.WriteEndElement();});
                    var names=Names(sheets.Select(s=>s.Name));
                    Xml(zip,"xl/workbook.xml",w=>{w.WriteStartElement("workbook",Main);w.WriteStartElement("sheets",Main);for(int i=0;i<sheets.Count;i++){w.WriteStartElement("sheet",Main);w.WriteAttributeString("name",names[i]);w.WriteAttributeString("sheetId",(i+1).ToString());w.WriteAttributeString("r","id",Rel,"rId"+(i+1));w.WriteEndElement();}w.WriteEndElement();w.WriteEndElement();});
                    Xml(zip,"xl/_rels/workbook.xml.rels",w=>{w.WriteStartElement("Relationships","http://schemas.openxmlformats.org/package/2006/relationships");for(int i=0;i<sheets.Count;i++)Empty(w,"Relationship","Id","rId"+(i+1),"Type",Rel+"/worksheet","Target","worksheets/sheet"+(i+1)+".xml");Empty(w,"Relationship","Id","styles","Type",Rel+"/styles","Target","styles.xml");w.WriteEndElement();});
                    Xml(zip,"xl/styles.xml",w=>WriteStyles(w,options,formats));
                    for(int index=0;index<sheets.Count;index++){
                        var sheet=sheets[index];Xml(zip,"xl/worksheets/sheet"+(index+1)+".xml",w=>WriteSheet(w,sheet,options,formats));
                    }
                }
                if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);
            }
            finally{if(File.Exists(tmp))File.Delete(tmp);}
        }
        static void WriteStyles(XmlWriter w,XlsxOptions o,List<string> formats)
        {
            w.WriteStartElement("styleSheet",Main);
            if(formats.Count>0){w.WriteStartElement("numFmts",Main);w.WriteAttributeString("count",formats.Count.ToString());for(int i=0;i<formats.Count;i++)Empty(w,"numFmt","numFmtId",(164+i).ToString(),"formatCode",formats[i]);w.WriteEndElement();}
            w.WriteStartElement("fonts",Main);w.WriteAttributeString("count","2");for(int i=0;i<2;i++){w.WriteStartElement("font",Main);Empty(w,"sz","val","11");Empty(w,"name","val","Calibri");if(i==1)Empty(w,"b");w.WriteEndElement();}w.WriteEndElement();
            w.WriteStartElement("fills",Main);w.WriteAttributeString("count","3");foreach(var fill in new[]{"none","gray125","solid"}){w.WriteStartElement("fill",Main);w.WriteStartElement("patternFill",Main);w.WriteAttributeString("patternType",fill);if(fill=="solid"){Empty(w,"fgColor","rgb",o.HeaderColor);Empty(w,"bgColor","indexed","64");}w.WriteEndElement();w.WriteEndElement();}w.WriteEndElement();
            w.WriteStartElement("borders",Main);w.WriteAttributeString("count","2");for(int i=0;i<2;i++){w.WriteStartElement("border",Main);foreach(var side in new[]{"left","right","top","bottom","diagonal"}){w.WriteStartElement(side,Main);if(i==1&&side!="diagonal"&&o.Border!="none"){w.WriteAttributeString("style",o.Border);Empty(w,"color","auto","1");}w.WriteEndElement();}w.WriteEndElement();}w.WriteEndElement();
            w.WriteStartElement("cellStyleXfs",Main);w.WriteAttributeString("count","1");Empty(w,"xf","numFmtId","0","fontId","0","fillId","0","borderId","0");w.WriteEndElement();
            w.WriteStartElement("cellXfs",Main);w.WriteAttributeString("count",(3+formats.Count).ToString());
            for(int i=0;i<3+formats.Count;i++){
                w.WriteStartElement("xf",Main);w.WriteAttributeString("numFmtId",i<3?"49":(164+i-3).ToString());w.WriteAttributeString("fontId",i==2||i==1&&o.HeaderBold?"1":"0");w.WriteAttributeString("fillId",i==1&&o.HeaderFill?"2":"0");w.WriteAttributeString("borderId",i==2?"0":"1");w.WriteAttributeString("xfId","0");w.WriteAttributeString("applyNumberFormat","1");w.WriteAttributeString("applyFont","1");w.WriteAttributeString("applyFill","1");w.WriteAttributeString("applyBorder","1");w.WriteAttributeString("applyAlignment","1");
                Empty(w,"alignment","vertical","center","horizontal",i==2?"center":i>=3?"right":"left","wrapText","1");w.WriteEndElement();
            }w.WriteEndElement();w.WriteEndElement();
        }
        static void WriteSheet(XmlWriter w,SheetData s,XlsxOptions o,List<string> formats)
        {
            w.WriteStartElement("worksheet",Main);int cols=Math.Max(1,s.Rows.Select(r=>r.Count).DefaultIfEmpty(1).Max());
            w.WriteStartElement("cols",Main);for(int col=0;col<cols;col++){
                int length=s.Rows.Select((r,i)=>s.Titles.ContainsKey(i)?0:col<r.Count?(r[col]??"").Length:0).DefaultIfEmpty(8).Max();
                Empty(w,"col","min",(col+1).ToString(),"max",(col+1).ToString(),"width",(o.AutoWidth?Math.Min(60,Math.Max(12,length+2)):20).ToString(),"customWidth","1");
            }w.WriteEndElement();w.WriteStartElement("sheetData",Main);
            for(int r=0;r<s.Rows.Count;r++){
                w.WriteStartElement("row",Main);w.WriteAttributeString("r",(r+1).ToString());
                for(int col=0;col<s.Rows[r].Count;col++){
                    bool title=s.Titles.ContainsKey(r),header=r<s.HeaderRows||s.Headers.Contains(r);NumberCell number;
                    bool numeric=o.Numbers&&!title&&!header&&s.Numbers.TryGetValue(SheetData.Key(r,col),out number);
                    number=numeric?s.Numbers[SheetData.Key(r,col)]:null;
                    w.WriteStartElement("c",Main);w.WriteAttributeString("r",Column(col)+(r+1));w.WriteAttributeString("s",(title?2:header?1:numeric?3+formats.IndexOf(number.Format):0).ToString());
                    if(numeric){w.WriteAttributeString("t","n");w.WriteElementString("v",Main,number.Value.ToString("R",CultureInfo.InvariantCulture));}
                    else{w.WriteAttributeString("t","inlineStr");w.WriteStartElement("is",Main);w.WriteStartElement("t",Main);w.WriteAttributeString("xml","space",null,"preserve");w.WriteString(s.Rows[r][col]??"");w.WriteEndElement();w.WriteEndElement();}w.WriteEndElement();
                }w.WriteEndElement();
            }w.WriteEndElement();
            var merged=s.Titles.Where(x=>x.Value>1).ToList();if(merged.Count>0){w.WriteStartElement("mergeCells",Main);w.WriteAttributeString("count",merged.Count.ToString());foreach(var title in merged)Empty(w,"mergeCell","ref","A"+(title.Key+1)+":"+Column(title.Value-1)+(title.Key+1));w.WriteEndElement();}w.WriteEndElement();
        }
        public static NumberCell ParseNumber(string text,CultureInfo culture)
        {
            var match=Regex.Match(text??"",@"^([^0-9+\-]*)([+\-]?[0-9][0-9.,\s]*)([^0-9]*)$");if(!match.Success)return null;
            string raw=match.Groups[2].Value.Trim(),prefix=match.Groups[1].Value,suffix=match.Groups[2].Value.Substring(match.Groups[2].Value.TrimEnd().Length)+match.Groups[3].Value;
            if(Regex.IsMatch(raw,@"^[+\-]?0[0-9]"))return null; // identifiers with leading zero remain text
            string decimalMark=Regex.Escape(culture.NumberFormat.NumberDecimalSeparator),groupMark=Regex.Escape(culture.NumberFormat.NumberGroupSeparator);
            string integerPattern=groupMark.Length>0?@"(?:[0-9]+|[0-9]{1,3}(?:"+groupMark+@"[0-9]{3})+)":@"[0-9]+";
            if(!Regex.IsMatch(raw,@"^[+\-]?"+integerPattern+"(?:"+decimalMark+@"[0-9]+)?$"))return null;
            double value;if(!double.TryParse(raw,NumberStyles.Number,culture,out value)||double.IsNaN(value)||double.IsInfinity(value))return null;
            string separator=culture.NumberFormat.NumberDecimalSeparator;int pos=raw.LastIndexOf(separator,StringComparison.Ordinal);int digits=pos<0?0:raw.Length-pos-separator.Length;
            if(digits>12)return null;
            string format=(raw.Contains(culture.NumberFormat.NumberGroupSeparator)&&culture.NumberFormat.NumberGroupSeparator.Length>0?"#,##0":"0")+(digits>0?"."+new string('0',digits):"");
            if(suffix.Trim()=="%"){value/=100;format+="%";suffix="";}
            if(prefix.Length>0)format="\""+prefix.Replace("\"","\"\"")+"\""+format;
            if(suffix.Length>0)format+="\""+suffix.Replace("\"","\"\"")+"\"";
            return new NumberCell{Value=value,Format=format};
        }

    }
}
