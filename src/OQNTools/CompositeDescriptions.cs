using System;
using System.Collections.Generic;
using System.Linq;
namespace OQNTools
{
    public static class CompositeDescriptions
    {
        public static List<string> Read(IEnumerable<string> names,Func<string,string> read,out List<string> missing)
        {
            var ordered=names.ToList();missing=new List<string>();var values=new List<string>();
            foreach(var name in ordered){var value=read(name);if(string.IsNullOrWhiteSpace(value)){missing.Add(name);value="";}values.Add(value.Trim());}
            if(values.All(string.IsNullOrWhiteSpace))throw new ArgumentException("Nenhum valor nos parâmetros: "+string.Join("; ",ordered));
            return values;
        }
        public static T PreferInstance<T>(T instance,Func<T,string> text,Func<T> type) where T:class =>!string.IsNullOrWhiteSpace(text(instance))?instance:type()??instance;
    }
}
