using System;
using System.Collections.Generic;
using System.IO;
namespace OQNTools.Everse.Export
{
    // Commit a complete export only after geometry and compression succeed.
    public static class OutputCommit
    {
        public static void Run(string stage,string destination)
        {
            var files=Directory.GetFiles(stage);Array.Sort(files,StringComparer.Ordinal);if(files.Length==0)throw new IOException("A exportação não gerou arquivos.");
            var backups=new Dictionary<string,string>();var moved=new List<string>();
            try {
                foreach(var file in files){string target=Path.Combine(destination,Path.GetFileName(file));
                    if(File.Exists(target)){string backup=Path.Combine(stage,Guid.NewGuid().ToString("N")+".backup");File.Move(target,backup);backups[target]=backup;}
                    File.Move(file,target);moved.Add(target);
                }
            }catch{
                foreach(var target in moved)File.Delete(target);
                foreach(var entry in backups)File.Move(entry.Value,entry.Key);
                throw;
            }
            foreach(var backup in backups.Values)File.Delete(backup);
        }
    }
}
