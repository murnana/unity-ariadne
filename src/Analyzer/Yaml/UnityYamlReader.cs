using System.Text;
using System.Text.RegularExpressions;
using YamlDotNet.RepresentationModel;

namespace UnityAriadne.Analyzer.Yaml;

/// <summary>
/// Streams a Unity YAML asset file document by document (Phase 1b).
/// </summary>
/// <remarks>
/// Unity YAML is not standard YAML, so the file cannot be fed to YamlDotNet whole:
/// the <c>%TAG !u!</c> directive, per-document anchors that repeat across documents,
/// and the invalid <c>stripped</c> keyword all break a naive parse. Instead the
/// document headers are handled line-based here, and only each document's body is
/// parsed with YamlDotNet. Bodies are buffered one document at a time, so memory use
/// is bounded by the largest document, not the file.
/// </remarks>
public static partial class UnityYamlReader
{
    // `--- !u!<classId> &<fileID>[ stripped]`. fileID can be negative; input may be CRLF,
    // but ReadLine already strips the terminator, so \s* only guards trailing blanks.
    [GeneratedRegex(@"^--- !u!(\d+) &(-?\d+)( stripped)?\s*$")]
    private static partial Regex DocumentHeader();

    public static IEnumerable<UnityYamlDocument> ReadFile(string path)
    {
        using var reader = new StreamReader(path);
        foreach (var document in ReadDocuments(reader))
        {
            yield return document;
        }
    }

    /// <summary>True when the file starts with a <c>%YAML</c> directive (binary assets and .shadergraph/.json don't).</summary>
    public static bool IsUnityYamlFile(string path)
    {
        using var reader = new StreamReader(path);
        Span<char> head = stackalloc char["%YAML".Length];
        return reader.ReadBlock(head) == head.Length && head is "%YAML";
    }

    public static IEnumerable<UnityYamlDocument> ReadDocuments(TextReader reader)
    {
        (int ClassId, long FileId, bool IsStripped)? header = null;
        var body = new StringBuilder();

        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith("--- ", StringComparison.Ordinal)
                && DocumentHeader().Match(line) is { Success: true } match)
            {
                if (header is { } previous)
                {
                    yield return CreateDocument(previous, body);
                    body.Clear();
                }

                header = (
                    int.Parse(match.Groups[1].ValueSpan),
                    long.Parse(match.Groups[2].ValueSpan),
                    match.Groups[3].Success);
                continue;
            }

            if (header is null)
            {
                // Before the first document only %YAML/%TAG directives are expected; skip them.
                continue;
            }

            body.AppendLine(line);
        }

        if (header is { } last)
        {
            yield return CreateDocument(last, body);
        }
    }

    private static UnityYamlDocument CreateDocument(
        (int ClassId, long FileId, bool IsStripped) header, StringBuilder body)
    {
        YamlNode? root = null;
        if (body.Length > 0)
        {
            var stream = new YamlStream();
            using var reader = new StringReader(body.ToString());
            stream.Load(reader);
            if (stream.Documents.Count > 0)
            {
                root = stream.Documents[0].RootNode;
            }
        }

        return new UnityYamlDocument
        {
            ClassId = header.ClassId,
            FileId = header.FileId,
            IsStripped = header.IsStripped,
            Root = root,
        };
    }
}
