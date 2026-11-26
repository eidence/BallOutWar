using UnityEditor;

namespace BOW.Editor
{
    /// <summary>
    /// Import settings for the Meshy character models in Assets/Resources/Models, so a new download needs no
    /// clicking: a Humanoid avatar (lets the game find and pose the hips, legs and arms by name, and lets any
    /// standard humanoid animation play on it), materials from the FBX's embedded textures, and no cameras,
    /// lights or animation clips (Meshy only ships a one-frame rest pose).
    /// </summary>
    public sealed class CharacterModelImport : AssetPostprocessor
    {
        private const string Folder = "Assets/Resources/Models/";

        /// <summary>Car paint textures: 1024 px is plenty for a car on a phone screen.</summary>
        private void OnPreprocessTexture()
        {
            bool car = assetPath.StartsWith("Assets/Resources/Cars/");
            bool character = assetPath.StartsWith(Folder);
            if (!car && !character) return;
            var importer = (TextureImporter)assetImporter;
            importer.maxTextureSize = 1024;
            importer.mipmapEnabled = true;
            // Loose character textures extracted from an FBX (e.g. bolt_normal.png) need the normal-map type.
            if (character && assetPath.EndsWith("_normal.png")) importer.textureType = TextureImporterType.NormalMap;
        }

        private void OnPreprocessModel()
        {
            if (!assetPath.StartsWith(Folder)) return;
            var importer = (ModelImporter)assetImporter;
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.useFileScale = true;
            importer.globalScale = 1f;
            importer.meshCompression = ModelImporterMeshCompression.Medium;
        }
    }
}
