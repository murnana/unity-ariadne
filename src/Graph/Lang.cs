namespace UnityAriadne.Graph;

/// <summary>
/// Language of the human-readable text in search answers (relation meanings, notes, clue
/// labels, relaxation and error messages). Field names, asset names, type and ref_kind
/// names and enum-like values stay English in every language.
/// </summary>
public enum Lang
{
    En,
    Ja,
}

public static class LangExtensions
{
    /// <summary>"en" / "ja" (case-insensitive, region suffix ignored: "ja-JP"); null or blank → English.</summary>
    public static bool TryParse(string? value, out Lang lang)
    {
        lang = Lang.En;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        var code = value.Trim().Split('-', '_')[0].ToLowerInvariant();
        switch (code)
        {
            case "en":
                return true;
            case "ja":
                lang = Lang.Ja;
                return true;
            default:
                return false;
        }
    }

    /// <summary>Picks the text for <paramref name="lang"/>.</summary>
    public static string Pick(this Lang lang, string en, string ja) => lang == Lang.Ja ? ja : en;
}
