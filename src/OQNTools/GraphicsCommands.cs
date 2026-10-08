using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;

namespace OQNTools
{
    public abstract class PaintCommand : Command
    {
        protected abstract bool Pipes {get;}
        protected override void Run(Context c)
        {
            var cats=new HashSet<long>{(long)(Pipes?BuiltInCategory.OST_PipeCurves:BuiltInCategory.OST_Conduit),(long)(Pipes?BuiltInCategory.OST_PipeFitting:BuiltInCategory.OST_ConduitFitting)};
            if(Pipes)cats.Remove((long)BuiltInCategory.OST_PipeFitting);
            var candidates=c.Scope("Modelo").Where(e=>e.Category!=null&&cats.Contains(e.Category.Id.OqnValue())).ToList();
            var options=ParameterOption.Discover(candidates).Where(x=>x.Scope=="auto").OrderByDescending(x=>x.Identity=="builtin:"+(long)BuiltInParameter.RBS_SYSTEM_ABBREVIATION_PARAM).ThenBy(x=>x.Name).ToList();
            var f=c.ScopeForm(Pipes?"Colorizar tubos":"Colorizar conduítes","Exclusivo para alunos OQN. Para a colorização funcionar, é necessário realizar o procedimento de substituição do segmento de tubulação, com criação de um novo material e posterior exclusão desse material. Técnica ensinada apenas no curso OQN.")
                .Select("parameter","Parâmetro que define o material",options.Select(o=>new Choice(o.Name,o)))
                .Check("replace","Substituir pintura existente",true)
                .Check("remove","Remover pintura, em vez de colorizar",false).ActionLabel("Colorizar / remover pintura");
            
            if(!f.Run())return;var option=f.Pick<ParameterOption>("parameter");
            var elements=c.Scope(f.Pick<string>("scope")).Where(e=>e.Category!=null&&cats.Contains(e.Category.Id.OqnValue())&&
                e.Category.Id.OqnValue()==(long)BuiltInCategory.OST_PipeCurves).GroupBy(e=>e.Id).Select(g=>g.First()).ToList();c.NeedSelection(elements);
            var materials=c.All<Material>().GroupBy(m=>m.Name.Trim(),StringComparer.OrdinalIgnoreCase).ToDictionary(g=>g.Key,g=>g.OrderBy(m=>m.Id.OqnValue()).First().Id,StringComparer.OrdinalIgnoreCase);
            var rows=new List<Change>();
            foreach(var e in elements){
                var row=new Change{Id=e.Id.OqnValue(),Elemento=e.Category.Name+" · "+e.Name};rows.Add(row);
                try{
                    var p=option.Get(e);string name=(Params.Text(p)??"").Trim();
                    if(!f.Yes("remove")&&(p==null||name.Length==0))throw new ArgumentException(p==null?"Parâmetro inexistente":"Valor vazio");
                    row.Depois=f.Yes("remove")?"Remover pintura":name;row.Estado="Pronto";
                    row.Apply=()=>{
                        var faces=NativeFaces(c.Doc,e).ToList();if(faces.Count==0)throw new ArgumentException("Nenhuma face nativa pintável encontrada.");
                        ElementId materialId=ElementId.InvalidElementId;
                        if(!f.Yes("remove")){
                            if(!materials.TryGetValue(name,out materialId)||!(c.Doc.GetElement(materialId) is Material current)||!string.Equals(current.Name.Trim(),name,StringComparison.OrdinalIgnoreCase)){
                                materialId=Material.Create(c.Doc,name);var material=(Material)c.Doc.GetElement(materialId);
                                uint hash=2166136261;foreach(char ch in name.ToUpperInvariant())hash=unchecked((hash^ch)*16777619);
                                material.Color=new Color((byte)(60+hash%160),(byte)(60+(hash>>8)%160),(byte)(60+(hash>>16)%160));materials[name]=materialId;
                            }
                        }
                        foreach(var face in faces){bool painted=c.Doc.IsPainted(e.Id,face);if(f.Yes("remove")){if(painted)c.Doc.RemovePaint(e.Id,face);}else if(!painted||f.Yes("replace"))c.Doc.Paint(e.Id,face,materialId);}
                    };
                }catch(Exception ex){row.Estado=Errors.Describe(ex);}
            }
            if(!Form.Preview(c.App,"Colorização · Conferir",rows,"Um material por valor normalizado. Elementos com erro serão ignorados.",f.Yes("remove")?"Remover pintura":"Colorizar"))return;
            Batch.Run(c,"Colorização",rows);
            Form.ShowChangeIssues(c.App,"Colorização · Atenção",rows);
        }
        static IEnumerable<Face> Faces(GeometryElement g)
        {
            if(g==null)yield break;
            foreach(GeometryObject obj in g)
                if(obj is Solid solid){foreach(Face face in solid.Faces)yield return face;}
                else if(obj is GeometryInstance instance){foreach(var face in Faces(instance.GetSymbolGeometry()))yield return face;}
        }
        internal static IEnumerable<Face> NativeFaces(Document doc,Element e)
        {
            var seen=new HashSet<string>();var geometry=e.get_Geometry(new Options {ComputeReferences=true,DetailLevel=ViewDetailLevel.Fine});
            foreach(var face in Faces(geometry))
            {
                var reference=face.Reference;if(reference==null)continue;
                string stable=reference.ConvertToStableRepresentation(doc);if(!seen.Add(stable))continue;
                if(reference.ElementId!=e.Id)continue;
                if(e.GetGeometryObjectFromReference(reference) is Face original)yield return original;
            }
        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class PaintPipes : PaintCommand { protected override bool Pipes=>true; }


    [Transaction(TransactionMode.Manual)] public sealed class DashByLevel : Command
    {
        protected override void Run(Context c)
        {
            if(!c.View.AreGraphicsOverridesAllowed())throw new ArgumentException("A vista não permite sobreposições.");
            var f=c.ScopeForm("Tracejar por altura","Aplica um padrão de linha ao elemento inteiro, preservando suas outras sobreposições. A altura usa o referencial interno do projeto; não soma novamente a cota compartilhada.")
                .Select("level","Nível de referência",c.All<Level>().OrderBy(l=>l.ProjectElevation).Select(l=>new Choice(l.Name,l)))
                .Text("min","Altura mínima em relação ao nível (m)","-2").Text("max","Altura máxima (m)","0,3")
                .Select("pattern","Padrão de linha",c.All<LinePatternElement>().OrderBy(p=>p.Name).Select(p=>new Choice(p.Name,p)))
                .Select("test","Critério",new[]{new Choice("Eixo inteiro dentro da faixa","inside"),new Choice("Eixo cruza a faixa (traceja o elemento inteiro)","intersect")});
            f.Validate=()=>{if(f.Number("min")>f.Number("max"))throw new ArgumentException("Altura mínima maior que máxima.");};
            if(!f.Run())return;var level=f.Pick<Level>("level");var pattern=f.Pick<LinePatternElement>("pattern");
            double min=level.ProjectElevation+UnitUtils.ConvertToInternalUnits(f.Number("min"),UnitTypeId.Meters),max=level.ProjectElevation+UnitUtils.ConvertToInternalUnits(f.Number("max"),UnitTypeId.Meters);
            var rows=new List<Change>();foreach(var e in c.Scope(f.Pick<string>("scope")))
            {
                if(!(e.Location is LocationCurve lc))continue;
                var points=lc.Curve.Tessellate();if(!Logic.InBand(points.Min(p=>p.Z),points.Max(p=>p.Z),min,max,f.Pick<string>("test")=="intersect"))continue;
                var ogs=new OverrideGraphicSettings(c.View.GetElementOverrides(e.Id));ogs.SetProjectionLinePatternId(pattern.Id);ogs.SetCutLinePatternId(pattern.Id);
                rows.Add(new Change{Id=e.Id.OqnValue(),Elemento=e.Name,Depois=pattern.Name,Estado="Pronto",Apply=()=>c.View.SetElementOverrides(e.Id,ogs)});
            }
            Changes.Apply(c,"Tracejar por altura",rows);
        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class TransferFilters : Command
    {
        sealed class Snapshot{public ElementId Id;public OverrideGraphicSettings Graphics;public bool Visible;public bool Enabled;}
        static List<Snapshot> Read(View v)=>v.GetOrderedFilters().Select(id=>new Snapshot{Id=id,Graphics=new OverrideGraphicSettings(v.GetFilterOverrides(id)),Visible=v.GetFilterVisibility(id),Enabled=v.GetIsFilterEnabled(id)}).ToList();
        protected override void Run(Context c)
        {
            var views=c.All<View>().Where(v=>v.AreGraphicsOverridesAllowed()).OrderBy(v=>v.Name).ToList();
            var origin=new Form(c.App,"ViewFilter · Copiar para outras vistas","Escolha uma vista ou modelo de vista de origem. Na próxima etapa, marque os filtros e os destinos.")
                .Select("source","Origem",views.Select(v=>new Choice((v.IsTemplate?"[Modelo] ":"[Vista] ")+v.Name,v)));
            if(!origin.Run())return;var source=origin.Pick<View>("source");
            if(Controlled(c.Doc,source))throw new ArgumentException("Os filtros desta origem são controlados por modelo de vista. Selecione o modelo como origem.");
            var available=Read(source);
            if(available.Count==0){c.Done("A origem não possui filtros.");return;}
            var f=new Form(c.App,"ViewFilter · Filtros e destinos","Origem: "+source.Name+". Copia cores, padrões, espessuras de linha, preenchimentos, transparência, visibilidade e ativação dos filtros marcados.")
                .CheckList("filters","Filtros da origem",available.Select(x=>new Choice(c.Doc.GetElement(x.Id).Name,x.Id)))
                .CheckList("targets","Vistas e modelos de destino",views.Where(v=>v.Id!=source.Id).Select(v=>new Choice((v.IsTemplate?"[Modelo] ":"[Vista] ")+v.Name,v)))
                .Check("replace","Remover filtros não selecionados dos destinos",false)
                .Check("categories","Copiar também sobreposições e visibilidade das categorias",false);
            f.Validate=()=>{if(f.Picks<ElementId>("filters").Count==0||f.Picks<View>("targets").Count==0)throw new ArgumentException("Marque ao menos um filtro e um destino.");};
            if(!f.Run())return;
            var selected=new HashSet<ElementId>(f.Picks<ElementId>("filters"));
            var snapshots=available.Where(x=>selected.Contains(x.Id)).ToList();var rows=new List<Change>();
            foreach(var target in f.Picks<View>("targets"))
            {
                var row=new Change{Id=target.Id.OqnValue(),Elemento=target.Name,Depois=snapshots.Count+" filtro(s)"};rows.Add(row);
                if(Controlled(c.Doc,target)){row.Estado="Ignorado: filtros controlados pelo modelo de vista; selecione o modelo";continue;}
                row.Estado="Pronto";
                row.Apply=()=>{
                    var existing=Read(target);
                    var desired=f.Yes("replace")?snapshots:existing.Where(x=>!selected.Contains(x.Id)).Concat(snapshots).ToList();
                    foreach(var id in target.GetFilters().ToList())target.RemoveFilter(id);
                    foreach(var snap in desired){target.AddFilter(snap.Id);target.SetFilterOverrides(snap.Id,snap.Graphics);target.SetFilterVisibility(snap.Id,snap.Visible);target.SetIsFilterEnabled(snap.Id,snap.Enabled);}
                    if(f.Yes("categories"))foreach(var cat in Categories(c.Doc.Settings.Categories)){
                        if(source.CanCategoryBeHidden(cat.Id)&&target.CanCategoryBeHidden(cat.Id)){target.SetCategoryOverrides(cat.Id,new OverrideGraphicSettings(source.GetCategoryOverrides(cat.Id)));target.SetCategoryHidden(cat.Id,source.GetCategoryHidden(cat.Id));}
                    }
                };
            }
            if(!Form.Preview(c.App,"ViewFilter · Conferir cópia",rows,"Os filtros marcados substituem as próprias sobreposições nos destinos. Outros filtros são mantidos, salvo se você marcou a remoção. Os filtros copiados ficam ao final na ordem da origem. Alterar um modelo afeta todas as vistas que ele controla."))return;
            using(var group=new TransactionGroup(c.Doc,"OQN · Copiar filtros")){
                group.Start();foreach(var row in rows.Where(x=>x.Apply!=null)){
                    try{c.Mutate("Copiar filtros para "+row.Elemento,row.Apply);row.Estado="Copiado";}
                    catch(Autodesk.Revit.Exceptions.RegenerationFailedException){throw;}
                    catch(Exception ex){row.Estado="Preservado: "+ex.Message;}
                }group.Assimilate();
            }
            Form.ShowIssues(c.App,"ViewFilter · Atenção",rows,r=>r.Estado!="Copiado");
        }
        static bool Controlled(Document doc,View view)
        {
            if(view.ViewTemplateId==ElementId.InvalidElementId)return false;
            var template=(View)doc.GetElement(view.ViewTemplateId);
            var parameter=new ElementId(BuiltInParameter.VIS_GRAPHICS_FILTERS);
            return template.GetTemplateParameterIds().Contains(parameter)&&!template.GetNonControlledTemplateParameterIds().Contains(parameter);
        }
        static IEnumerable<Category> Categories(CategoryNameMap categories){foreach(Category cat in categories){yield return cat;foreach(var sub in Categories(cat.SubCategories))yield return sub;}}
        static IEnumerable<Category> Categories(Autodesk.Revit.DB.Categories categories){foreach(Category cat in categories){yield return cat;foreach(var sub in Categories(cat.SubCategories))yield return sub;}}
    }
    [Transaction(TransactionMode.Manual)] public sealed class PurgeExistingAppearance : Command
    {
        protected override void Run(Context c)
        {
            // Use the engine's purgeability result, never infer safety from material references alone.
            var method=typeof(Document).GetMethods().FirstOrDefault(m=>m.Name=="GetUnusedElements"&&m.GetParameters().Length==1);
            if(method==null)throw new NotSupportedException("Esta API não expõe GetUnusedElements. A limpeza automática foi desabilitada nesta versão; use a limpeza nativa para conferir os recursos não usados.");
            var f=new Form(c.App,"Remover vírus fase existente","Remove apenas AppearanceAssetElement apontados pelo próprio Revit como não utilizados. Não remove fases nem materiais. Se uma exclusão atingir outro elemento, toda a operação será revertida.")
                .Text("name","Nome exato da aparência","Fase - Existente").Check("suffix","Incluir cópias com sufixo numérico (ex.: Fase - Existente 1)",true).Text("limit","Limite de exclusões","10000");
            f.Validate=()=>{if(f.Get("name").Length==0||f.Number("limit")<1||f.Number("limit")>100000||f.Number("limit")%1!=0)throw new ArgumentException("Informe nome e limite inteiro entre 1 e 100000.");};if(!f.Run())return;
            var unused=((IEnumerable<ElementId>)method.Invoke(c.Doc,new object[]{new HashSet<ElementId>()})).ToList();
            string expression="^"+Regex.Escape(f.Get("name"))+(f.Yes("suffix")?@"(?:\s*(?:\(\d+\)|\d+))?":"")+"$";
            var assets=unused.Select(c.Doc.GetElement).OfType<AppearanceAssetElement>().Where(a=>Regex.IsMatch(a.Name,expression,RegexOptions.CultureInvariant)).Take((int)f.Number("limit")).ToList();
            if(assets.Count==0){c.Done("Nenhuma aparência correspondente e não utilizada encontrada.");return;}
            if(!Form.Preview(c.App,"Aparências a excluir",assets.Select(a=>new{Id=a.Id.OqnValue(),Nome=a.Name}),"Esta operação pode ser desfeita pelo Desfazer do Revit."))return;
            var ids=new HashSet<ElementId>(assets.Select(a=>a.Id));int count=0;
            c.Mutate("Remover vírus fase existente",()=>{var removed=c.Doc.Delete(ids.ToList());if(removed.Any(id=>!ids.Contains(id)))throw new InvalidOperationException("Exclusão cancelada: o Revit incluiu elementos fora da lista revisada.");count=removed.Count;});
        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class TagPipes : Command
    {
        protected override void Run(Context c)
        {
            if(c.View.IsTemplate||c.View is ViewSchedule||c.View is ViewSheet||c.View is View3D v3&&(v3.IsPerspective||!v3.IsLocked))throw new ArgumentException("Use planta, corte ou vista 3D ortográfica bloqueada.");
            var symbols=c.All<FamilySymbol>().Where(s=>s.Category?.Id.OqnValue()==(long)BuiltInCategory.OST_PipeTags).OrderBy(s=>s.FamilyName).ThenBy(s=>s.Name).ToList();if(symbols.Count==0)throw new ArgumentException("Carregue uma família de identificação de tubos.");
            var f=c.ScopeForm("Identificar tubos","Classifica o eixo em relação ao plano horizontal do modelo. A tolerância é angular, em graus. Tags existentes na vista serão preservadas.")
                .Select("tag","Tipo de identificador",symbols.Select(s=>new Choice(s.FamilyName+" : "+s.Name,s)))
                .Select("direction","Orientação dos tubos",new[]{"Todos","Horizontal","Vertical","Inclinado"}.Select(s=>new Choice(s,s)))
                .Text("length","Comprimento mínimo (m)","1").Text("tolerance","Tolerância de inclinação (graus)","2")
                .Check("leader","Usar chamada",true).Text("offset","Afastamento da tag no modelo (m)","0,15");
            f.Validate=()=>{if(f.Number("length")<0||f.Number("offset")<0)throw new ArgumentException("Comprimento e afastamento não podem ser negativos.");Logic.Direction(1,0,0,f.Number("tolerance"));};if(!f.Run())return;
            if(!FilteredElementCollector.IsViewValidForElementIteration(c.Doc,c.View.Id))throw new ArgumentException("Esta vista não permite coletar elementos para identificação.");
            var visible=new HashSet<ElementId>(new FilteredElementCollector(c.Doc,c.View.Id).OfClass(typeof(Pipe)).ToElementIds());
            var tagged=new HashSet<ElementId>(new FilteredElementCollector(c.Doc,c.View.Id).OfClass(typeof(IndependentTag)).Cast<IndependentTag>().SelectMany(t=>t.GetTaggedLocalElementIds()));
            var symbol=f.Pick<FamilySymbol>("tag");double minimum=UnitUtils.ConvertToInternalUnits(f.Number("length"),UnitTypeId.Meters),offset=UnitUtils.ConvertToInternalUnits(f.Number("offset"),UnitTypeId.Meters);var rows=new List<Change>();
            foreach(var pipe in c.Scope(f.Pick<string>("scope")).OfType<Pipe>().Where(p=>visible.Contains(p.Id)&&!tagged.Contains(p.Id)))
            {
                var curve=(pipe.Location as LocationCurve)?.Curve;if(!(curve is Line)||curve.Length<minimum)continue;
                var delta=curve.GetEndPoint(1)-curve.GetEndPoint(0);string direction=Logic.Direction(delta.X,delta.Y,delta.Z,f.Number("tolerance"));if(f.Pick<string>("direction")!="Todos"&&direction!=f.Pick<string>("direction"))continue;
                var perp=c.View.ViewDirection.CrossProduct(delta.Normalize());if(perp.GetLength()<1e-8)perp=c.View.RightDirection;else perp=perp.Normalize();var point=curve.Evaluate(.5,true)+(f.Yes("leader")?perp.Multiply(offset):XYZ.Zero);
                rows.Add(new Change{Id=pipe.Id.OqnValue(),Elemento=pipe.Name,Depois=direction+" · "+symbol.Name,Estado="Pronto",Apply=()=>{if(!symbol.IsActive){symbol.Activate();c.Doc.Regenerate();}IndependentTag.Create(c.Doc,symbol.Id,c.View.Id,new Reference(pipe),f.Yes("leader"),TagOrientation.Horizontal,point);}});
            }
            Changes.Apply(c,"Identificar tubos",rows);
        }
    }
}
