using Autodesk.Revit.DB;
using OQNTools.Everse.Core;
using OQNTools.Everse.EportUtils;
using OQNTools.Everse.Export;
using OQNTools.Everse.Model;
using OQNTools.Everse.Transform;
using OQNTools.Everse.Utils;
using OQNTools.Everse.Windows.MainWindow;
using glTF.Manipulator.Schema;
using glTF.Manipulator.Utils;
using OQNTools.Everse.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Transform = Autodesk.Revit.DB.Transform;

namespace OQNTools.Everse.EportUtils
{
    public static class ElementValidations
    {
        public static bool ShouldSkipElement(Element currentElement, Autodesk.Revit.DB.View currentView,
            Document currentDocument, Preferences preferences, IndexedDictionary<Node> nodes)
        {
            if (currentElement == null)
            {
                return true;
            }

            bool isHiddenOrLocked = !Util.CanBeLockOrHidden(currentElement, currentView, currentDocument.IsFamilyDocument);
            bool isLevelToSkip = currentElement is Level && !preferences.levels;
            bool isAlreadyProcessed = nodes.Contains(currentElement.UniqueId);

            if (isHiddenOrLocked || isLevelToSkip || isAlreadyProcessed)
            {
                return true;
            }

            return false;
        }

        public static bool ShouldOmitElement(Element currentElement, 
            IndexedDictionary<VertexLookupIntObject> currentVertices, 
            Autodesk.Revit.DB.View currentView, Document currentDocument, ElementId elemId)
        {
            if (currentElement == null)
                return true;

            #if REVIT2026 || REVIT2027
            if (currentElement.Id.OqnValue() != elemId.OqnValue())
                return true;
            #else
            if (currentElement.Id.OqnValue() != elemId.OqnValue())
                return true;
            #endif


            if (currentVertices == null || !currentVertices.List.Any())
                return true;

            if (!Util.CanBeLockOrHidden(currentElement, currentView, currentDocument.IsFamilyDocument) ||
                currentElement is RevitLinkInstance)
                return true;

            return false;
        }
    }
}
