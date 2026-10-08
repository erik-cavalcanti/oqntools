using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using OQNTools.Everse.Core;
using OQNTools.Everse.Windows.MainWindow;
using glTF.Manipulator.GenericSchema;

namespace OQNTools.Everse.Export
{
    internal class GlbFile
    {
        public static void Create(Preferences preferences, GLTFBinaryData binaryFileData, string json)
        {
            byte[] jsonChunk = GlbJsonInfo.Get(json);
            int lenggg = jsonChunk.Length;
            byte[] binChunk = GlbBinInfo.Get(binaryFileData);
            byte[] headerChunk = GlbHeaderInfo.Get(jsonChunk, binChunk);

            string fileDirectory = string.Concat(preferences.path, ".glb");
            byte[] exportArray = headerChunk.Concat(jsonChunk).Concat(binChunk).ToArray();

            File.WriteAllBytes(fileDirectory, exportArray);
        }
    }
}
