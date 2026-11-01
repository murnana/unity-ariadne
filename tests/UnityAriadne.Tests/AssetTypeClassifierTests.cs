using UnityAriadne.Analyzer.Scanning;

namespace UnityAriadne.Tests;

public class AssetTypeClassifierTests
{
    [Theory]
    [InlineData("Assets/Scenes/Demo.unity", "Scene")]
    [InlineData("Assets/Prefabs/House.prefab", "Prefab")]
    [InlineData("Assets/Scripts/RuntimeLoader.cs", "Script")]
    [InlineData("Assets/Terrain/URPTerrain_MAT.mat", "Material")]
    [InlineData("Assets/Art/Meshes/Composition_4А.fbx", "Model")]
    [InlineData("Assets/Art/Meshes/Model.FBX", "Model")]
    [InlineData("Assets/Art/Textures/rock.tga", "Texture")]
    [InlineData("Assets/Audio/wind.wav", "AudioClip")]
    [InlineData("Assets/VFX/Fire.vfx", "VisualEffect")]
    [InlineData("Assets/Shaders/Water.shadergraph", "ShaderGraph")]
    [InlineData("Assets/Shaders/Depth.shadersubgraph", "ShaderSubGraph")]
    [InlineData("Assets/Anim/Player.controller", "AnimatorController")]
    [InlineData("Assets/Anim/Walk.anim", "AnimationClip")]
    [InlineData("Assets/Trees/Oak.st9", "SpeedTree")]
    [InlineData("Assets/Terrain/Grass.terrainlayer", "TerrainLayer")]
    [InlineData("Assets/Settings/Controls.inputactions", "InputActions")]
    [InlineData("Assets/readme.txt", "TextAsset")]
    public void KnownExtensions_ClassifyByExtension(string path, string expected)
    {
        Assert.Equal(expected, AssetTypeClassifier.Classify(path, isFolder: false, importerType: null, mainObjectFileId: null));
    }

    [Fact]
    public void Folder_WinsOverExtensionLookingName()
    {
        // Folder names may contain dots ("Ver 1.3"), so the folder flag must be
        // checked before any extension mapping.
        Assert.Equal("Folder", AssetTypeClassifier.Classify(
            "Assets/Samples/Shader Graph/17.0.3", isFolder: true, importerType: null, mainObjectFileId: null));
    }

    [Theory]
    [InlineData(null, "ScriptableObject")]
    [InlineData(0L, "ScriptableObject")]
    [InlineData(11400000L, "ScriptableObject")]
    [InlineData(15600000L, "TerrainData")]
    [InlineData(6418180475301049370L, "SerializedAsset")]
    public void DotAsset_ClassifiedByMainObjectFileId(long? mainObjectFileId, string expected)
    {
        Assert.Equal(expected, AssetTypeClassifier.Classify(
            "Assets/Data/Something.asset", isFolder: false, importerType: "NativeFormatImporter", mainObjectFileId: mainObjectFileId));
    }

    [Fact]
    public void UnknownExtension_FallsBackToImporter()
    {
        Assert.Equal("Script", AssetTypeClassifier.Classify(
            "Assets/Scripts/Weird.custom", isFolder: false, importerType: "MonoImporter", mainObjectFileId: null));
    }

    [Fact]
    public void UnknownExtensionAndImporter_IsUnknown()
    {
        Assert.Equal("Unknown", AssetTypeClassifier.Classify(
            "Assets/Data/blob.xyz", isFolder: false, importerType: null, mainObjectFileId: null));
    }
}
