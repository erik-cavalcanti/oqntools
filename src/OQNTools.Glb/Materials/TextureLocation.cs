using System;
using System.Collections.Generic;
using System.IO;
using OQNTools.Everse.Utils;

namespace OQNTools.Everse.Materials
{
    public static class TextureLocation
    {
        private const string AUTODESK_TEXTURES = @"Autodesk Shared\Materials\Textures\";

        public static List<string> GetPaths()
        {
            var paths = new List<string>
            {
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonProgramFiles),
                    AUTODESK_TEXTURES)
            };

            var externalPaths = RevitIniReader.GetAdditionalRenderAppearancePaths();
            if (externalPaths?.Count > 0)
            {
                paths.AddRange(externalPaths);
            }

            return paths;
        }
    }
}
