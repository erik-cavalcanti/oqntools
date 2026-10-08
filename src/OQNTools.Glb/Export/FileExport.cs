using Autodesk.Revit.DB;
using OQNTools.Everse.Core;
using OQNTools.Everse.Model;
using OQNTools.Everse.Windows.MainWindow;
using glTF.Manipulator.GenericSchema;
using glTF.Manipulator.Schema;
using glTF.Manipulator.Utils;
using OQNTools.Everse.UI;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Buffer = glTF.Manipulator.Schema.Buffer;
using Material = glTF.Manipulator.Schema.Material;

namespace OQNTools.Everse.Export
{
    public static class FileExport
    {
        const string BIN = ".bin";
        const string GLTF = ".gltf";

        public static void Run(
            Preferences preferences,
            List<BufferView> bufferViews,
            List<Buffer> buffers,
            GLTFBinaryData binaryFileData, 
            List<Scene> scenes,
            IndexedDictionary<Node> nodes,
            IndexedDictionary<glTF.Manipulator.Schema.Mesh> meshes,
            IndexedDictionary<BaseMaterial> materials,
            List<Accessor> accessors,
            List<Texture> textures,
            List<glTFImage> images)
        {
            if (preferences.format == FormatEnum.gltf)
            {
                BufferConfig.Run(bufferViews, buffers, preferences);
                string fileDirectory = string.Concat(preferences.path, BIN);
                BinFile.Create(fileDirectory, binaryFileData);

                string gltfJson = GltfJson.Get(scenes, nodes.List, meshes.List, materials.List, buffers,
                bufferViews, accessors, textures, images, preferences);

                string gltfName = string.Concat(preferences.path, GLTF);
                var utf8WithoutBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
                File.WriteAllText(gltfName, gltfJson, utf8WithoutBom);
            }
            else
            {
                BufferConfig.Run(bufferViews, buffers, preferences);

                string gltfJson = GltfJson.Get(scenes, nodes.List, meshes.List, materials.List, buffers,
                bufferViews, accessors, textures, images, preferences);

                GlbFile.Create(preferences, binaryFileData, gltfJson);
            }
        }
    }
}
