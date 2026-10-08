using Autodesk.Revit.Attributes;
namespace OQNTools
{
    [Transaction(TransactionMode.Manual)] public sealed class ExportGlb : Command
    {
        protected override void Run(Context c)
        {
            // Keep the existing GLB call and exception propagation; diagnostics are opt-in.
            if(CompatibilityDiagnostics.Extended)CompatibilityDiagnostics.TryWrite(c.App.Application.VersionNumber,"glb-before");
            try{OQNTools.Everse.GlbEntry.Open(c.App);}
            catch(System.Exception ex){if(CompatibilityDiagnostics.Extended)CompatibilityDiagnostics.TryWrite(c.App.Application.VersionNumber,"glb-failed",ex);throw;}
            finally{if(CompatibilityDiagnostics.Extended)CompatibilityDiagnostics.TryWrite(c.App.Application.VersionNumber,"glb-after");}
        }
    }
}
