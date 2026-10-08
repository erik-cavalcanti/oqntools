using System;
using System.Collections.Generic;
namespace OQNTools
{
    // The transaction callback MUST roll back on failure before throwing.
    // Retrying single items preserves isolation without paying one commit per item normally.
    public static class ChunkedWrites
    {
        public static void Run<T>(IList<T> items,int size,Action<IList<T>> commit,Func<Exception,bool> fatal,Action<T,Exception> failed)
        {
            if(size<1)throw new ArgumentException("Tamanho de lote inválido.");
            for(int offset=0;offset<items.Count;offset+=size)
            {
                var chunk=new List<T>();for(int i=offset;i<Math.Min(items.Count,offset+size);i++)chunk.Add(items[i]);
                try{commit(chunk);}
                catch(Exception ex)
                {
                    if(fatal(ex))throw;
                    if(chunk.Count==1){failed(chunk[0],ex);continue;}
                    foreach(var item in chunk)
                    {
                        try{commit(new[]{item});}
                        catch(Exception single){if(fatal(single))throw;failed(item,single);}
                    }
                }
            }
        }
    }
}
