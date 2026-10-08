using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;

namespace OQNTools
{
    [Transaction(TransactionMode.Manual)] public sealed class BulkRename : Command
    {
        protected override void Run(Context c)
        {
            var mode=new Form(c.App,"Renomear em lote","Adiciona prefixo/sufixo e substitui texto em nomes. A prévia permite revisar cada nome; conflitos fazem a transação inteira voltar ao estado anterior.")
                .Select("kind","Elementos",new[]{"Vistas","Folhas: nome","Folhas: número","Famílias","Tipos","Materiais"}.Select(x=>new Choice(x,x)));
            if(!mode.Run())return;string kind=mode.Pick<string>("kind");List<Element> elements;
            switch(kind){case "Vistas":elements=c.All<View>().Where(v=>!(v is ViewSheet)).Cast<Element>().ToList();break;case "Folhas: nome":case "Folhas: número":elements=c.All<ViewSheet>().Cast<Element>().ToList();break;case "Famílias":elements=c.All<Family>().Cast<Element>().ToList();break;case "Tipos":elements=new FilteredElementCollector(c.Doc).WhereElementIsElementType().ToElements().ToList();break;default:elements=c.All<Material>().Cast<Element>().ToList();break;}
            var f=new Form(c.App,"Renomear · "+kind,"Substituição literal e sensível a maiúsculas/minúsculas. Deixe Procurar vazio para apenas prefixo/sufixo.")
                .List("elements","Elementos",elements.OrderBy(e=>e.Name).Select(e=>new Choice((e is ElementType t?t.FamilyName+" : ":"")+(kind=="Folhas: número"?((ViewSheet)e).SheetNumber:e.Name)+" ["+e.Id.OqnValue()+"]",e)))
                .Text("find","Procurar").Text("replace","Substituir por").Text("prefix","Prefixo").Text("suffix","Sufixo");if(!f.Run())return;
            var rows=new List<Change>();foreach(var e in f.Picks<Element>("elements"))
            {
                string old=kind=="Folhas: número"?((ViewSheet)e).SheetNumber:e.Name;string next=f.Get("prefix")+(f.Get("find")==""?old:old.Replace(f.Get("find"),f.Get("replace")))+f.Get("suffix");if(next==old)continue;
                var row=new Change{Id=e.Id.OqnValue(),Elemento=e.Name,Antes=old,Depois=next,Estado=string.IsNullOrWhiteSpace(next)?"ERRO: nome vazio":"Pronto"};if(!string.IsNullOrWhiteSpace(next))row.Apply=()=>{if(kind=="Folhas: número")((ViewSheet)e).SheetNumber=next;else e.Name=next;};rows.Add(row);
            }
            if(!Form.Preview(c.App,"Prévia dos nomes",rows,"O Revit verifica nomes duplicados e caracteres permitidos. Se houver conflito, toda a operação será revertida."))return;
            var valid=rows.Where(r=>r.Apply!=null).ToList();c.Mutate("Renomear em lote",()=>{foreach(var row in valid){var e=c.Doc.GetElement(OqnElementIdCompatibility.Create(row.Id));string temp="OQN_TEMP_"+Guid.NewGuid().ToString("N");if(kind=="Folhas: número")((ViewSheet)e).SheetNumber=temp;else e.Name=temp;}foreach(var row in valid)row.Apply();});
        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class DuplicateViews : Command
    {
        protected override void Run(Context c)
        {
            var f=new Form(c.App,"Duplicar vistas","Escolha explicitamente as vistas. A seleção independe da posição do Navegador de Projeto.")
                .List("views","Vistas",c.All<View>().Where(v=>!v.IsTemplate&&!(v is ViewSheet)).OrderBy(v=>v.Name).Select(v=>new Choice(v.Name,v)))
                .Select("option","Modo",new[]{new Choice("Duplicar",ViewDuplicateOption.Duplicate),new Choice("Com detalhamento",ViewDuplicateOption.WithDetailing),new Choice("Como dependente",ViewDuplicateOption.AsDependent)})
                .Text("suffix","Sufixo"," - Cópia");if(!f.Run())return;
            var option=f.Pick<ViewDuplicateOption>("option");var rows=new List<Change>();foreach(var v in f.Picks<View>("views"))
            {var row=new Change{Id=v.Id.OqnValue(),Elemento=v.Name,Depois=v.Name+f.Get("suffix")};rows.Add(row);if(!v.CanViewBeDuplicated(option)){row.Estado="Ignorada: modo não suportado";continue;}row.Estado="Pronto";row.Apply=()=>{var copy=(View)c.Doc.GetElement(v.Duplicate(option));copy.Name=UniqueName(c,rows.First(x=>x.Id==v.Id.OqnValue()).Depois);};}Changes.Apply(c,"Duplicar vistas",rows);
        }
        internal static string UniqueName(Context c,string name)
        {var names=new HashSet<string>(c.All<View>().Select(v=>v.Name),StringComparer.OrdinalIgnoreCase);string n=name;int i=2;while(names.Contains(n))n=name+" ("+i+++")";return n;}
    }
    [Transaction(TransactionMode.Manual)] public sealed class SheetsFromViews : Command
    {
        protected override void Run(Context c)
        {
            var titleblocks=c.All<FamilySymbol>().Where(s=>s.Category?.Id.OqnValue()==(long)BuiltInCategory.OST_TitleBlocks).ToList();
            var f=new Form(c.App,"Criar folhas a partir de vistas","Cria uma folha por vista e posiciona a vista no centro do carimbo. Vistas não colocáveis fazem a operação ser revertida.")
                .List("views","Vistas",c.All<View>().Where(v=>!v.IsTemplate&&!(v is ViewSheet)&&!(v is ViewSchedule)).OrderBy(v=>v.Name).Select(v=>new Choice(v.Name,v)))
                .Select("titleblock","Carimbo",titleblocks.Select(s=>new Choice(s.FamilyName+" : "+s.Name,s))).Text("prefix","Prefixo do número","E-").Text("start","Número inicial","1");
            f.Validate=()=>{if(f.Number("start")<0||f.Number("start")>1000000||f.Number("start")%1!=0)throw new ArgumentException("Número inicial inválido.");};if(!f.Run())return;
            var block=f.Pick<FamilySymbol>("titleblock");var views=f.Picks<View>("views");int i=(int)f.Number("start");
            if(!Form.Preview(c.App,"Novas folhas",views.Select(v=>new{Vista=v.Name}),"Será criada uma folha por vista selecionada."))return;
            c.Mutate("Folhas a partir de vistas",()=>{foreach(var v in views){var sheet=ViewSheet.Create(c.Doc,block.Id);sheet.SheetNumber=UniqueNumber(c,f.Get("prefix")+(i++).ToString("D3"));sheet.Name=v.Name;c.Doc.Regenerate();if(!Viewport.CanAddViewToSheet(c.Doc,sheet.Id,v.Id))throw new ArgumentException("Vista não pode ser colocada: "+v.Name);var b=sheet.Outline;Viewport.Create(c.Doc,sheet.Id,v.Id,new XYZ((b.Min.U+b.Max.U)/2,(b.Min.V+b.Max.V)/2,0));}});
        }
        internal static string UniqueNumber(Context c,string proposed){var names=new HashSet<string>(c.All<ViewSheet>().Select(s=>s.SheetNumber),StringComparer.OrdinalIgnoreCase);string n=proposed;int i=2;while(names.Contains(n))n=proposed+"-"+i++;return n;}
    }
    [Transaction(TransactionMode.Manual)] public sealed class DuplicateSheets : Command
    {
        protected override void Run(Context c)
        {
            var f=new Form(c.App,"Duplicar folhas","Copia carimbos, vistas posicionadas e instâncias de tabelas. Legendas e tabelas são reutilizadas. Vistas são duplicadas com detalhamento. Anotações desenhadas diretamente na folha não são copiadas nesta edição.")
                .List("sheets","Folhas",c.All<ViewSheet>().Where(s=>!s.IsPlaceholder).OrderBy(s=>s.SheetNumber).Select(s=>new Choice(s.SheetNumber+" · "+s.Name,s)))
                .Text("suffix","Sufixo"," - Cópia");if(!f.Run())return;
            var sheets=f.Picks<ViewSheet>("sheets");if(!Form.Preview(c.App,"Duplicar folhas",sheets.Select(s=>new{Numero=s.SheetNumber,Nome=s.Name}),"Novos números de folha serão gerados sem colisões."))return;
            c.Mutate("Duplicar folhas",()=>{foreach(var source in sheets)
            {
                var target=ViewSheet.Create(c.Doc,ElementId.InvalidElementId);target.Name=source.Name+f.Get("suffix");target.SheetNumber=SheetsFromViews.UniqueNumber(c,source.SheetNumber+"-C");
                var tb=new FilteredElementCollector(c.Doc,source.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType().ToElementIds();if(tb.Count>0)ElementTransformUtils.CopyElements(source,tb,target,Transform.Identity,new CopyPasteOptions());
                foreach(var id in source.GetAllViewports())
                {
                    var vp=(Viewport)c.Doc.GetElement(id);var view=(View)c.Doc.GetElement(vp.ViewId);var use=view.Id;
                    if(view.ViewType!=ViewType.Legend){if(!view.CanViewBeDuplicated(ViewDuplicateOption.WithDetailing))throw new ArgumentException("Não foi possível duplicar: "+view.Name);use=view.Duplicate(ViewDuplicateOption.WithDetailing);((View)c.Doc.GetElement(use)).Name=DuplicateViews.UniqueName(c,view.Name+f.Get("suffix"));}
                    var placed=Viewport.Create(c.Doc,target.Id,use,vp.GetBoxCenter());placed.ChangeTypeId(vp.GetTypeId());placed.Rotation=vp.Rotation;placed.LabelOffset=vp.LabelOffset;placed.LabelLineLength=vp.LabelLineLength;
                }
                foreach(var si in new FilteredElementCollector(c.Doc,source.Id).OfClass(typeof(ScheduleSheetInstance)).Cast<ScheduleSheetInstance>().Where(s=>!s.IsTitleblockRevisionSchedule))ScheduleSheetInstance.Create(c.Doc,target.Id,si.ScheduleId,si.Point);
            }});
        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class AlignViewports : Command
    {
        protected override void Run(Context c)
        {
            var vps=c.All<Viewport>().ToList();Func<Viewport,string> label=v=>((ViewSheet)c.Doc.GetElement(v.SheetId)).SheetNumber+" · "+c.Doc.GetElement(v.ViewId).Name;
            var f=new Form(c.App,"Alinhar vistas nas folhas","Alinha o centro da caixa das vistas nas coordenadas da folha. Não altera escala, recorte ou título.")
                .Select("source","Viewport de referência",vps.OrderBy(label).Select(v=>new Choice(label(v),v)))
                .List("targets","Viewports destino",vps.OrderBy(label).Select(v=>new Choice(label(v),v)))
                .Select("axis","Alinhamento",new[]{"X e Y","Somente X","Somente Y"}.Select(x=>new Choice(x,x)));if(!f.Run())return;
            var source=f.Pick<Viewport>("source");var point=source.GetBoxCenter();string axis=f.Pick<string>("axis");var rows=new List<Change>();
            foreach(var target in f.Picks<Viewport>("targets").Where(v=>v.Id!=source.Id)){var old=target.GetBoxCenter();var next=new XYZ(axis=="Somente Y"?old.X:point.X,axis=="Somente X"?old.Y:point.Y,old.Z);rows.Add(new Change{Id=target.Id.OqnValue(),Elemento=label(target),Depois=axis,Estado=target.Pinned?"Ignorado: fixado":"Pronto",Apply=target.Pinned?(Action)null:()=>target.SetBoxCenter(next)});}Changes.Apply(c,"Alinhar viewports",rows);
        }
    }
}
