using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Microsoft.Win32;

namespace OQNTools
{
    [Transaction(TransactionMode.Manual)] public sealed class ExportSchedules : Command
    {
        protected override void Run(Context c)
        {
            var schedules=c.All<ViewSchedule>().Where(v=>!v.IsTemplate&&!v.IsTitleblockRevisionSchedule).OrderBy(v=>v.Name).ToList();
            var options=ExportOptions.Show(c,schedules);if(options==null)return;var sheets=new List<SheetData>();var failures=new List<string>();
            foreach(var view in options.Schedules){try{sheets.Add(ReadSchedule(view,options.Style.Numbers));}catch(Exception ex){failures.Add(view.Name+": "+Errors.Describe(ex));}}
            if(sheets.Count==0)throw new ArgumentException("Nenhuma tabela pôde ser lida. "+string.Join("; ",failures));
            if(failures.Count>0)Form.ShowRows(c.App,"Tabelas não exportadas",failures.Select(x=>new{Motivo=x}),"As demais serão exportadas.",true);
            if(options.Combined){var all=new SheetData{Name="Quantitativos",HeaderRows=0};foreach(var sheet in sheets){int offset=all.Rows.Count;all.Rows.AddRange(sheet.Rows);foreach(var h in sheet.Headers)all.Headers.Add(h+offset);foreach(var t in sheet.Titles)all.Titles[t.Key+offset]=t.Value;foreach(var n in sheet.Numbers){var parts=n.Key.Split(':');all.Numbers[SheetData.Key(int.Parse(parts[0])+offset,int.Parse(parts[1]))]=n.Value;}all.Rows.Add(new List<string>());all.Rows.Add(new List<string>());}sheets=new List<SheetData>{all};}
            try{Xlsx.Write(options.Path,sheets,options.Style);}catch(Exception ex){throw new ArgumentException(Errors.Describe(ex));}
            if(options.Open){try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(options.Path){UseShellExecute=true});}catch{c.Done("Arquivo exportado, mas não foi possível abrir o aplicativo padrão: "+options.Path);return;}}

        }
        static SheetData ReadSchedule(ViewSchedule view,bool numbers)
        {
            var sheet=new SheetData{Name=view.Name,HeaderRows=0};var data=view.GetTableData();var body=data.GetSectionData(SectionType.Body);
            int cols=body.NumberOfColumns;sheet.Titles[0]=Math.Max(1,cols);sheet.Rows.Add(new List<string>{view.Name});
            var fields=view.Definition.GetFieldOrder().Select(id=>view.Definition.GetField(id)).Where(f=>!f.IsHidden).ToList();
            // The title is explicit; extra custom header rows are retained as text.
            var header=data.GetSectionData(SectionType.Header);
            for(int r=header.FirstRowNumber;r<=header.LastRowNumber;r++){
                var row=new List<string>();for(int col=header.FirstColumnNumber;col<=header.LastColumnNumber;col++)row.Add(view.GetCellText(SectionType.Header,r,col));
                if(row.All(string.IsNullOrWhiteSpace)||row.Count(x=>!string.IsNullOrWhiteSpace(x))==1&&row.Any(x=>x==view.Name))continue;
                sheet.Headers.Add(sheet.Rows.Count);sheet.Rows.Add(row);
            }
            for(int r=body.FirstRowNumber;r<=body.LastRowNumber;r++){
                int target=sheet.Rows.Count;var row=new List<string>();bool heading=view.Definition.ShowHeaders&&r==body.FirstRowNumber;if(heading)sheet.Headers.Add(target);
                for(int col=body.FirstColumnNumber;col<=body.LastColumnNumber;col++){
                    string value=view.GetCellText(SectionType.Body,r,col);row.Add(value);int index=col-body.FirstColumnNumber;
                    if(numbers&&!heading&&fields.Count==cols&&index<fields.Count){var field=fields[index];bool numeric=Numeric(field);
                        if(numeric){var number=Xlsx.ParseNumber(value,System.Globalization.CultureInfo.CurrentCulture);if(number!=null)sheet.Numbers[SheetData.Key(target,index)]=number;}
                    }
                }sheet.Rows.Add(row);
            }return sheet;
        }
        static bool Numeric(ScheduleField field)
        {
            try{var spec=field.GetSpecTypeId();return field.FieldType==ScheduleFieldType.Count||spec==SpecTypeId.Number||spec==SpecTypeId.Int.Integer||(!string.IsNullOrEmpty(spec.TypeId)&&UnitUtils.IsMeasurableSpec(spec));}
            catch(Autodesk.Revit.Exceptions.ArgumentException){return false;}
        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class TransferParameters : Command
    {
        protected override void Run(Context c)
        {
            if(c.Doc.IsFamilyDocument){TransferFamily(c);return;}
            // Opening the dialog must not inspect every parameter in the document.
            var f=c.ScopeForm("Transferir parâmetros","Informe os nomes exatos dos parâmetros. A busca é automática na instância e no tipo. Use I: ou T: para fixar o escopo, ou @GUID para distinguir parâmetros compartilhados homônimos.")
                .Text("from","Parâmetro origem").Text("to","Parâmetro destino")
                .Check("empty","Preencher somente destinos vazios",true).ActionLabel("Prévia da transferência");
            f.Validate=()=>{if(string.IsNullOrWhiteSpace(f.Get("from"))||string.IsNullOrWhiteSpace(f.Get("to")))throw new ArgumentException("Informe os parâmetros de origem e destino.");};
            if(!f.Run())return;
            Errors.Checkpoint("Transferir parâmetros: coletar escopo "+f.Pick<string>("scope"));
            var elements=c.Scope(f.Pick<string>("scope")).ToList();c.NeedSelection(elements);var changes=new List<Change>();
            foreach(var e in elements)
            {
                Errors.Checkpoint("Transferir parâmetros: ler elemento "+e.Id.OqnValue());
                var row=new Change{Id=e.Id.OqnValue(),Elemento=e.Name};changes.Add(row);
                try{var source=Params.Find(e,f.Get("from"));var target=Params.Find(e,f.Get("to"),true);if(source!=null&&target!=null&&Params.Identity(source)==Params.Identity(target))throw new ArgumentException("Origem e destino são o mesmo parâmetro.");Params.CheckWritable(target);row.Antes=Params.Text(target);row.Depois=Params.Text(source);row.Key=Params.Identity(target);
                    if(f.Yes("empty")&&!string.IsNullOrEmpty(row.Antes)){row.Estado="Ignorado: preenchido";continue;}
                    row.Apply=Params.Copy(source,target);row.Signature=target.StorageType==StorageType.String?row.Depois:Params.Fingerprint(source);row.Estado=target.Element is ElementType?"Alteração de TIPO":"Pronto";
                }catch(Exception ex){row.Estado="ERRO: "+ex.Message;}
            }
            Errors.Checkpoint("Transferir parâmetros: prévia");
            Changes.RejectConflicts(changes);if(!Form.Preview(c.App,"Transferir parâmetros",changes,"Confira os destinos. Alterações de tipo afetam todas as instâncias desse tipo.","Transferir"))return;Batch.Run(c,"Transferir parâmetros",changes);Form.ShowChangeIssues(c.App,"Transferência",changes);
        }
        void TransferFamily(Context c)
        {
            var fm=c.Doc.FamilyManager;var ps=fm.Parameters.Cast<FamilyParameter>().OrderBy(p=>p.Definition.Name).ToList();
            var f=new Form(c.App,"Transferir na família","Copia valores para cada tipo da família. Fórmulas no destino são preservadas e impedem a escrita.")
                .Select("from","Origem",ps.Select(p=>new Choice(p.Definition.Name,p))).Select("to","Destino",ps.Select(p=>new Choice(p.Definition.Name,p)));
            if(!f.Run())return;var a=f.Pick<FamilyParameter>("from");var b=f.Pick<FamilyParameter>("to");
            if(a.Id==b.Id)throw new ArgumentException("Origem e destino são iguais.");
            if(b.IsReadOnly||b.IsDeterminedByFormula)throw new ArgumentException("Destino é somente leitura ou possui fórmula.");
            if(a.StorageType!=b.StorageType||a.Definition.GetDataType()!=b.Definition.GetDataType())throw new ArgumentException("Selecione parâmetros com o mesmo tipo de dado.");
            var types=fm.Types.Cast<FamilyType>().ToList();var original=fm.CurrentType;
            if(!Form.Preview(c.App,"Transferir na família",types.Select(t=>new{Tipo=t.Name,Origem=a.Definition.Name,Destino=b.Definition.Name}),"Aplicar em todos os tipos desta família?"))return;
            c.Mutate("Transferir na família",()=>{try{foreach(var t in types){if(!t.HasValue(a))continue;fm.CurrentType=t;switch(a.StorageType){case StorageType.String:fm.Set(b,t.AsString(a)??"");break;case StorageType.Double:fm.Set(b,t.AsDouble(a).Value);break;case StorageType.Integer:fm.Set(b,t.AsInteger(a).Value);break;case StorageType.ElementId:fm.Set(b,t.AsElementId(a));break;}}}finally{if(original!=null)fm.CurrentType=original;}});

        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class NumberSchedule : Command
    {
        protected override void Run(Context c)
        {
            if(!(c.View is ViewSchedule schedule))throw new ArgumentException("Abra a tabela que deseja numerar.");
            if(schedule.Definition.IsMaterialTakeoff||schedule.Definition.IsKeySchedule)throw new ArgumentException("Esta versão numera tabelas de elementos; tabelas de materiais e tabelas-chave exigem outro mapeamento.");
            var f=new Form(c.App,"Numerar tabela","Digite os nomes dos parâmetros identificadores na ordem desejada, separados por ponto e vírgula. A leitura procura valor na instância e depois no tipo, automaticamente. Cada combinação recebe um código.")
                .Text("keys","Parâmetros de descrição (separados por ;)","Descrição")
                .Text("target","Parâmetro que receberá a numeração","Marca")
                .Text("prefix","Prefixo","F").Text("start","Número inicial","1").ActionLabel("Prévia da numeração");
            f.Validate=()=>{if(string.IsNullOrWhiteSpace(f.Get("keys"))||string.IsNullOrWhiteSpace(f.Get("target"))||f.Number("start")<0||f.Number("start")>int.MaxValue||f.Number("start")%1!=0)throw new ArgumentException("Informe os nomes dos parâmetros e um número inicial inteiro não negativo.");};
            if(!f.Run())return;
            var elements=c.Scope("Vista ativa").ToList();c.NeedSelection(elements);
            var keys=TextKeys.Parse(f.Get("keys"));
            if(keys.Count==0)throw new ArgumentException("Informe pelo menos um parâmetro de descrição.");
            string target=f.Get("target").Trim();
            var details=new Dictionary<long,string>();
            var rows=new List<Change>();var groups=new Dictionary<string,List<Change>>();var descriptions=new Dictionary<string,string>();
            foreach(var e in elements)
            {
                var row=new Change{Id=e.Id.OqnValue(),Elemento=e.Name};rows.Add(row);
                try
                {
                    List<string> values;
                    if(keys.Count==1){values=keys.Select(k=>Params.Text(Params.Find(e,k))).ToList();if(values.Any(v=>v==null))throw new ArgumentException("Campo de descrição ausente ou sem valor.");}
                    else{List<string> missing;values=NamedValues.Components(e,keys,out missing);details[row.Id]=string.Join(" | ",keys.Zip(values,(name,value)=>name+" = "+(value.Length==0?"[sem valor]":value)));}

                    var p=Params.Find(e,target,true);Params.CheckWritable(p);if(p.StorageType!=StorageType.String)throw new ArgumentException("O destino deve ser um parâmetro textual.");
                    row.Key=Params.Identity(p);row.Antes=Params.Text(p);row.Estado=p.Element is ElementType?"Alteração de TIPO":"Pronto";
                    var key=Logic.Composite(values);descriptions[key]=string.Join(" · ",values);if(!groups.ContainsKey(key))groups[key]=new List<Change>();groups[key].Add(row);
                    row.Apply=()=>{Params.SetText(p,row.Depois);};
                }catch(Exception ex){row.Estado="ERRO: "+ex.Message;}
            }
            long index=(long)f.Number("start");foreach(var key in groups.Keys.OrderBy(k=>descriptions[k],new NaturalComparer()).ThenBy(k=>k,StringComparer.Ordinal))
            {if(index>int.MaxValue)throw new ArgumentException("A sequência excederia o limite numérico; reduza o número inicial.");string code=f.Get("prefix")+index.ToString();foreach(var row in groups[key])row.Depois=code;index=checked(index+1);}
            Changes.RejectConflicts(rows);if(!Form.Preview(c.App,"Numerar · Prévia",rows.Select(row=>new{row.Id,row.Elemento,Composição=details.ContainsKey(row.Id)?details[row.Id]:"",row.Antes,row.Depois,row.Estado}),"Confira os códigos. Alterar parâmetros de tipo afeta todas as instâncias desse tipo. Conflitos são ignorados.","Numerar"))return;NumberingBatch.Run(c,rows);Form.ShowChangeIssues(c.App,"Numeração · Atenção",rows);
        }
    }
    [Transaction(TransactionMode.Manual)] public sealed class SumParameters : Command
    {
        protected override void Run(Context c)
        {
            var f=c.ScopeForm("Somar parâmetros","Soma somente valores numéricos do mesmo tipo de dado. Valores de tipo são contabilizados uma vez por elemento selecionado.").Text("name","Parâmetro","Comprimento");if(!f.Run())return;
            var elements=c.Scope(f.Pick<string>("scope")).ToList();c.NeedSelection(elements);var ps=elements.Select(e=>Params.Find(e,f.Get("name"))).Where(p=>p!=null&&p.HasValue).ToList();
            if(ps.Count==0)throw new ArgumentException("Nenhum valor encontrado.");var spec=ps[0].Definition.GetDataType();if(ps.Any(p=>p.Definition.GetDataType()!=spec||p.StorageType!=ps[0].StorageType))throw new ArgumentException("Parâmetros incompatíveis entre elementos.");
            string value;if(ps[0].StorageType==StorageType.Double)value=UnitFormatUtils.Format(c.Doc.GetUnits(),spec,ps.Sum(p=>p.AsDouble()),false);
            else if(ps[0].StorageType==StorageType.Integer)value=ps.Sum(p=>(long)p.AsInteger()).ToString();else throw new ArgumentException("O parâmetro precisa ser numérico.");
            string unit="Sem unidade (número)";
            if(ps[0].StorageType==StorageType.Double&&UnitUtils.IsMeasurableSpec(spec))
                unit=LabelUtils.GetLabelForUnit(c.Doc.GetUnits().GetFormatOptions(spec).GetUnitTypeId());
            c.Done("Soma: "+value+"\nUnidade: "+unit+"\nValores considerados: "+ps.Count+"\nSem valor: "+(elements.Count-ps.Count));
        }
    }
}
