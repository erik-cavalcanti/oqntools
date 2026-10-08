using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Microsoft.Win32;

namespace OQNTools
{
    [Transaction(TransactionMode.Manual)] public sealed class PushScheduleParameters : Command
    {
        protected override void Run(Context c)
        {
            if(!(c.View is ViewSchedule schedule))throw new ArgumentException("Abra uma tabela de famílias carregáveis.");
            var needed=schedule.Definition.GetFieldOrder().Select(id=>schedule.Definition.GetField(id)).Select(field=>c.Doc.GetElement(field.ParameterId)).OfType<SharedParameterElement>().GroupBy(p=>p.GuidValue).Select(g=>g.First()).ToList();
            if(needed.Count==0)throw new ArgumentException("A tabela não possui campos de parâmetros compartilhados.");
            var open=new OpenFileDialog{Filter="Parâmetros compartilhados (*.txt)|*.txt",Title="Arquivo com os GUIDs dos parâmetros da tabela"};if(open.ShowDialog()!=true)return;
            string old=c.App.Application.SharedParametersFilename;var definitions=new Dictionary<Guid,ExternalDefinition>();
            try{c.App.Application.SharedParametersFilename=open.FileName;var file=c.App.Application.OpenSharedParameterFile();if(file==null)throw new ArgumentException("Arquivo inválido.");foreach(DefinitionGroup group in file.Groups)foreach(ExternalDefinition d in group.Definitions)definitions[d.GUID]=d;}finally{c.App.Application.SharedParametersFilename=old;}
            var missing=needed.Where(p=>!definitions.ContainsKey(p.GuidValue)).ToList();if(missing.Count>0)throw new ArgumentException("GUIDs não encontrados no arquivo: "+string.Join(", ",missing.Select(p=>p.Name)));
            var families=c.Scope("Vista ativa").OfType<FamilyInstance>().Select(i=>i.Symbol.Family).Where(f=>f.IsEditable&&!f.IsInPlace).GroupBy(f=>f.Id).Select(g=>g.First()).ToList();if(families.Count==0)throw new ArgumentException("Nenhuma família carregável editável encontrada na tabela.");
            var form=new Form(c.App,"Adicionar campos da tabela às famílias","Adiciona as definições compartilhadas às famílias presentes na tabela. Tipos/instâncias seguem a escolha abaixo. Cada família é recarregada separadamente, preservando valores existentes.")
                .List("families","Famílias",families.OrderBy(f=>f.Name).Select(f=>new Choice(f.Name,f)))
                .List("parameters","Parâmetros da tabela",needed.Select(p=>new Choice(p.Name+" · "+p.GuidValue,p.GuidValue)))
                .Check("instance","Adicionar como parâmetros de instância",true);
            if(!form.Run())return;var selected=form.Picks<Family>("families");var parameters=form.Picks<Guid>("parameters");if(selected.Count==0||parameters.Count==0)throw new ArgumentException("Selecione famílias e parâmetros.");
            if(!Form.Preview(c.App,"Adicionar parâmetros às famílias",selected.Select(f=>new{Familia=f.Name,Parametros=parameters.Count}),"As famílias serão editadas e recarregadas uma por vez. Falhas serão relatadas por família."))return;
            var results=new List<object>();foreach(var family in selected)
            {
                Document fd=null;try
                {
                    fd=c.Doc.EditFamily(family);var fm=fd.FamilyManager;var existing=new HashSet<Guid>(fm.Parameters.Cast<FamilyParameter>().Where(p=>p.IsShared).Select(p=>p.GUID));int added=0;
                    using(var t=new Transaction(fd,"OQN · Adicionar parâmetros")){t.Start();foreach(var guid in parameters.Where(g=>!existing.Contains(g))){fm.AddParameter(definitions[guid],GroupTypeId.Data,form.Yes("instance"));added++;}if(t.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Alterações da família não foram confirmadas.");}
                    if(added>0)fd.LoadFamily(c.Doc,new PreserveFamilyValues());
                }catch(Exception ex){results.Add(new{Familia=family.Name,Resultado="ERRO: "+ex.Message});}finally{fd?.Close(false);}
            }
            Form.ShowIssues(c.App,"Famílias · Atenção",results,r=>true);
        }
        sealed class PreserveFamilyValues:IFamilyLoadOptions
        {
            public bool OnFamilyFound(bool familyInUse,out bool overwriteParameterValues){overwriteParameterValues=false;return true;}
            public bool OnSharedFamilyFound(Family sharedFamily,bool familyInUse,out FamilySource source,out bool overwriteParameterValues){source=FamilySource.Family;overwriteParameterValues=false;return true;}
        }
    }
}
