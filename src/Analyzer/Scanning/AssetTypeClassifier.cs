namespace UnityAriadne.Analyzer.Scanning;

/// <summary>
/// Determines the <c>asset_type</c> column value from an asset path and its .meta contents.
/// </summary>
/// <remarks>
/// Extension is the primary signal because Unity 6 omits the importer section from .meta
/// files with all-default settings. The importer type is only a fallback for unknown
/// extensions.
/// </remarks>
public static class AssetTypeClassifier
{
    private static readonly Dictionary<string, string> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["unity"] = "Scene",
        ["prefab"] = "Prefab",
        ["cs"] = "Script",
        ["asmdef"] = "AssemblyDefinition",
        ["asmref"] = "AssemblyDefinitionReference",
        ["dll"] = "Assembly",
        ["mat"] = "Material",
        ["shader"] = "Shader",
        ["shadergraph"] = "ShaderGraph",
        ["shadersubgraph"] = "ShaderSubGraph",
        ["cginc"] = "ShaderInclude",
        ["hlsl"] = "ShaderInclude",
        ["glslinc"] = "ShaderInclude",
        ["compute"] = "ComputeShader",
        ["vfx"] = "VisualEffect",
        ["vfxoperator"] = "VisualEffectSubgraph",
        ["vfxblock"] = "VisualEffectSubgraph",
        ["controller"] = "AnimatorController",
        ["overridecontroller"] = "AnimatorOverrideController",
        ["anim"] = "AnimationClip",
        ["mask"] = "AvatarMask",
        ["fbx"] = "Model",
        ["obj"] = "Model",
        ["blend"] = "Model",
        ["dae"] = "Model",
        ["3ds"] = "Model",
        ["mesh"] = "Mesh",
        ["st"] = "SpeedTree",
        ["st9"] = "SpeedTree",
        ["spm"] = "SpeedTree",
        ["png"] = "Texture",
        ["jpg"] = "Texture",
        ["jpeg"] = "Texture",
        ["tga"] = "Texture",
        ["tif"] = "Texture",
        ["tiff"] = "Texture",
        ["psd"] = "Texture",
        ["psb"] = "Texture",
        ["exr"] = "Texture",
        ["hdr"] = "Texture",
        ["bmp"] = "Texture",
        ["gif"] = "Texture",
        ["cubemap"] = "Cubemap",
        ["rendertexture"] = "RenderTexture",
        ["wav"] = "AudioClip",
        ["mp3"] = "AudioClip",
        ["ogg"] = "AudioClip",
        ["aif"] = "AudioClip",
        ["aiff"] = "AudioClip",
        ["flac"] = "AudioClip",
        ["mixer"] = "AudioMixer",
        ["ttf"] = "Font",
        ["otf"] = "Font",
        ["fontsettings"] = "Font",
        ["txt"] = "TextAsset",
        ["md"] = "TextAsset",
        ["json"] = "TextAsset",
        ["xml"] = "TextAsset",
        ["yaml"] = "TextAsset",
        ["yml"] = "TextAsset",
        ["csv"] = "TextAsset",
        ["html"] = "TextAsset",
        ["bytes"] = "TextAsset",
        ["physicmaterial"] = "PhysicsMaterial",
        ["physicsmaterial"] = "PhysicsMaterial",
        ["physicsmaterial2d"] = "PhysicsMaterial2D",
        ["spriteatlas"] = "SpriteAtlas",
        ["spriteatlasv2"] = "SpriteAtlas",
        ["terrainlayer"] = "TerrainLayer",
        ["lighting"] = "LightingSettings",
        ["giparams"] = "LightmapParameters",
        ["inputactions"] = "InputActions",
        ["uxml"] = "VisualTreeAsset",
        ["uss"] = "StyleSheet",
    };

    private static readonly Dictionary<string, string> ByImporter = new(StringComparer.Ordinal)
    {
        ["MonoImporter"] = "Script",
        ["PrefabImporter"] = "Prefab",
        ["TextureImporter"] = "Texture",
        ["IHVImageFormatImporter"] = "Texture",
        ["AudioImporter"] = "AudioClip",
        ["ModelImporter"] = "Model",
        ["ShaderImporter"] = "Shader",
        ["ShaderIncludeImporter"] = "ShaderInclude",
        ["ComputeShaderImporter"] = "ComputeShader",
        ["VisualEffectImporter"] = "VisualEffect",
        ["TrueTypeFontImporter"] = "Font",
        ["TextScriptImporter"] = "TextAsset",
        ["PluginImporter"] = "Assembly",
        ["SpeedTreeImporter"] = "SpeedTree",
        ["VideoClipImporter"] = "VideoClip",
    };

    // NativeFormatImporter mainObjectFileID values: 11400000 is a MonoBehaviour-derived
    // main object (= ScriptableObject); 15600000 is TerrainData.
    private const long ScriptableObjectMainFileId = 11400000;
    private const long TerrainDataMainFileId = 15600000;

    public static string Classify(string assetPath, bool isFolder, string? importerType, long? mainObjectFileId)
    {
        if (isFolder)
        {
            return "Folder";
        }

        var extension = Path.GetExtension(assetPath).TrimStart('.');
        if (extension.Equals("asset", StringComparison.OrdinalIgnoreCase))
        {
            return mainObjectFileId switch
            {
                null or 0 or ScriptableObjectMainFileId => "ScriptableObject",
                TerrainDataMainFileId => "TerrainData",
                _ => "SerializedAsset",
            };
        }

        if (extension.Length > 0 && ByExtension.TryGetValue(extension, out var byExtension))
        {
            return byExtension;
        }

        if (importerType is not null && ByImporter.TryGetValue(importerType, out var byImporter))
        {
            return byImporter;
        }

        return "Unknown";
    }
}
