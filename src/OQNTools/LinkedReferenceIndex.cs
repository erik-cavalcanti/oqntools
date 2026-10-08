using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
namespace OQNTools
{
    // Classification only: stable representations are never rewritten or used to
    // synthesize references. Original native references remain authoritative.
    public sealed class LinkedReferenceIndex
    {
        readonly Document doc;readonly Dictionary<long,string> ids=new Dictionary<long,string>();
        readonly Dictionary<string,string> uids=new Dictionary<string,string>(StringComparer.Ordinal);
        public LinkedReferenceIndex(Document document,bool includeImports)
        {
            doc=document;
            foreach(var id in ExternalFileUtils.GetAllExternalFileReferences(doc)){
                var file=ExternalFileUtils.GetExternalFileReference(doc,id);
                if(file.ExternalFileReferenceType==ExternalFileReferenceType.CADLink)Add(doc.GetElement(id),"CAD vinculado");
            }
            using(var collector=new FilteredElementCollector(doc))foreach(ImportInstance import in collector.OfClass(typeof(ImportInstance))){
                if(!import.IsLinked&&!includeImports)continue;
                string kind=import.IsLinked?"CAD vinculado":"CAD importado";Add(import,kind);Add(doc.GetElement(import.GetTypeId()),kind);
            }
            using(var collector=new FilteredElementCollector(doc))foreach(RevitLinkInstance link in collector.OfClass(typeof(RevitLinkInstance)))Add(link,"RVT vinculado");
        }
        void Add(Element e,string kind){if(e==null)return;ids[e.Id.OqnValue()]=kind;uids[e.UniqueId]=kind;}
        public string Kind(Reference r)
        {
            if(r==null)return null;
            if(ids.TryGetValue(r.ElementId.OqnValue(),out var kind))return kind;
            if(r.LinkedElementId!=ElementId.InvalidElementId&&r.LinkedElementId.OqnValue()>0)return "RVT vinculado";
            var owner=doc.GetElement(r.ElementId);
            if(owner!=null&&ids.TryGetValue(owner.GetTypeId().OqnValue(),out kind))return kind;
            // Supplementary match of exact known UniqueId tokens, without depending
            // on token positions or treating the internal serialization as a contract.
            return ReferenceTokens.Match(r.ConvertToStableRepresentation(doc),uids);
        }
    }
}
