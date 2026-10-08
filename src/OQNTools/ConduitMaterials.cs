using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.UI;

namespace OQNTools
{
    internal static class ConduitMaterials
    {
        internal static readonly Guid MaterialGuid=new Guid("ba2fa071-a5d0-47b5-811a-24d50ad3396a");
        internal static Definition FindDefinition(Document doc)
        {
            var matches=new List<Definition>();var bindings=doc.ParameterBindings.ForwardIterator();
            while(bindings.MoveNext()){
                var binding=bindings.Current as TypeBinding;if(binding==null||!binding.Categories.Contains(doc.Settings.Categories.get_Item(BuiltInCategory.OST_Conduit)))continue;
                if(bindings.Key.Name=="Material"){
                    if(!bindings.Key.GetDataType().Equals(SpecTypeId.Reference.Material))throw new ArgumentException("O parâmetro Material existente em conduítes não é do tipo Material.");
                    matches.Add(bindings.Key);
                }
            }
            if(matches.Count>1)throw new ArgumentException("Há mais de um parâmetro de tipo chamado Material nos conduítes. Remova a ambiguidade antes de configurar.");
            return matches.FirstOrDefault();
        }
        internal static Definition Ensure(Context c)
        {
            var existing=FindDefinition(c.Doc);if(existing!=null)return existing;
            // Do not introduce a homonymous type parameter over an instance binding.
            var it=c.Doc.ParameterBindings.ForwardIterator();while(it.MoveNext())if(it.Key.Name=="Material"&&it.Current is InstanceBinding ib&&ib.Categories.Contains(c.Doc.Settings.Categories.get_Item(BuiltInCategory.OST_Conduit)))throw new ArgumentException("Já existe Material como parâmetro de instância nos conduítes. Converta esse vínculo de parâmetro para tipo antes de continuar.");
            Definition definition=SharedParameterElement.Lookup(c.Doc,MaterialGuid)?.GetDefinition();
            string previous=c.App.Application.SharedParametersFilename;string path=Path.Combine(Path.GetTempPath(),"oqn-material-"+Guid.NewGuid().ToString("N")+".txt");
            try{
                if(definition==null){
                    File.WriteAllText(path,"# OQN Tools\n*META\tVERSION\tMINVERSION\nMETA\t2\t1\n*GROUP\tID\tNAME\n*PARAM\tGUID\tNAME\tDATATYPE\tDATACATEGORY\tGROUP\tVISIBLE\tDESCRIPTION\tUSERMODIFIABLE\n");
                    c.App.Application.SharedParametersFilename=path;var file=c.App.Application.OpenSharedParameterFile();if(file==null)throw new InvalidOperationException("Não foi possível criar a definição Material.");
                    var group=file.Groups.Create("OQN Tools");definition=group.Definitions.Create(new ExternalDefinitionCreationOptions("Material",SpecTypeId.Reference.Material){GUID=MaterialGuid,Visible=true,UserModifiable=true,Description="Material aplicado automaticamente aos conduítes deste tipo pelo OQN Tools."});
                }
                var categories=c.App.Application.Create.NewCategorySet();categories.Insert(c.Doc.Settings.Categories.get_Item(BuiltInCategory.OST_Conduit));
                if(!c.Doc.ParameterBindings.Insert(definition,c.App.Application.Create.NewTypeBinding(categories),GroupTypeId.Materials))throw new InvalidOperationException("Não foi possível vincular Material aos tipos de conduíte.");
                c.Doc.Regenerate();return FindDefinition(c.Doc)??throw new InvalidOperationException("O campo Material não ficou disponível.");
            }finally{c.App.Application.SharedParametersFilename=previous;if(File.Exists(path))File.Delete(path);}
        }
        internal static bool Paint(Document doc,Conduit conduit,Definition definition)
        {
            var type=doc.GetElement(conduit.GetTypeId());var parameter=type?.get_Parameter(definition);if(parameter==null||!parameter.HasValue)return false;
            var material=parameter.HasValue?parameter.AsElementId():ElementId.InvalidElementId;
            if(material.OqnValue()>0&&!(doc.GetElement(material) is Material))throw new ArgumentException("Material do tipo não encontrado.");
            var faces=PaintCommand.NativeFaces(doc,conduit).ToList();if(faces.Count==0)throw new ArgumentException("Conduíte sem faces disponíveis para aplicar o material.");
            foreach(var face in faces){
                bool painted=doc.IsPainted(conduit.Id,face);
                if(material.OqnValue()<=0){if(painted)doc.RemovePaint(conduit.Id,face);}
                else if(!painted||doc.GetPaintedMaterial(conduit.Id,face)!=material)doc.Paint(conduit.Id,face,material);
            }
            return true;
        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class PaintConduits:Command
    {
        protected override void Run(Context c)
        {
            if(c.Doc.IsFamilyDocument)throw new ArgumentException("Abra um projeto Revit.");
            var materials=new List<Choice>{new Choice("Manter os materiais atuais dos tipos",long.MinValue),new Choice("<Por categoria>",-1L)};
            materials.AddRange(c.All<Material>().OrderBy(m=>m.Name).Select(m=>new Choice(m.Name,m.Id.OqnValue())));
            var form=new Form(c.App,"Material dos conduítes","Adiciona o campo Material em Propriedades de tipo > Materiais e acabamentos. Alterações nesse campo serão aplicadas automaticamente aos conduítes do tipo. Conexões não são pintadas.")
                .CheckList("types","Tipos a configurar",c.All<ConduitType>().OrderBy(t=>t.Name).Select(t=>new Choice(t.Name,t.Id.OqnValue())))
                .Select("material","Material para os tipos marcados",materials).ActionLabel("Configurar / aplicar");
            form.Validate=()=>{if(form.Pick<long>("material")!=long.MinValue&&form.Picks<long>("types").Count==0)throw new ArgumentException("Marque os tipos que receberão o material.");};if(!form.Run())return;
            Definition definition=null;
            c.Mutate("Configurar Material dos conduítes",()=>{
                definition=ConduitMaterials.Ensure(c);long material=form.Pick<long>("material");
                if(material!=long.MinValue)foreach(long id in form.Picks<long>("types")){var p=c.Doc.GetElement(OqnElementIdCompatibility.Create(id)).get_Parameter(definition);Params.CheckWritable(p);p.Set(OqnElementIdCompatibility.Create(material));}
            });
            var rows=new List<Change>();foreach(var conduit in c.All<Conduit>()){
                var element=conduit;rows.Add(new Change{Id=element.Id.OqnValue(),Elemento=element.Name,Estado="Pronto",Apply=()=>ConduitMaterials.Paint(c.Doc,element,definition)});
            }
            Batch.Run(c,"Aplicar Material dos conduítes",rows,true);Form.ShowChangeIssues(c.App,"Conduítes · Atenção",rows);
        }
    }
    internal sealed class ConduitMaterialUpdater:IUpdater
    {
        readonly UpdaterId id;
        internal static readonly FailureDefinitionId WarningId=new FailureDefinitionId(new Guid("76dc7215-a955-4357-9f23-b8b36d8ef9dd"));
        internal ConduitMaterialUpdater(AddInId addin){id=new UpdaterId(addin,new Guid("83ffb261-a6ad-41c9-9c0c-6461746db80c"));}
        public UpdaterId GetUpdaterId()=>id;
        public string GetUpdaterName()=>"OQN Tools · Material dos conduítes";
        public string GetAdditionalInformation()=>"Atualiza a pintura de conduítes pelo Material configurado no tipo.";
        public ChangePriority GetChangePriority()=>ChangePriority.MEPFixtures;
        public void Execute(UpdaterData data)
        {
            var doc=data.GetDocument();if(doc.IsFamilyDocument)return;
            Definition definition;try{definition=ConduitMaterials.FindDefinition(doc);}catch(Exception ex){Errors.Describe(ex);doc.PostFailure(new FailureMessage(WarningId));return;}if(definition==null)return;
            var changed=data.GetAddedElementIds().Concat(data.GetModifiedElementIds()).Distinct().Select(doc.GetElement).Where(e=>e!=null).ToList();
            var typeIds=new HashSet<ElementId>(changed.OfType<ConduitType>().Select(t=>t.Id));
            var conduits=changed.OfType<Conduit>().ToList();
            if(typeIds.Count>0)conduits.AddRange(new FilteredElementCollector(doc).OfClass(typeof(Conduit)).Cast<Conduit>().Where(e=>typeIds.Contains(e.GetTypeId())));
            var failed=new List<ElementId>();foreach(var e in conduits.GroupBy(e=>e.Id).Select(g=>g.First()))try{ConduitMaterials.Paint(doc,e,definition);}catch(Autodesk.Revit.Exceptions.RegenerationFailedException){throw;}catch(AccessViolationException){throw;}catch(Exception ex){Errors.Describe(ex);failed.Add(e.Id);}
            if(failed.Count>0){var message=new FailureMessage(WarningId);message.SetFailingElements(failed);doc.PostFailure(message);}
        }
        internal static ConduitMaterialUpdater Register(UIControlledApplication app)
        {
            FailureDefinition.CreateFailureDefinition(WarningId,FailureSeverity.Warning,"OQN Tools: não foi possível atualizar o material de alguns conduítes. Use Material dos conduítes para conferir os itens.");
            var updater=new ConduitMaterialUpdater(app.ActiveAddInId);UpdaterRegistry.RegisterUpdater(updater,true);
            var filter=new ElementCategoryFilter(BuiltInCategory.OST_Conduit);
            UpdaterRegistry.AddTrigger(updater.id,filter,Element.GetChangeTypeElementAddition());
            UpdaterRegistry.AddTrigger(updater.id,filter,Element.GetChangeTypeAny());return updater;
        }
    }
}
