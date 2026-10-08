using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
#if !REVIT2022 && !REVIT2023 && !REVIT2024
using System.Runtime.Loader;
#endif

namespace OQNTools
{
    // Read-only snapshots. No resolver, loading handler, assembly loading or unload operation.
    internal static class CompatibilityDiagnostics
    {
        static bool Relevant(string name)=>name.StartsWith("OQNTools",StringComparison.Ordinal)||
            name.StartsWith("DocumentFormat.OpenXml",StringComparison.Ordinal)||name=="Newtonsoft.Json"||
            name.StartsWith("Microsoft.Extensions.",StringComparison.Ordinal)||name.StartsWith("Microsoft.Xaml.Behaviors",StringComparison.Ordinal)||
            name.StartsWith("MahApps",StringComparison.Ordinal)||name.StartsWith("CommunityToolkit",StringComparison.Ordinal)||
            name.StartsWith("ClosedXML",StringComparison.Ordinal)||name.StartsWith("glTF",StringComparison.OrdinalIgnoreCase)||
            name.StartsWith("Draco",StringComparison.OrdinalIgnoreCase)||name=="MeshOpt"||name=="System.Text.Json"||
            name=="System.Drawing.Common"||name=="System.Configuration.ConfigurationManager";
        static string Context(Assembly assembly)
        {
#if REVIT2022 || REVIT2023 || REVIT2024
            return "AppDomain: "+AppDomain.CurrentDomain.FriendlyName;
#else
            var context=AssemblyLoadContext.GetLoadContext(assembly);
            return context==null?"unknown":(context.Name??"unnamed")+" #"+context.GetHashCode();
#endif
        }
        internal static string Snapshot(string revit,string stage,Exception error=null)
        {
            var text=new StringBuilder();var own=typeof(CompatibilityDiagnostics).Assembly;
            text.AppendLine("UTC: "+DateTime.UtcNow.ToString("o"));text.AppendLine("Stage: "+stage);
            text.AppendLine("Revit: "+revit);text.AppendLine("OQN: "+own.GetName().Version);
            text.AppendLine("CLR: "+Environment.Version);
            text.AppendLine("Target: "+own.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()?.FrameworkName);
            text.AppendLine("Main: "+own.FullName);text.AppendLine("Path: "+own.Location);text.AppendLine("Context: "+Context(own));
            var assemblies=AppDomain.CurrentDomain.GetAssemblies().Where(a=>Relevant(a.GetName().Name)).ToArray();
            foreach(var a in assemblies.OrderBy(a=>a.GetName().Name,StringComparer.Ordinal))
            {
                try{text.AppendLine(a.FullName+" | "+(a.IsDynamic?"<dynamic>":a.Location)+" | "+Context(a));}
                catch(Exception ex){text.AppendLine("Metadata unavailable: "+ex.GetType().Name);}
            }
            foreach(var group in assemblies.GroupBy(a=>a.GetName().Name).Where(g=>g.Count()>1))
                text.AppendLine("MULTIPLE INSTANCES (not proof of conflict): "+group.Key+" count="+group.Count());
            text.AppendLine("Not listed = not loaded at this snapshot; no dependency was loaded by this diagnostic.");
            if(error!=null)text.AppendLine(error.ToString());
            return text.ToString();
        }
        internal static bool TryWrite(string revit,string stage,Exception error=null,string diagnosticDirectory=null)
        {
            try
            {
                string dir=diagnosticDirectory??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"OQN Tools","Diagnostics",revit);
                Directory.CreateDirectory(dir);
                string path=Path.Combine(dir,"session-"+System.Diagnostics.Process.GetCurrentProcess().Id+"-"+DateTime.UtcNow.ToString("yyyyMMdd")+".log");
                File.AppendAllText(path,Snapshot(revit,stage,error)+Environment.NewLine,new UTF8Encoding(false));return true;
            }
            catch(Exception){return false;} // Diagnostic failure must never prevent startup/export.
        }
        internal static bool Extended=>Environment.GetEnvironmentVariable("OQN_TOOLS_DIAGNOSTICS")=="1";
    }
}
