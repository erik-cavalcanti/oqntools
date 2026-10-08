using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
namespace OQNTools
{
    public sealed class ParameterOption
    {
        public string Name,Identity,DataType,Scope; public StorageType Storage;
        public string Label=>(Scope=="auto"?"Automático (instância/tipo)":Scope=="type"?"Tipo":"Instância")+" — "+Name;
        public string Key=>Scope+"|"+Identity+"|"+DataType;
        public override string ToString()=>Label;
        public Parameter Get(Element e,bool writing=false)
        {
            if(e==null||!e.IsValidObject)return null;
            var type=e is ElementType?e:e.Document.GetElement(e.GetTypeId());
            if(Scope=="type")return On(type);
            var p=On(e);if(Scope=="instance")return p;
            return writing?(p??On(type)):(p!=null&&p.HasValue?p:On(type)??p);
        }
        Parameter On(Element e)
        {
            if(e==null||!e.IsValidObject)return null;
            // Targeted access: never enumerate hidden/native parameters to find one value.
            if(Identity.StartsWith("builtin:",StringComparison.Ordinal))return e.get_Parameter((BuiltInParameter)long.Parse(Identity.Substring(8)));
            if(Identity.StartsWith("guid:",StringComparison.Ordinal))return e.get_Parameter(Guid.Parse(Identity.Substring(5)));
            var matches=e.GetParameters(Name).Where(p=>TypeId(p)==DataType).ToList();
            if(matches.Count>1)throw new ArgumentException("Há parâmetros homônimos ambíguos: "+Name+".");return matches.FirstOrDefault();
        }
        static string TypeId(Parameter p){try{return p.Definition.GetDataType().TypeId;}catch(Autodesk.Revit.Exceptions.ArgumentException){return "storage:"+p.StorageType;}}
        static string Id(Parameter p)
        {
            var id=p.Id;
            if(id.OqnValue()<0)return "builtin:"+id.OqnValue();
            var shared=p.Element.Document.GetElement(id) as SharedParameterElement;
            return shared!=null?"guid:"+shared.GuidValue:"name:"+p.Definition.Name;
        }
        public static List<ParameterOption> Discover(IEnumerable<Element> elements,bool writableText=false,bool automatic=true)
        {
            var found=new Dictionary<string,ParameterOption>();var seen=new HashSet<long>();
            foreach(var e in elements.Where(e=>e!=null&&e.IsValidObject))foreach(var scope in new[]{"instance","type"}){
                var owner=scope=="instance"?e:e.Document.GetElement(e.GetTypeId());if(owner==null||!owner.IsValidObject)continue;
                if(scope=="type"&&!seen.Add(owner.Id.OqnValue()))continue;
                Errors.Checkpoint("Listar parâmetros visíveis: elemento "+owner.Id.OqnValue());
                foreach(Parameter p in owner.GetOrderedParameters()){try{if(p.StorageType==StorageType.None||writableText&&(p.IsReadOnly||p.StorageType!=StorageType.String))continue;
                    var o=new ParameterOption{Name=p.Definition.Name,Identity=Id(p),DataType=TypeId(p),Scope=scope,Storage=p.StorageType};found[o.Key]=o;
                }catch(Autodesk.Revit.Exceptions.InvalidOperationException ex){System.Diagnostics.Trace.WriteLine(ex);}catch(Autodesk.Revit.Exceptions.ArgumentException ex){System.Diagnostics.Trace.WriteLine(ex);}}
            }
            if(automatic)foreach(var o in found.Values.ToList()){
                var a=new ParameterOption{Name=o.Name,Identity=o.Identity,DataType=o.DataType,Scope="auto",Storage=o.Storage};found[a.Key]=a;
            }
            return found.Values.OrderBy(x=>x.Name).ThenBy(x=>x.Scope).ToList();
        }
    }
    public static class Batch
    {
        public static void Run(Context c,string title,IEnumerable<Change> rows,bool collectWarnings=false)
        {
            using(var group=new TransactionGroup(c.Doc,"OQN · "+title)){
                group.Start();foreach(var row in rows.Where(r=>r.Apply!=null)){
                    try{Errors.Checkpoint(title+": aplicar elemento "+row.Id);var warnings=collectWarnings?new List<string>():null;c.Mutate(title,row.Apply,warnings);row.Estado="Concluído"+(warnings!=null&&warnings.Count>0?" · Avisos: "+string.Join("; ",warnings.Distinct()):"");}
                    catch(Autodesk.Revit.Exceptions.RegenerationFailedException){throw;}
                    catch(Exception ex){row.Estado=Errors.Describe(ex);}
                }group.Assimilate();
            }
        }
    }
    public static class Errors
    {
        // Written before native calls: a process termination cannot be caught reliably.
        // Contains no parameter values or model paths. Best effort; never blocks a command.
        static string command="";
        public static void Begin(string name){command=name;Checkpoint("Início");}
        public static void Checkpoint(string stage)
        {
            try{
                var dir=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OQN Tools");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(System.IO.Path.Combine(dir,"ultima-operacao.log"),DateTime.Now.ToString("o")+" | "+command+" | "+stage+Environment.NewLine);
            }catch(System.IO.IOException){}catch(UnauthorizedAccessException){}
        }
        public static string Describe(Exception e)
        {
            System.Diagnostics.Trace.WriteLine(e.ToString());
            try {
                var dir=System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OQN Tools");
                System.IO.Directory.CreateDirectory(dir);
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir,"erros.log"),DateTime.Now.ToString("s")+"\n"+e+"\n\n");
            }catch(System.IO.IOException){}catch(UnauthorizedAccessException){}
            if(e is System.Reflection.TargetInvocationException&&e.InnerException!=null)return Describe(e.InnerException);
            if(e is System.Xml.XmlException)return "Conjunto XML inválido ou incompleto. Confira o arquivo escolhido.";
            if(e.Message.StartsWith("Sequence ",StringComparison.Ordinal)||e.Message.IndexOf("same key",StringComparison.OrdinalIgnoreCase)>=0)return "Não foi possível identificar um item único para esta operação. O item foi preservado.";
            if(e is System.IO.IOException)return "Não foi possível gravar: confira a pasta e se o arquivo está aberto.";
            if(e is UnauthorizedAccessException)return "Sem permissão para acessar o arquivo ou pasta.";
            if(e is ArgumentException||e is InvalidOperationException)return e.Message;
            return e.GetType().Name+": "+e.Message;
        }
    }
}
