using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace OQNTools
{
    // Pure logic, also compiled by the tests without Revit references.
    public static class Logic
    {
        public static double Number(string text)
        {
            double d;
            if(!double.TryParse(text.Trim().Replace(',','.'),NumberStyles.Float,CultureInfo.InvariantCulture,out d)||double.IsNaN(d)||double.IsInfinity(d))
                throw new ArgumentException("Número inválido: "+text+". Use ponto ou vírgula decimal, sem separador de milhares.");
            return d;
        }
        public static string Composite(IEnumerable<string> values) => string.Concat(values.Select(v=>(v??"").Length+":"+(v??"")));
        public static string Direction(double dx,double dy,double dz,double tolerance)
        {
            if(tolerance<0||tolerance>=45)throw new ArgumentException("A tolerância deve ser de 0 até menos de 45 graus.");
            double horizontal=Math.Sqrt(dx*dx+dy*dy);
            if(horizontal==0&&dz==0)throw new ArgumentException("Comprimento nulo.");
            double angle=Math.Atan2(Math.Abs(dz),horizontal)*180/Math.PI;
            return angle<=tolerance+1e-9?"Horizontal":angle>=90-tolerance-1e-9?"Vertical":"Inclinado";
        }
        public static bool InBand(double a,double b,double min,double max,bool intersect)
        { if(min>max)throw new ArgumentException("A altura mínima não pode exceder a máxima."); return intersect?Math.Max(a,b)>=min-1e-9&&Math.Min(a,b)<=max+1e-9:Math.Min(a,b)>=min-1e-9&&Math.Max(a,b)<=max+1e-9; }
        public static bool Match(string actual,string op,string expected)
        {
            if(op=="vazio")return string.IsNullOrEmpty(actual);
            if(actual==null)return false;
            switch(op)
            {
                case "=":return string.Equals(actual,expected,StringComparison.Ordinal);
                case "!=":return !string.Equals(actual,expected,StringComparison.Ordinal);
                case "contém":return actual.IndexOf(expected,StringComparison.OrdinalIgnoreCase)>=0;
                case "não contém":return actual.IndexOf(expected,StringComparison.OrdinalIgnoreCase)<0;
                case "termina":return actual.EndsWith(expected,StringComparison.OrdinalIgnoreCase);
                case "começa":return actual.StartsWith(expected,StringComparison.OrdinalIgnoreCase);
                case ">":return Number(actual)>Number(expected);
                case ">=":return Number(actual)>=Number(expected);
                case "<":return Number(actual)<Number(expected);
                case "<=":return Number(actual)<=Number(expected);
                default:throw new ArgumentException("Operador desconhecido: "+op);
            }
        }
    }
    public sealed class NaturalComparer : IComparer<string>
    {
        public int Compare(string x,string y)
        {
            var a=Regex.Split(x??"","([0-9]+)");var b=Regex.Split(y??"","([0-9]+)");
            for(int i=0;i<Math.Min(a.Length,b.Length);i++)
            {
                int c;
                if(a[i].Length>0&&b[i].Length>0&&char.IsDigit(a[i][0])&&char.IsDigit(b[i][0]))
                { var aa=a[i].TrimStart('0');var bb=b[i].TrimStart('0');c=aa.Length.CompareTo(bb.Length);if(c==0)c=StringComparer.Ordinal.Compare(aa,bb); }
                else c=StringComparer.OrdinalIgnoreCase.Compare(a[i],b[i]);
                if(c!=0)return c;
            }
            int n=a.Length.CompareTo(b.Length);return n!=0?n:StringComparer.Ordinal.Compare(x,y);
        }
    }
}
