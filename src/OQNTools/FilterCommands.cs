using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Microsoft.Win32;

namespace OQNTools
{
    public sealed class Rule
    { public string Scope="auto"; public int Grupo {get;set;}=1; public string Parametro {get;set;}=""; public string Operador {get;set;}="="; public string Valor {get;set;}=""; }
    [Transaction(TransactionMode.Manual)] public sealed class FilterElements : Command
    {
        protected override void Run(Context c)
        {
            var categories=c.Doc.Settings.Categories.Cast<Category>().Where(x=>x.CategoryType==CategoryType.Model).OrderBy(x=>x.Name).Select(x=>new Choice(x.Name,x.Id.OqnValue())).ToList();
            var rules=new List<Rule>();var found=new List<ElementResult>();var catIds=new HashSet<long>();string scopeName="Vista ativa",action="";List<Element> selected=null;List<ElementId> ids=null;bool searched=false;
            while(true)
            {
                var editor=new RuleEditor(rules);var result=new ElementResults();result.Set(found,searched);
                var f=new Form(c.App,"Filtrar elementos","Defina categorias e regras, busque e marque os resultados. Famílias aninhadas compartilhadas são encontradas pelo próprio ID.")
                    .Select("scope","Aplicar em",new[]{scopeName,"Seleção","Vista ativa","Modelo"}.Distinct().Select(x=>new Choice(x,x)))
                    .CheckList("cats","Categorias (nenhuma marcada = todas)",categories,catIds.Cast<object>())
                    .Custom("rules","Regras",editor).ActionLabel("Selecionar");
                action="Selecionar";
                f.ActionButton("Buscar elementos",()=>{editor.Validate();action="Buscar";f.DialogResult=true;});
                f.ActionButton("Carregar conjunto...",()=>{var open=new OpenFileDialog{Filter="Conjunto OQN|*.xml"};if(open.ShowDialog()==true)editor.Load(open.FileName);});
                f.ActionButton("Salvar conjunto...",()=>{var save=new SaveFileDialog{Filter="Conjunto OQN|*.xml",FileName="Regras.xml"};if(save.ShowDialog()==true)editor.Save(save.FileName);});
                f.Custom("results","",result);
                foreach(var name in new[]{"Isolar","Ocultar","Enquadrar","Criar caixa 3D"}){var chosen=name;f.ActionButton(name,()=>{if(result.Selected.Count==0)throw new ArgumentException("Busque e marque resultados primeiro.");action=chosen;f.DialogResult=true;});}
                f.Validate=()=>{if(result.Selected.Count==0)throw new ArgumentException("Busque e marque resultados primeiro.");};
                if(!f.Run())return;
                scopeName=f.Pick<string>("scope");catIds=new HashSet<long>(f.Picks<long>("cats"));rules=editor.Rules.ToList();
                if(action!="Buscar"){
                    ids=result.Selected.Select(id=>OqnElementIdCompatibility.Create(id)).ToList();selected=ids.Select(c.Doc.GetElement).Where(e=>e!=null&&e.IsValidObject).ToList();ids=selected.Select(e=>e.Id).ToList();break;
                }
                searched=true;found.Clear();var errors=new List<string>();var groups=rules.GroupBy(x=>x.Grupo).ToList();
                // No Parameters, IsShared, family geometry or subcomponent traversal.
                // Reacquire each element by ID. UI controls retain only managed strings/IDs.
                foreach(var id in c.Scope(scopeName).Select(e=>e.Id).Distinct().ToList()){
                    var e=c.Doc.GetElement(id);if(e==null||!e.IsValidObject||e.Category==null||e.Category.CategoryType!=CategoryType.Model)continue;
                    if(catIds.Count>0&&!catIds.Contains(e.Category.Id.OqnValue()))continue;
                    try{if(groups.Count==0||groups.Any(g=>g.All(rule=>Evaluate(e,rule))))found.Add(new ElementResult{Id=id.OqnValue(),Label=e.Category.Name+" | "+e.Name+" | "+id.OqnValue()});}
                    catch(AccessViolationException){throw;}
                    catch(Exception ex){errors.Add(id.OqnValue()+": "+Errors.Describe(ex));}
                }
                if(errors.Count>0)Form.ShowRows(c.App,"Itens não avaliados",errors.Select(x=>new{Motivo=x}));
            }
            if(action=="Selecionar"){
                c.Ui.Selection.SetElementIds(ids);var actual=c.Ui.Selection.GetElementIds();
                if(actual.Any(id=>!ids.Contains(id))||ids.Any(id=>!actual.Contains(id))){c.Ui.Selection.SetElementIds(actual.Where(ids.Contains).ToList());c.Done("Alguns aninhados não aceitaram seleção independente. Nenhuma família mãe foi mantida como substituta.");}
            }
            else if(action=="Enquadrar")c.Ui.ShowElements(ids);
            else if(action=="Isolar")c.Mutate("Isolar resultados",()=>c.View.IsolateElementsTemporary(ids));
            else if(action=="Ocultar"){
                var valid=ids.Where(id=>c.Doc.GetElement(id).CanBeHidden(c.View)&&!c.Doc.GetElement(id).IsHidden(c.View)).ToList();
                if(valid.Count>0)c.Mutate("Ocultar resultados",()=>c.View.HideElements(valid));
                if(valid.Count!=ids.Count)c.Done((ids.Count-valid.Count)+" itens não puderam ser ocultados ou já estavam ocultos.");
            }
            else Box(c,selected,.5);
        }
        static bool Evaluate(Element e,Rule r)
        {
            FilterTrace.Mark(e.Id.OqnValue(),r.Parametro);var p=NamedValues.Find(e,r.Parametro,r.Scope,false);if(r.Operador=="vazio")return p==null||!p.HasValue||string.IsNullOrWhiteSpace(NamedValues.Text(p));if(p==null||!p.HasValue)return false;
            if((p.StorageType==StorageType.Integer||p.StorageType==StorageType.Double)&&!new[]{"contém","não contém","começa","termina"}.Contains(r.Operador)){
                double actual=p.StorageType==StorageType.Integer?p.AsInteger():p.AsDouble();
                if(p.StorageType==StorageType.Double&&UnitUtils.IsMeasurableSpec(p.Definition.GetDataType()))actual=UnitUtils.ConvertFromInternalUnits(actual,p.GetUnitTypeId());
                double expected=Logic.Number(r.Valor);switch(r.Operador){case "=":return Math.Abs(actual-expected)<1e-9;case "!=":return Math.Abs(actual-expected)>=1e-9;case ">":return actual>expected;case ">=":return actual>=expected;case "<":return actual<expected;case "<=":return actual<=expected;default:return false;}
            }
            return Logic.Match(NamedValues.Text(p),r.Operador,r.Valor??"");
        }
        public static void Box(Context c,IEnumerable<Element> elements,double margin)
        {
            var points=new List<XYZ>();foreach(var e in elements){var b=e.get_BoundingBox(null);if(b==null)continue;for(int i=0;i<8;i++)points.Add(b.Transform.OfPoint(new XYZ((i&1)==0?b.Min.X:b.Max.X,(i&2)==0?b.Min.Y:b.Max.Y,(i&4)==0?b.Min.Z:b.Max.Z)));}
            if(points.Count==0)throw new ArgumentException("Os elementos não possuem geometria para caixa 3D.");double pad=Math.Max(UnitUtils.ConvertToInternalUnits(margin,UnitTypeId.Meters),.01);
            var box=new BoundingBoxXYZ{Min=new XYZ(points.Min(p=>p.X)-pad,points.Min(p=>p.Y)-pad,points.Min(p=>p.Z)-pad),Max=new XYZ(points.Max(p=>p.X)+pad,points.Max(p=>p.Y)+pad,points.Max(p=>p.Z)+pad)};View3D view=null;
            var templates=c.All<View3D>().Where(v=>v.IsTemplate).OrderBy(v=>v.Name).ToList();
            var choices=new List<Choice>{new Choice("Sem modelo de vista",ElementId.InvalidElementId)};
            choices.AddRange(templates.Select(v=>new Choice(v.Name,v.Id)));
            var form=new Form(c.App,"Criar caixa 3D","Defina o nome da nova vista e o modelo de vista a aplicar.")
                .Text("name","Nome da nova vista","OQN - Seleção "+DateTime.Now.ToString("yyyyMMdd HHmmss"))
                .Select("template","Modelo de vista",choices).ActionLabel("Criar vista");
            form.Validate=()=>{if(string.IsNullOrWhiteSpace(form.Get("name")))throw new ArgumentException("Informe o nome da vista.");if(c.All<View>().Any(v=>string.Equals(v.Name,form.Get("name").Trim(),StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Já existe uma vista com esse nome.");};
            if(!form.Run())return;
            c.Mutate("Caixa 3D dos resultados",()=>{var type=c.All<ViewFamilyType>().FirstOrDefault(v=>v.ViewFamily==ViewFamily.ThreeDimensional);if(type==null)throw new ArgumentException("Tipo de vista 3D indisponível.");view=View3D.CreateIsometric(c.Doc,type.Id);view.Name=form.Get("name").Trim();
                var template=form.Pick<ElementId>("template");if(template!=ElementId.InvalidElementId){if(!view.IsValidViewTemplate(template))throw new ArgumentException("Modelo de vista incompatível.");view.ViewTemplateId=template;}
                view.SetSectionBox(box);view.IsSectionBoxActive=true;});c.Ui.ActiveView=view;
        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class FindInRooms : Command
    {
        protected override void Run(Context c)
        {
            var containers=c.All<SpatialElement>().Where(e=>e is Room||e is Space).OrderBy(e=>e.Name).ToList();
            var f=c.ScopeForm("Localizar em ambientes","Teste do ponto de localização do elemento ou ponto médio do eixo. Não representa contenção integral da geometria. Ambientes sobrepostos produzem uma ocorrência para cada ambiente.")
                .List("rooms","Ambientes/espaços",containers.Select(e=>new Choice(e.Name+" ["+e.Id.OqnValue()+"]",e)))
                .Check("write","Gravar identificação do ambiente em parâmetro de instância")
                .Text("target","Parâmetro destino (texto)","Comentários");if(!f.Run())return;
            var rooms=f.Picks<SpatialElement>("rooms");var found=new List<Element>();var rows=new List<Change>();
            foreach(var e in c.Scope(f.Pick<string>("scope")).Where(e=>!(e is SpatialElement)))
            {
                XYZ point=e.Location is LocationPoint lp?lp.Point:e.Location is LocationCurve lc?lc.Curve.Evaluate(.5,true):null;if(point==null)continue;
                foreach(var room in rooms)
                {
                    bool inside=room is Room r?r.IsPointInRoom(point):((Space)room).IsPointInSpace(point);if(!inside)continue;found.Add(e);
                    var row=new Change{Id=e.Id.OqnValue(),Elemento=e.Name,Depois=room.Name,Estado="Encontrado"};rows.Add(row);
                    if(f.Yes("write"))try{var p=Params.Find(e,"I:"+f.Get("target"),true);Params.CheckWritable(p);if(p.StorageType!=StorageType.String)throw new ArgumentException("Destino deve ser texto.");row.Key=Params.Identity(p);row.Antes=Params.Text(p);row.Apply=()=>{Params.SetText(p,room.Name);};}catch(Exception ex){row.Estado=ex.Message;}
                }
            }
            if(f.Yes("write")){Changes.RejectConflicts(rows);Changes.Apply(c,"Identificar ambiente",rows);}else{Form.ShowRows(c.App,"Resultados por ambiente",rows);c.Ui.Selection.SetElementIds(found.Select(e=>e.Id).Distinct().ToList());}
        }
    }
}
