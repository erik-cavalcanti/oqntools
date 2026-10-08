using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
namespace OQNTools
{
    public static class NumberingBatch
    {
        public static void Run(Context c,IEnumerable<Change> rows)
        {
            // Called AFTER conflict detection and preview: unchanged rows must still
            // participate in detecting competing values for a shared type destination.
            var pending=new List<Change>();
            foreach(var row in rows.Where(r=>r.Apply!=null))
            {
                if(string.Equals(row.Antes,row.Depois,StringComparison.Ordinal)){row.Estado="Concluído";continue;}
                pending.Add(row);
            }
            if(pending.Count==0)return;
            using(var group=new TransactionGroup(c.Doc,"OQN · Numerar"))
            {
                group.Start();
                ChunkedWrites.Run(pending,100,chunk=>
                {
                    Errors.Checkpoint("Numerar: lote de "+chunk.Count+" destinos; primeiro elemento "+chunk[0].Id);
                    var warnings=new List<string>();
                    c.Mutate("Numerar",()=>{foreach(var row in chunk)row.Apply();},warnings);
                    // Only publish success after commit. Failed chunks leave no success markers.
                    foreach(var row in chunk)row.Estado="Concluído";
                    if(warnings.Count>0)chunk[0].Estado="Concluído · Avisos do lote: "+string.Join("; ",warnings.Distinct());
                },ex=>ex is Autodesk.Revit.Exceptions.RegenerationFailedException,
                  (row,ex)=>row.Estado=Errors.Describe(ex));
                group.Assimilate();
            }
        }
    }
}
