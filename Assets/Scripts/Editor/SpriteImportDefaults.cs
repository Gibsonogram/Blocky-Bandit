using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;

// Applies default import settings to every newly imported sprite texture: 32 pixels per
// unit, point (no) filtering, no compression, and an automatic 32x32 grid slice.
// Only affects textures on their first import — re-importing an already-configured or
// manually-sliced texture leaves your edits alone.
public class SpriteImportDefaults : AssetPostprocessor
{
    private const float PixelsPerUnit = 32f;
    private const int CellSize = 32;

    void OnPreprocessTexture()
    {
        TextureImporter importer = (TextureImporter)assetImporter;

        // Only touch sprites, and only on first import (no prior settings applied yet).
        if (importer.textureType != TextureImporterType.Sprite)
            return;
        if (importer.importSettingsMissing == false)
            return;

        importer.spritePixelsPerUnit = PixelsPerUnit;
        importer.filterMode = FilterMode.Point;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.mipmapEnabled = false;
        importer.spriteImportMode = SpriteImportMode.Multiple;
    }

    void OnPostprocessTexture(Texture2D texture)
    {
        TextureImporter importer = (TextureImporter)assetImporter;
        if (importer.textureType != TextureImporterType.Sprite)
            return;
        if (importer.spriteImportMode != SpriteImportMode.Multiple)
            return;

        SpriteDataProviderFactories factory = new SpriteDataProviderFactories();
        factory.Init();
        ISpriteEditorDataProvider dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
        dataProvider.InitSpriteEditorDataProvider();

        // Skip if this texture already has sprite metadata (already sliced, or hand-edited).
        SpriteRect[] existingRects = dataProvider.GetSpriteRects();
        if (existingRects != null && existingRects.Length > 0)
            return;

        int columns = Mathf.Max(1, texture.width / CellSize);
        int rows = Mathf.Max(1, texture.height / CellSize);
        string baseName = Path.GetFileNameWithoutExtension(importer.assetPath);

        List<SpriteRect> spriteRects = new List<SpriteRect>();
        List<SpriteNameFileIdPair> nameFileIds = new List<SpriteNameFileIdPair>();

        int index = 0;
        for (int row = rows - 1; row >= 0; row--)
        {
            for (int col = 0; col < columns; col++)
            {
                SpriteRect spriteRect = new SpriteRect
                {
                    name = $"{baseName}_{index}",
                    rect = new Rect(col * CellSize, row * CellSize, CellSize, CellSize),
                    alignment = SpriteAlignment.Center,
                    pivot = new Vector2(0.5f, 0.5f),
                    border = Vector4.zero,
                    spriteID = GUID.Generate()
                };

                spriteRects.Add(spriteRect);
                nameFileIds.Add(new SpriteNameFileIdPair(spriteRect.name, spriteRect.spriteID));
                index++;
            }
        }

        dataProvider.SetSpriteRects(spriteRects.ToArray());

        ISpriteNameFileIdDataProvider nameFileIdProvider = dataProvider.GetDataProvider<ISpriteNameFileIdDataProvider>();
        nameFileIdProvider.SetNameFileIdPairs(nameFileIds);

        dataProvider.Apply();
    }
}
