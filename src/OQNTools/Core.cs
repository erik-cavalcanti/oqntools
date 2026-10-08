using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace OQNTools
{
    public abstract class Command : IExternalCommand
    {
        public Result Execute(ExternalCommandData data,ref string message,ElementSet elements)
        {
            try { Errors.Begin(GetType().Name);if(data.Application.ActiveUIDocument==null)throw new ArgumentException("Abra um documento Revit.");Run(new Context(data.Application));Errors.Checkpoint("Concluído ou janela cancelada");return Result.Succeeded; }
            catch(Cancelled){Errors.Checkpoint("Cancelado");return Result.Cancelled;}
            catch(Autodesk.Revit.Exceptions.OperationCanceledException){Errors.Checkpoint("Cancelado");return Result.Cancelled;}
            catch(Exception ex){message=Errors.Describe(ex);Errors.Checkpoint("Erro gerenciado: "+ex.GetType().Name);TaskDialog.Show("OQN Tools",message);return Result.Failed;}
        }
        protected abstract void Run(Context c);
    }
    public sealed class Context
    {
        public UIApplication App; public UIDocument Ui;public Document Doc;public View View=>Doc.ActiveView;
        public Context(UIApplication app){App=app;Ui=app.ActiveUIDocument;Doc=Ui.Document;}
        public IEnumerable<Element> Scope(string scope)
        {
            Errors.Checkpoint("Coletar elementos: "+scope);
            if(scope=="Seleção")return Ui.Selection.GetElementIds().Select(Doc.GetElement).Where(e=>e!=null&&!(e is ElementType)).ToList();
            if(scope=="Vista ativa")
            {
                if(!FilteredElementCollector.IsViewValidForElementIteration(Doc,View.Id))throw new ArgumentException("Esta vista não permite coletar elementos.");
                return new FilteredElementCollector(Doc,View.Id).WhereElementIsNotElementType().ToElements();
            }
            return new FilteredElementCollector(Doc).WhereElementIsNotElementType().ToElements();
        }
        public Form ScopeForm(string title,string hint) => new Form(App,title,hint).Select("scope","Aplicar em",new[]{"Seleção","Vista ativa","Modelo"}.Select(x=>new Choice(x,x)));
        public List<T> All<T>() where T:Element => new FilteredElementCollector(Doc).OfClass(typeof(T)).Cast<T>().ToList();
        public void Mutate(string name,Action action,List<string> warnings=null)
        {
            if(Doc.IsReadOnly)throw new InvalidOperationException("O documento está somente para leitura.");
            using(var t=new Transaction(Doc,"OQN · "+name))
            {
                t.Start();
                var options=t.GetFailureHandlingOptions();options.SetFailuresPreprocessor(new RollbackErrors(warnings));options.SetClearAfterRollback(true);t.SetFailureHandlingOptions(options);
                action();if(t.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("A operação foi revertida pelo Revit. Nenhuma alteração desta transação foi mantida.");
            }
        }
        public void Done(string message)=>TaskDialog.Show("OQN Tools",message);
        public void NeedSelection(IEnumerable<Element> list){if(!list.Any())throw new ArgumentException("Nenhum elemento no escopo escolhido. Faça uma seleção ou escolha outro escopo.");}
    }
    sealed class RollbackErrors : IFailuresPreprocessor
    {
        readonly List<string> warnings;
        public RollbackErrors(List<string> warnings=null){this.warnings=warnings;}
        public FailureProcessingResult PreprocessFailures(FailuresAccessor a)
        {
            var messages=a.GetFailureMessages();
            if(messages.Any(f=>f.GetSeverity()==FailureSeverity.Error))
            {
                if(warnings!=null)warnings.AddRange(messages.Select(f=>f.GetDescriptionText()));
                return FailureProcessingResult.ProceedWithRollBack;
            }
            // Only a caller explicitly collecting warnings may suppress their modal UI.
            if(warnings!=null)foreach(var failure in messages.Where(f=>f.GetSeverity()==FailureSeverity.Warning))
            {warnings.Add(failure.GetDescriptionText());a.DeleteWarning(failure);}
            return FailureProcessingResult.Continue;
        }
    }

    public static class Params
    {
        // I: forces instance; T: forces type; otherwise instance with value then type.
        // @GUID disambiguates shared parameters. Duplicate same-name definitions are rejected.
        static Parameter On(Element e,string name)
        {
            if(e==null)return null;
            if(name.StartsWith("@")&&Guid.TryParse(name.Substring(1),out Guid guid))return e.get_Parameter(guid);
            var ps=e.GetParameters(name);if(ps.Count>1)throw new ArgumentException("Parâmetros homônimos: "+name+". Informe @GUID do parâmetro compartilhado.");
            return ps.FirstOrDefault();
        }
        public static Parameter Find(Element e,string spec,bool writing=false)
        {
            string name=spec.Trim();string scope="auto";
            if(name.StartsWith("I:",StringComparison.OrdinalIgnoreCase)||name.StartsWith("T:",StringComparison.OrdinalIgnoreCase)){scope=name.Substring(0,1).ToUpperInvariant();name=name.Substring(2).Trim();}
            var type=e is ElementType?e:e.Document.GetElement(e.GetTypeId());
            if(scope=="T")return On(type,name);
            var pi=On(e,name);if(scope=="I")return pi;
            if(writing)return pi!=null&&!pi.IsReadOnly?pi:On(type,name)??pi;
            if(pi!=null&&pi.HasValue&&(pi.StorageType!=StorageType.String||!string.IsNullOrWhiteSpace(pi.AsString())))return pi;
            return On(type,name)??pi;
        }
        public static string Text(Parameter p)
        {
            if(p==null||!p.HasValue)return null;
            return p.StorageType==StorageType.String?p.AsString():p.AsValueString()??(p.StorageType==StorageType.Integer?p.AsInteger().ToString():p.StorageType==StorageType.ElementId?p.AsElementId().OqnValue().ToString():p.AsDouble().ToString("R",System.Globalization.CultureInfo.InvariantCulture));
        }
        public static string Read(Element e,string name)=>Text(Find(e,name));
        public static string Identity(Parameter p)=>p.Element.UniqueId+"/"+p.Id.OqnValue();
        public static string Fingerprint(Parameter p)
        {
            if(p==null||!p.HasValue)return "<sem valor>";
            string raw=p.StorageType==StorageType.Double?p.AsDouble().ToString("R",System.Globalization.CultureInfo.InvariantCulture):p.StorageType==StorageType.Integer?p.AsInteger().ToString():p.StorageType==StorageType.ElementId?p.AsElementId().OqnValue().ToString():p.AsString();
            return p.StorageType+"/"+p.Definition.GetDataType().TypeId+"/"+raw;
        }
        public static void CheckWritable(Parameter p)
        { if(p==null)throw new ArgumentException("Parâmetro destino não encontrado.");if(p.IsReadOnly)throw new ArgumentException("Parâmetro destino é somente leitura."); }
        public static void SetText(Parameter p,string value){if(!p.Set(value)&&(p.AsString()??"")!=value)throw new InvalidOperationException("Revit recusou o texto.");}
        public static void SetDouble(Parameter p,double value){if(!p.Set(value)&&p.AsDouble()!=value)throw new InvalidOperationException("Revit recusou o número.");}
        public static void SetInt(Parameter p,int value){if(!p.Set(value)&&p.AsInteger()!=value)throw new InvalidOperationException("Revit recusou o inteiro.");}
        public static void SetId(Parameter p,ElementId value){if(!p.Set(value)&&p.AsElementId()!=value)throw new InvalidOperationException("Revit recusou a referência.");}
        public static Action Copy(Parameter from,Parameter to)
        {
            CheckWritable(to);if(from==null||!from.HasValue)throw new ArgumentException("Origem sem valor.");
            if(to.StorageType==StorageType.String){string s=Text(from)??"";return ()=>SetText(to,s);}
            if(from.StorageType!=to.StorageType||from.Definition.GetDataType()!=to.Definition.GetDataType())throw new ArgumentException("Tipos de dados/unidades incompatíveis. Para copiar o texto exibido, use destino textual.");
            switch(from.StorageType)
            {
                case StorageType.Double:double d=from.AsDouble();return ()=>SetDouble(to,d);
                case StorageType.Integer:int i=from.AsInteger();return ()=>SetInt(to,i);
                case StorageType.ElementId:var id=from.AsElementId();return ()=>SetId(to,id);
                default:throw new ArgumentException("Tipo de dado não suportado.");
            }
        }
    }
    public sealed class Change
    {
        public long Id {get;set;} public string Elemento {get;set;} public string Antes {get;set;} public string Depois {get;set;} public string Estado {get;set;}
        internal Action Apply;internal string Key;internal string Signature;
    }
    public static class Changes
    {
        public static void Apply(Context c,string title,List<Change> changes)
        {
            if(changes.Count==0){c.Done("Nenhuma alteração necessária.");return;}
            if(!Form.Preview(c.App,title,changes,"Confira os valores. Linhas com erro serão ignoradas. Alterar um parâmetro de tipo afeta TODAS as instâncias desse tipo, inclusive fora do escopo."))return;
            var valid=changes.Where(x=>x.Apply!=null).ToList();if(valid.Count==0){c.Done("Não há alterações válidas.");return;}
            c.Mutate(title,()=>{foreach(var row in valid)row.Apply();});Form.ShowIssues(c.App,title+" · Atenção",changes,r=>r.Apply==null&&r.Estado!="Mesmo destino já incluído");
        }
        public static void RejectConflicts(List<Change> changes)
        {
            foreach(var g in changes.Where(x=>x.Apply!=null&&x.Key!=null).GroupBy(x=>x.Key))
            {
                if(g.Select(x=>x.Signature??x.Depois).Distinct(StringComparer.Ordinal).Count()>1)foreach(var row in g){row.Estado="ERRO: valores divergentes para o mesmo destino de tipo";row.Apply=null;}
                else foreach(var row in g.Skip(1)){row.Estado="Mesmo destino já incluído";row.Apply=null;}
            }
        }
    }
}
