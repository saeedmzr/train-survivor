using UnityEditor;
using UnityEngine;

namespace TrainSurvivor.EditorTools
{
    /// <summary>
    /// The generated atlases must import as plain, clamp-wrapped, high-quality textures:
    /// they hold every billboard and HUD sprite, so compression artefacts show up as
    /// fringes on the zombies and gold trim.
    /// </summary>
    public sealed class ArtTextureImport : AssetPostprocessor
    {
        /// <summary>Enemy card art cut from art-work/ → real Sprite assets for prefabs/UGUI.</summary>
        void OnPreprocessSprite()
        {
            if (!assetPath.Contains("/Art/Sprites/")) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteMeshType = SpriteMeshType.FullRect;
            settings.spriteAlignment = (int)SpriteAlignment.BottomCenter;
            settings.spritePixelsPerUnit = 100f;
            settings.spriteGenerateFallbackPhysicsShape = false;
            importer.SetTextureSettings(settings);
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.maxTextureSize = 1024;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }

        void OnPreprocessTexture()
        {
            if (assetPath.Contains("/Art/Sprites/"))
            {
                OnPreprocessSprite();
                return;
            }
            if (!assetPath.Contains("/Art/Resources/Art/")) return;

            var importer = (TextureImporter)assetImporter;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.anisoLevel = 0;
            importer.maxTextureSize = 2048;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.textureCompression = TextureImporterCompression.CompressedHQ;
        }
    }
}
