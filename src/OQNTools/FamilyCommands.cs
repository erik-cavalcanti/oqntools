using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Microsoft.Win32;

namespace OQNTools
{
    [Transaction(TransactionMode.Manual)] public sealed class ManageFamilies : Command
    {
        protected override void Run(Context c)
        {
            if(c.Doc.IsFamilyDocument)throw new ArgumentException("Abra um projeto para gerenciar as famílias carregadas.");
            var f=new Form(c.App,"Gerenciar famílias","Importa RFA ou exporta famílias editáveis organizadas por categoria. Arquivos existentes na exportação são ignorados.")
                .Select("mode","Operação",new[]{new Choice("Exportar famílias","export"),new Choice("Carregar arquivos RFA","import")})
                .List("families","Famílias para exportar",c.All<Family>().Where(x=>x.IsEditable&&!x.IsInPlace).OrderBy(x=>x.Name).Select(x=>new Choice((x.FamilyCategory?.Name??"Sem categoria")+" · "+x.Name,x)));
            if(!f.Run())return;
            if(f.Pick<string>("mode")=="import")
            {
                var open=new OpenFileDialog{Filter="Famílias (*.rfa)|*.rfa",Multiselect=true};if(open.ShowDialog()!=true)return;
                if(!Form.Preview(c.App,"Carregar famílias",open.FileNames.Select(p=>new{Arquivo=p}),"Famílias já existentes serão preservadas; o Revit informa quando não carrega um arquivo."))return;
                var results=new List<object>();c.Mutate("Carregar famílias",()=>{foreach(var path in open.FileNames){using(var st=new SubTransaction(c.Doc)){st.Start();try{bool ok=c.Doc.LoadFamily(path,out Family family);if(ok)st.Commit();else st.RollBack();if(!ok)results.Add(new{Arquivo=path,Resultado="Não carregada / já existente"});}catch(Exception ex){st.RollBack();results.Add(new{Arquivo=path,Resultado=ex.Message});}}}});Form.ShowIssues(c.App,"Carga de famílias · Atenção",results,r=>true);return;
            }
            using(var folder=new System.Windows.Forms.FolderBrowserDialog())
            {
                if(folder.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;var results=new List<object>();
                foreach(var family in f.Picks<Family>("families"))
                {
                    string directory=Path.Combine(folder.SelectedPath,Safe(family.FamilyCategory?.Name??"Sem categoria"));Directory.CreateDirectory(directory);string path=Path.Combine(directory,Safe(family.Name)+".rfa");
                    if(File.Exists(path)){results.Add(new{Familia=family.Name,Resultado="Ignorada: arquivo já existe"});continue;}
                    Document fd=null;try{fd=c.Doc.EditFamily(family);fd.SaveAs(path,new SaveAsOptions{OverwriteExistingFile=false,MaximumBackups=1});}catch(Exception ex){results.Add(new{Familia=family.Name,Resultado=ex.Message});}finally{fd?.Close(false);}
                }
                Form.ShowIssues(c.App,"Exportação de famílias · Atenção",results,r=>true);
            }
        }
        internal static string Safe(string value){string s=string.Concat(value.Select(ch=>Path.GetInvalidFileNameChars().Contains(ch)?'_':ch)).TrimEnd('.',' ');if(string.IsNullOrEmpty(s))s="Familia";return s;}
    }
    [Transaction(TransactionMode.Manual)] public sealed class AssignWorkset : Command
    {
        protected override void Run(Context c)
        {
            if(!c.Doc.IsWorkshared)throw new ArgumentException("O projeto não utiliza compartilhamento de trabalho.");
            var worksets=new FilteredWorksetCollector(c.Doc).OfKind(WorksetKind.UserWorkset).ToWorksets();
            var f=c.ScopeForm("Atribuir workset","Use Filtrar elementos antes para selecionar por categoria e regras. Elementos em grupos, bloqueados ou não editáveis serão relatados.").Select("workset","Workset destino",worksets.OrderBy(w=>w.Name).Select(w=>new Choice(w.Name,w)));
            if(!f.Run())return;var target=f.Pick<Workset>("workset");var rows=new List<Change>();foreach(var e in c.Scope(f.Pick<string>("scope")))
            {
                var row=new Change{Id=e.Id.OqnValue(),Elemento=e.Name,Depois=target.Name};rows.Add(row);try{var p=e.get_Parameter(BuiltInParameter.ELEM_PARTITION_PARAM);Params.CheckWritable(p);row.Antes=p.AsValueString();row.Estado="Pronto";row.Apply=()=>{Params.SetInt(p,target.Id.IntegerValue);};}catch(Exception ex){row.Estado="Ignorado: "+ex.Message;}
            }
            Changes.Apply(c,"Atribuir workset",rows);
        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class ArchiveBackups : Command
    {
        protected override void Run(Context c)
        {
            using(var folder=new System.Windows.Forms.FolderBrowserDialog())
            {
                if(folder.ShowDialog()!=System.Windows.Forms.DialogResult.OK)return;string dest=Path.Combine(folder.SelectedPath,"OQN_Backups_"+DateTime.Now.ToString("yyyyMMdd_HHmmss"));
                var files=Directory.GetFiles(folder.SelectedPath,"*.rfa",SearchOption.AllDirectories).Where(p=>!p.Split(Path.DirectorySeparatorChar).Any(n=>(n.StartsWith("OQN_Backups_")||n.StartsWith("ERARIK_Backups_")))&&Regex.IsMatch(Path.GetFileName(p),@"\.\d{4}\.rfa$",RegexOptions.IgnoreCase)).ToList();
                if(!Form.Preview(c.App,"Arquivar backups",files.Select(p=>new{Arquivo=p}),"Os backups RFA serão movidos para uma pasta OQN_Backups, preservando as subpastas. Nenhum arquivo será excluído."))return;
                var moved=new List<KeyValuePair<string,string>>();try{foreach(var file in files){string relative=file.Substring(folder.SelectedPath.TrimEnd(Path.DirectorySeparatorChar).Length).TrimStart(Path.DirectorySeparatorChar);string target=Path.Combine(dest,relative);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Move(file,target);moved.Add(new KeyValuePair<string,string>(file,target));}}catch{foreach(var pair in moved.AsEnumerable().Reverse())if(!File.Exists(pair.Key)&&File.Exists(pair.Value))File.Move(pair.Value,pair.Key);throw;}
                c.Done(files.Count+" backup(s) arquivado(s) em:\n"+dest);
            }
        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class AddSharedParameters : Command
    {
        protected override void Run(Context c)
        {
            if(!c.Doc.IsFamilyDocument)throw new ArgumentException("Abra a família no Editor de Famílias para adicionar parâmetros compartilhados.");
            var open=new OpenFileDialog{Filter="Parâmetros compartilhados (*.txt)|*.txt"};if(open.ShowDialog()!=true)return;
            string old=c.App.Application.SharedParametersFilename;var defs=new List<ExternalDefinition>();
            try{c.App.Application.SharedParametersFilename=open.FileName;var file=c.App.Application.OpenSharedParameterFile();if(file==null)throw new ArgumentException("Arquivo de parâmetros inválido.");foreach(DefinitionGroup g in file.Groups)defs.AddRange(g.Definitions.Cast<ExternalDefinition>());}finally{c.App.Application.SharedParametersFilename=old;}
            var f=new Form(c.App,"Adicionar parâmetros compartilhados","Adiciona as definições selecionadas à família aberta, no grupo Dados. Parâmetros já existentes com o mesmo GUID são preservados.")
                .List("parameters","Definições",defs.OrderBy(d=>d.Name).Select(d=>new Choice(d.Name+" · "+d.GUID,d))).Check("instance","Parâmetros de instância",false);
            if(!f.Run())return;var selected=f.Picks<ExternalDefinition>("parameters");var fm=c.Doc.FamilyManager;var existing=fm.Parameters.Cast<FamilyParameter>().Where(p=>p.IsShared).Select(p=>p.GUID).ToHashSetCompat();
            c.Mutate("Adicionar parâmetros compartilhados",()=>{foreach(var d in selected.Where(d=>!existing.Contains(d.GUID)))fm.AddParameter(d,GroupTypeId.Data,f.Yes("instance"));});
        }
    }
    static class SetExtensions{public static HashSet<T> ToHashSetCompat<T>(this IEnumerable<T> values)=>new HashSet<T>(values);}
}
