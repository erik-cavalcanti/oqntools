using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.DB;
namespace OQNTools
{
    // Used by composed descriptions and the filter only. Single-field numbering
    // retains its existing reader and all numbering/writing rules remain unchanged.
    public static class NamedValues
    {
        static IList<Parameter> Matches(Element owner,string name,bool visibleFallback)
        {
            if(owner==null||!owner.IsValidObject)return new List<Parameter>();
            if(name.StartsWith("@")&&Guid.TryParse(name.Substring(1),out var guid)){
                var p=owner.get_Parameter(guid);return p==null?new List<Parameter>():new List<Parameter>{p};
            }
            var exact=owner.GetParameters(name);
            if(exact.Count>0||!visibleFallback)return exact;
            // Only visible UI parameters, and only for this requested name.
            // No IsShared/GUID or data-type inspection during name matching.
            return owner.GetOrderedParameters().Where(p=>TextKeys.Name(p.Definition.Name)==TextKeys.Name(name)).ToList();
        }
        public static string Text(Parameter p)
        {
            if(p==null||!p.HasValue)return null;
            switch(p.StorageType){
                case StorageType.String:return p.AsString();
                case StorageType.Integer:return p.AsValueString()??p.AsInteger().ToString();
                case StorageType.Double:return p.AsValueString()??p.AsDouble().ToString("R",System.Globalization.CultureInfo.InvariantCulture);
                case StorageType.ElementId:
                    var id=p.AsElementId();var element=id.OqnValue()>0?p.Element.Document.GetElement(id):null;
                    return element!=null?element.Name:id.OqnValue().ToString();
                default:return null;
            }
        }
        static Parameter Choose(IList<Parameter> parameters,string name)
        {
            var populated=parameters.Where(p=>!string.IsNullOrWhiteSpace(Text(p))).ToList();
            if(populated.Select(p=>Text(p)).Distinct(StringComparer.Ordinal).Count()>1)
                throw new ArgumentException("Parâmetros homônimos com valores diferentes: "+name+". Use @GUID para distinguir.");
            return populated.FirstOrDefault()??parameters.FirstOrDefault();
        }
        public static Parameter Find(Element e,string name,string scope="auto",bool visibleFallback=true)
        {
            if(e==null||!e.IsValidObject)return null;
            var type=e is ElementType?e:e.Document.GetElement(e.GetTypeId());
            if(scope=="type")return Choose(Matches(type,name,visibleFallback),name);
            var instance=Choose(Matches(e,name,visibleFallback),name);
            if(scope=="instance")return instance;
            return CompositeDescriptions.PreferInstance(instance,Text,()=>Choose(Matches(type,name,visibleFallback),name));
        }
        public static List<string> Components(Element e,IEnumerable<string> names,out List<string> missing)
        {
            return CompositeDescriptions.Read(names,name=>Text(Find(e,name)),out missing);
        }
    }
}
