using System.Text;

namespace GuidGraph.Graph;

/// <summary>
/// Spelling-insensitive substring matching for search clues (keywords, path filters).
/// Both sides are folded the same way before comparing:
/// <list type="bullet">
/// <item>Unicode NFKC — full-width and half-width forms become one (<c>ＢＯＳＳ</c> = <c>BOSS</c>,
/// <c>ﾎﾞｽ</c> = <c>ボス</c>), compatibility characters become their plain form (<c>①</c> = <c>1</c>);</item>
/// <item>lower case (invariant culture);</item>
/// <item>hiragana → katakana (<c>ぼす</c> = <c>ボス</c>).</item>
/// </list>
/// Plus one English plural rule: a keyword ending in <c>y</c> also matches <c>…ie</c>
/// (<c>enemy</c> finds <c>Enemies</c>; <c>boss</c> → <c>Bosses</c> already works as a substring).
/// No tokenizer or stemming: inflected Japanese (<c>攻撃した</c> vs <c>攻撃する</c>) and synonyms
/// are left to the caller, who should give the stem or several keywords.
/// </summary>
public static class TextMatch
{
    private const char FirstHiragana = 'ぁ';
    private const char LastHiragana = 'ゖ';
    private const int HiraganaToKatakana = 0x60;

    /// <summary>
    /// NFKC form of <paramref name="text"/>. <see cref="Fold"/> keeps its length, so an index
    /// found in the folded text points at the same place in this form (used for excerpts).
    /// </summary>
    public static string Display(string text) => text.Normalize(NormalizationForm.FormKC);

    /// <summary>The form both sides are compared in: NFKC, lower case, hiragana as katakana.</summary>
    public static string Fold(string text)
    {
        var chars = Display(text).ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            var c = char.ToLowerInvariant(chars[i]);
            if (c is >= FirstHiragana and <= LastHiragana)
            {
                c = (char)(c + HiraganaToKatakana);
            }

            chars[i] = c;
        }

        return new string(chars);
    }

    /// <summary>Index of folded <paramref name="foldedTerm"/> in folded <paramref name="foldedText"/>, or -1.</summary>
    public static int IndexOf(string foldedText, string foldedTerm)
    {
        var index = foldedText.IndexOf(foldedTerm, StringComparison.Ordinal);
        if (index >= 0 || foldedTerm.Length <= 2 || !foldedTerm.EndsWith('y'))
        {
            return index;
        }

        return foldedText.IndexOf(foldedTerm[..^1] + "ie", StringComparison.Ordinal);
    }

    /// <summary>Whether <paramref name="text"/> contains <paramref name="term"/> after folding both.</summary>
    public static bool Contains(string text, string term) => IndexOf(Fold(text), Fold(term)) >= 0;
}
