using System.Text;

namespace HAMMOR.App.Search;

/// <summary>
/// How Search compares text: case-insensitive, and tolerant of the ways the
/// same Arabic word is commonly typed.
/// </summary>
/// <remarks>
/// Arabic folding removes tashkeel (harakat, tanween, shadda, sukun, dagger
/// alef) and tatweel, and treats أ إ آ ٱ as ا, ى as ي and ة as ه. "مُهِمَّة",
/// "مهمه" and "مهمة" therefore all match. Latin text is case-folded with the
/// invariant culture, so results do not change with the UI language.
/// </remarks>
public static class SearchText
{
    /// <summary>The query matched the start of the title.</summary>
    public const int TitleStart = 3;

    /// <summary>The query matched the start of a later word in the title.</summary>
    public const int TitleWord = 2;

    /// <summary>The query appears somewhere in the title.</summary>
    public const int TitleAnywhere = 1;

    /// <summary>The query appears only in the detail line.</summary>
    public const int DetailOnly = 0;

    /// <summary>No match.</summary>
    public const int NoMatch = -1;

    /// <summary>Text folded for comparison. Null becomes empty.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var raw in text.Normalize(NormalizationForm.FormKC))
        {
            var c = raw switch
            {
                'أ' or 'إ' or 'آ' or 'ٱ' => 'ا',
                'ى' => 'ي',
                'ة' => 'ه',
                _ => raw,
            };

            if (IsArabicMark(c))
            {
                continue;
            }

            builder.Append(char.IsWhiteSpace(c) ? ' ' : char.ToLowerInvariant(c));
        }

        return CollapseSpaces(builder.ToString()).Trim();
    }

    /// <summary>
    /// How well a folded query matches a title and an optional detail line:
    /// one of the constants above, higher is better.
    /// </summary>
    public static int Score(string normalizedQuery, string? title, string? detail = null)
    {
        if (normalizedQuery.Length == 0)
        {
            return NoMatch;
        }

        var foldedTitle = Normalize(title);
        if (foldedTitle.StartsWith(normalizedQuery, StringComparison.Ordinal))
        {
            return TitleStart;
        }

        if (foldedTitle.Contains(" " + normalizedQuery, StringComparison.Ordinal))
        {
            return TitleWord;
        }

        if (foldedTitle.Contains(normalizedQuery, StringComparison.Ordinal))
        {
            return TitleAnywhere;
        }

        return Normalize(detail).Contains(normalizedQuery, StringComparison.Ordinal)
            ? DetailOnly
            : NoMatch;
    }

    /// <summary>A single line of at most <paramref name="maxLength"/> characters.</summary>
    public static string Snippet(string? text, int maxLength = 140)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var line = CollapseSpaces(text.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ')).Trim();
        return line.Length <= maxLength ? line : line[..(maxLength - 1)].TrimEnd() + "…";
    }

    // Tashkeel U+064B–U+065F, the dagger alef U+0670 and tatweel U+0640.
    private static bool IsArabicMark(char c) =>
        c is >= 'ً' and <= 'ٟ' or 'ٰ' or 'ـ';

    private static string CollapseSpaces(string text)
    {
        var builder = new StringBuilder(text.Length);
        var previousSpace = false;
        foreach (var c in text)
        {
            if (c == ' ')
            {
                if (!previousSpace)
                {
                    builder.Append(c);
                }

                previousSpace = true;
            }
            else
            {
                builder.Append(c);
                previousSpace = false;
            }
        }

        return builder.ToString();
    }
}
