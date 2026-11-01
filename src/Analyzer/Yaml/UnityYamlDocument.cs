using YamlDotNet.RepresentationModel;

namespace UnityAriadne.Analyzer.Yaml;

/// <summary>
/// One document in a Unity YAML asset file: the <c>--- !u!&lt;classId&gt; &amp;&lt;fileID&gt;</c>
/// header plus its body parsed into a YamlDotNet node tree (Phase 1b).
/// </summary>
public sealed record UnityYamlDocument
{
    /// <summary>Unity class ID from the <c>!u!</c> tag (1 GameObject, 114 MonoBehaviour, 1001 PrefabInstance, ...).</summary>
    public required int ClassId { get; init; }

    /// <summary>The document anchor (<c>&amp;n</c>): the object's ID within the file. Can be negative.</summary>
    public required long FileId { get; init; }

    /// <summary>True when the header carried Unity's non-standard <c>stripped</c> keyword.</summary>
    public bool IsStripped { get; init; }

    /// <summary>Document root node; null when the body is empty.</summary>
    public YamlNode? Root { get; init; }

    /// <summary>
    /// Top-level key naming the Unity type (e.g. "GameObject"), when the root has the
    /// usual single-key mapping shape; null otherwise.
    /// </summary>
    public string? ClassName =>
        Root is YamlMappingNode { Children.Count: 1 } map
        && map.Children.First().Key is YamlScalarNode scalar
            ? scalar.Value
            : null;

    /// <summary>The property mapping under <see cref="ClassName"/>; null when the shape is unexpected.</summary>
    public YamlMappingNode? Properties =>
        Root is YamlMappingNode { Children.Count: 1 } map
            ? map.Children.First().Value as YamlMappingNode
            : null;

    /// <summary>Value of <c>m_Name</c>, when present.</summary>
    public string? Name
    {
        get
        {
            if (Properties is not { } properties)
            {
                return null;
            }

            foreach (var (key, value) in properties.Children)
            {
                if (key is YamlScalarNode { Value: "m_Name" })
                {
                    return (value as YamlScalarNode)?.Value;
                }
            }

            return null;
        }
    }
}
