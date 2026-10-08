using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;

namespace OQNTools
{
    [Transaction(TransactionMode.Manual)]
    public sealed class ReplaceLinkedDimensions : Command
    {
        const double Tolerance = 0.01 / 304.8;
        sealed class SegmentData
        {
            public XYZ Origin; public double Value; public object Source;
        }
        sealed class Plan
        {
            public Dimension Old; public View View; public XYZ Direction;
            public List<SegmentData> Segments; public List<XYZ> Points;
            public LinkedReferenceIndex Index;
            public Dictionary<int, Reference> Local = new Dictionary<int, Reference>();
        }
        public sealed class Row
        {
            public string Vista { get; set; }
            public long Cota { get; set; }
            public string Resultado { get; set; }
            public string Referências {get;set;}
            internal bool Ready;
        }
        protected override void Run(Context c)
        {
            if(c.Doc.IsFamilyDocument)throw new ArgumentException("Abra um projeto Revit.");
            var eligible=c.All<View>().Where(Eligible).OrderBy(v=>v.Name).ToList();
            var f=new Form(c.App,"Substituir cotas de vínculos","As referências a RVT ou DWG/DXF vinculados serão trocadas por linhas de detalhe locais. Essas medidas deixam de acompanhar alterações do vínculo. As referências locais são preservadas.")
                .Select("scope","Vistas a processar",new[]{new Choice("Somente a vista ativa",false),new Choice("Escolher várias vistas",true)})
                .Text("length","Comprimento das linhas de detalhe (mm)","200")
                .Check("imports","Incluir também referências a CAD importado (sem vínculo externo)",false);
            f.Validate=()=>{if(double.IsNaN(f.Number("length"))||double.IsInfinity(f.Number("length"))||f.Number("length")<=0)throw new ArgumentException("Informe um comprimento maior que zero.");};
            if(!f.Run())return;
            List<View> views;
            if(f.Pick<bool>("scope"))
            {
                var pick=new Form(c.App,"Escolher vistas","Selecione as vistas desejadas. Use Ctrl para seleção múltipla ou pesquise e selecione os resultados.")
                    .List("views","Vistas",eligible.Select(v=>new Choice(v.Name+" · "+v.ViewType,v)));
                if(!pick.Run())return;views=pick.Picks<View>("views");
            }
            else views=new List<View>{c.View};
            if(views.Count==0||views.Any(v=>!Eligible(v)))throw new ArgumentException("Escolha plantas, cortes, elevações, detalhes, vistas de desenho ou legendas válidas.");
            var index=new LinkedReferenceIndex(c.Doc,f.Yes("imports"));
            var wanted=new Dictionary<long,ElementId>();
            foreach(var view in views){
                using(var collector=new FilteredElementCollector(c.Doc))foreach(Dimension d in collector.OfClass(typeof(Dimension)).OwnedByView(view.Id))wanted[d.Id.OqnValue()]=d.OwnerViewId;
                if(FilteredElementCollector.IsViewValidForElementIteration(c.Doc,view.Id))
                    using(var visible=new FilteredElementCollector(c.Doc,view.Id))foreach(Dimension d in visible.OfClass(typeof(Dimension)))wanted[d.Id.OqnValue()]=d.OwnerViewId;
            }
            var rows=new List<Row>();var originalView=c.View.Id;
            try {
                // View-dependent CAD references may be unavailable until their owner
                // view is opened. Switch only outside transactions.
                foreach(var group in wanted.GroupBy(x=>x.Value.OqnValue())){
                    var owner=c.Doc.GetElement(OqnElementIdCompatibility.Create(group.Key)) as View;
                    if(owner==null||!Eligible(owner))continue;
                    try{c.Ui.ActiveView=owner;c.Ui.RefreshActiveView();}
                    catch(Autodesk.Revit.Exceptions.RegenerationFailedException){throw;}
                    catch(AccessViolationException){throw;}
                    catch(Exception ex){foreach(var item in group)rows.Add(new Row{Vista=owner.Name,Cota=item.Key,Resultado="Vista não ativada: "+Errors.Describe(ex)});continue;}
                    foreach(var item in group){
                        var row=new Row{Vista=owner.Name,Cota=item.Key};
                        try {
                            var d=c.Doc.GetElement(OqnElementIdCompatibility.Create(item.Key)) as Dimension;
                            if(d==null)continue;
                            var references=d.References.Cast<Reference>().ToList();
                            var kinds=references.Select(index.Kind).ToList();
                            if(!kinds.Any(kind=>kind!=null))continue;
                            row.Referências=string.Join("; ",kinds.Where(kind=>kind!=null).Distinct());
                            if(!d.AreReferencesAvailable)throw new ArgumentException("Referências indisponíveis mesmo após ativar a vista. Verifique se o CAD/vínculo está carregado e a cota ainda tem referências válidas.");
                            Analyze(c.Doc,d,index);row.Ready=true;row.Resultado="Pronta para converter";
                        }catch(Autodesk.Revit.Exceptions.RegenerationFailedException){throw;}
                        catch(AccessViolationException){throw;}
                        catch(Exception ex){row.Resultado="Ignorada: "+Errors.Describe(ex);}
                        rows.Add(row);
                    }
                }
            }finally{RestoreView(c,originalView);}
            if(rows.Count==0){c.Done("Nenhuma cota com referência RVT/CAD vinculada identificada nas vistas escolhidas. Para CAD inserido como importação, ative a opção correspondente.");return;}
            if(!Form.Preview(c.App,"Prévia de substituição",rows,"Inclui cotas visíveis herdadas de vistas principais. A coluna Vista indica onde cada cota será alterada. Cada cota é processada uma única vez e preservada se falhar."))return;
            double length=f.Number("length")/304.8;
            if(length<=c.App.Application.ShortCurveTolerance)throw new ArgumentException("A linha é menor que o mínimo permitido pelo Revit.");
            try {
                using(var transactionGroup=new TransactionGroup(c.Doc,"OQN · Substituir cotas de vínculos")){
                    transactionGroup.Start();
                    foreach(var row in rows.Where(r=>r.Ready)){
                        try{
                            var original=(Dimension)c.Doc.GetElement(OqnElementIdCompatibility.Create(row.Cota));
                            if(original==null)throw new ArgumentException("A cota não existe mais.");
                            c.Ui.ActiveView=(View)c.Doc.GetElement(original.OwnerViewId);c.Ui.RefreshActiveView();
                            // Re-read after switching views and previous conversions;
                            // never trust native reference wrappers across rollback cycles.
                            var plan=Analyze(c.Doc,(Dimension)c.Doc.GetElement(OqnElementIdCompatibility.Create(row.Cota)),index);
                            c.Mutate("Substituir cota "+row.Cota,()=>Convert(c.Doc,plan,length));row.Resultado="Convertida";
                        }catch(Autodesk.Revit.Exceptions.RegenerationFailedException){throw;}
                        catch(AccessViolationException){throw;}
                        catch(Exception ex){row.Resultado="Preservada: "+Errors.Describe(ex);}
                    }
                    transactionGroup.Assimilate();
                }
            }finally{RestoreView(c,originalView);}
            Form.ShowIssues(c.App,"Substituição de cotas · Atenção",rows,r=>r.Resultado!="Convertida");
        }
        static void RestoreView(Context c,ElementId id){var view=c.Doc.GetElement(id) as View;if(view!=null&&view.IsValidObject&&c.Ui.ActiveView.Id!=id)c.Ui.ActiveView=view;}
        static bool Eligible(View v)=>!v.IsTemplate&&(v.ViewType==ViewType.FloorPlan||v.ViewType==ViewType.CeilingPlan||v.ViewType==ViewType.EngineeringPlan||v.ViewType==ViewType.Section||v.ViewType==ViewType.Elevation||v.ViewType==ViewType.Detail||v.ViewType==ViewType.DraftingView||v.ViewType==ViewType.Legend);
        static List<SegmentData> Segments(Dimension d)
        {
            if(d.NumberOfSegments==0)return new List<SegmentData>{new SegmentData{Origin=d.Origin,Value=d.Value??-1,Source=d}};
            return d.Segments.Cast<DimensionSegment>().Select(s=>new SegmentData{Origin=s.Origin,Value=s.Value??-1,Source=s}).ToList();
        }
        static Plan Analyze(Document doc,Dimension d,LinkedReferenceIndex index)
        {
            var line=d.Curve as Line;
            if(line==null)throw new ArgumentException("Apenas cotas lineares são suportadas.");
            if(d.Pinned||d.GroupId!=ElementId.InvalidElementId)throw new ArgumentException("Cota fixada ou pertencente a grupo.");
            if(d.NumberOfSegments>0)
            {
                if((d.NumberOfSegments>1&&d.AreSegmentsEqual)||d.Segments.Cast<DimensionSegment>().Any(s=>s.IsLocked))throw new ArgumentException("Cota com igualdade ou restrição bloqueada.");
            }
            else if(d.IsLocked)throw new ArgumentException("Cota bloqueada.");
            var p=new Plan{Index=index,Old=d,View=(View)doc.GetElement(d.OwnerViewId),Direction=line.Direction.Normalize(),Segments=Segments(d),Points=new List<XYZ>()};
            if(p.Segments.Any(s=>s.Value<=Tolerance||s.Origin==null))throw new ArgumentException("Medida nula, coincidente ou indisponível.");
            if(Math.Abs(p.Direction.DotProduct(p.View.ViewDirection))>1e-8)throw new ArgumentException("Cota fora do plano da vista.");
            foreach(var s in p.Segments)
                foreach(var point in new[]{s.Origin-p.Direction*s.Value/2,s.Origin+p.Direction*s.Value/2})
                    if(!p.Points.Any(x=>x.DistanceTo(point)<=Tolerance))p.Points.Add(point);
            p.Points=p.Points.OrderBy(x=>x.DotProduct(p.Direction)).ToList();
            p.Segments=p.Segments.OrderBy(s=>s.Origin.DotProduct(p.Direction)).ToList();
            var refs=d.References.Cast<Reference>().ToList();
            if(p.Points.Count!=refs.Count||p.Points.Count!=p.Segments.Count+1)throw new ArgumentException("Cadeia de cotas descontínua ou ambígua.");
            for(int i=0;i<p.Segments.Count;i++)
                if(Math.Abs(p.Points[i].DistanceTo(p.Points[i+1])-p.Segments[i].Value)>Tolerance||((p.Points[i]+p.Points[i+1])/2).DistanceTo(p.Segments[i].Origin)>Tolerance)
                    throw new ArgumentException("Não foi possível reconstruir a cadeia de medidas.");
            var localStable=new Dictionary<int,string>();
            foreach(var segment in p.Segments)segment.Source=CaptureText(segment.Source);
            var localReferences=refs.Where(r=>index.Kind(r)==null).Select(r=>r.ConvertToStableRepresentation(doc)).ToList();
            foreach(var stable in localReferences)
            {
                var reference=Reference.ParseFromStableRepresentation(doc,stable);
                double station=ProbeStation(doc,reference,p);
                var indices=Enumerable.Range(0,p.Points.Count).Where(i=>Math.Abs(p.Points[i].DotProduct(p.Direction)-station)<=Tolerance).ToList();
                if(indices.Count!=1||localStable.ContainsKey(indices[0]))throw new ArgumentException("Referência local ambígua; original mantido.");
                localStable.Add(indices[0],stable);
            }
            foreach(var pair in localStable)p.Local.Add(pair.Key,Reference.ParseFromStableRepresentation(doc,pair.Value));
            return p;
        }
        static double ProbeStation(Document doc,Reference reference,Plan p)
        {
            // Family planes, CAD edges, endpoints and datum references may not expose
            // their geometry through GetGeometryObjectFromReference. Let Revit resolve
            // the same reference through a temporary dimension instead.
            using(var transaction=new Transaction(doc,"OQN · Resolver referência da cota"))
            {
                transaction.Start();
                try
                {
                    var normal=p.View.ViewDirection.Normalize();
                    var across=normal.CrossProduct(p.Direction).Normalize();
                    var baseline=p.Points[0]-p.Direction*(p.Points.Last().DistanceTo(p.Points[0])+10);
                    baseline=baseline-normal*((baseline-p.View.Origin).DotProduct(normal));
                    var line=doc.Create.NewDetailCurve(p.View,Line.CreateBound(baseline-across,baseline+across));
                    doc.Regenerate();var refs=new ReferenceArray();refs.Append(line.GeometryCurve.Reference);refs.Append(reference);
                    var probe=doc.Create.NewDimension(p.View,Line.CreateBound(baseline,baseline+p.Direction*2),refs,p.Old.DimensionType);
                    doc.Regenerate();
                    if(probe==null||!probe.Value.HasValue||probe.Origin==null)throw new ArgumentException("Revit não conseguiu resolver esta referência em uma cota temporária.");
                    double station=2*probe.Origin.DotProduct(p.Direction)-baseline.DotProduct(p.Direction);
                    if(Math.Abs(station-baseline.DotProduct(p.Direction)-probe.Value.Value)>Tolerance)throw new ArgumentException("Referência temporária ambígua.");
                    return station;
                }
                finally{transaction.RollBack();}
            }
        }
        static void Convert(Document doc,Plan p,double length)
        {
            var refs=new ReferenceArray();var normal=p.View.ViewDirection.Normalize();var across=normal.CrossProduct(p.Direction).Normalize();
            for(int i=0;i<p.Points.Count;i++)
            {
                if(p.Local.TryGetValue(i,out Reference retained)){refs.Append(retained);continue;}
                var q=p.Points[i];q=q-normal*((q-p.View.Origin).DotProduct(normal));
                var detail=doc.Create.NewDetailCurve(p.View,Line.CreateBound(q-across*length/2,q+across*length/2));
                doc.Regenerate();refs.Append(detail.GeometryCurve.Reference);
            }
            var newDim=doc.Create.NewDimension(p.View,Line.CreateBound(p.Points[0],p.Points[p.Points.Count-1]),refs,p.Old.DimensionType);
            if(newDim==null)throw new InvalidOperationException("Revit não criou a cota.");
            newDim.HasLeader=p.Old.HasLeader;
            doc.Regenerate();
            var after=Segments(newDim).OrderBy(s=>s.Origin.DotProduct(p.Direction)).ToList();
            if(after.Count!=p.Segments.Count)throw new InvalidOperationException("Número de segmentos alterado.");
            for(int i=0;i<after.Count;i++)
            {
                if(Math.Abs(after[i].Value-p.Segments[i].Value)>Tolerance||after[i].Origin.DistanceTo(p.Segments[i].Origin)>Tolerance)
                    throw new InvalidOperationException("Medida ou posição divergente; conversão revertida.");
                CopyText(p.Segments[i].Source,after[i].Source);
            }
            p.View.SetElementOverrides(newDim.Id,p.View.GetElementOverrides(p.Old.Id));
            if(newDim.References.Cast<Reference>().Any(r=>p.Index.Kind(r)!=null))throw new InvalidOperationException("A nova cota ainda referencia um vínculo.");
            var oldId=p.Old.Id;
            var deleted=doc.Delete(oldId);
            if(deleted.Any(id=>id!=oldId))throw new InvalidOperationException("A exclusão afetaria outros elementos; original preservado.");
        }
        static Dictionary<string,object> CaptureText(object from)
        {
            var values=new Dictionary<string,object>();
            foreach(var name in new[]{"ValueOverride","Above","Below","Prefix","Suffix","TextPosition","LeaderEndPosition"}){
                var property=from.GetType().GetProperty(name);if(property==null)continue;
                try{var value=property.GetValue(from,null);if(value!=null)values[name]=value;}
                catch(System.Reflection.TargetInvocationException){} // Optional unsupported text feature.
            }
            return values;
        }
        static void CopyText(object from,object to)
        {
            var values=(Dictionary<string,object>)from;
            foreach(var entry in values){
                var target=to.GetType().GetProperty(entry.Key);if(target==null||!target.CanWrite)continue;
                object current=null;try{current=target.GetValue(to,null);}catch(System.Reflection.TargetInvocationException){}
                if(Equals(current,entry.Value))continue;
                if(current is XYZ a&&entry.Value is XYZ b&&a.DistanceTo(b)<=Tolerance)continue;
                target.SetValue(to,entry.Value,null);
            }
        }
    }
}
