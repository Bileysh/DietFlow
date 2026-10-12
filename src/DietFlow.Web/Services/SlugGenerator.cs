using System.Text;

namespace DietFlow.Web.Services;


public static class SlugGenerator
{
    private static readonly Dictionary<char, string> Map = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "h", ['ґ'] = "g", ['д'] = "d", ['е'] = "e",
        ['є'] = "ie", ['ж'] = "zh", ['з'] = "z", ['и'] = "y", ['і'] = "i", ['ї'] = "i", ['й'] = "i",
        ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r",
        ['с'] = "s", ['т'] = "t", ['у'] = "u", ['ф'] = "f", ['х'] = "kh", ['ц'] = "ts", ['ч'] = "ch",
        ['ш'] = "sh", ['щ'] = "shch", ['ь'] = "", ['ю'] = "iu", ['я'] = "ia", ['\''] = "", ['’'] = "",
        ['ё'] = "io", ['ы'] = "y", ['э'] = "e", ['ъ'] = "",
    };

    private static readonly Dictionary<char, string> WordStart = new()
    {
        ['є'] = "ye", ['ї'] = "yi", ['й'] = "y", ['ю'] = "yu", ['я'] = "ya",
    };

    public static string Slugify(string text)
    {
        var sb = new StringBuilder();
        var lower = text.Trim().ToLowerInvariant();

        for (var i = 0; i < lower.Length; i++)
        {
            var c = lower[i];
            var atWordStart = i == 0 || !(char.IsLetter(lower[i - 1]) || lower[i - 1] is '\'' or '’');

            if (c == 'г' && i > 0 && lower[i - 1] == 'з') { sb.Append("gh"); continue; }

            if (atWordStart && WordStart.TryGetValue(c, out var start)) sb.Append(start);
            else if (Map.TryGetValue(c, out var latin)) sb.Append(latin);
            else if (c is >= 'a' and <= 'z' or >= '0' and <= '9') sb.Append(c);
            else sb.Append('-'); 
        }

        var slug = string.Join('-', sb.ToString().Split('-', StringSplitOptions.RemoveEmptyEntries));
        return slug.Length > 0 ? slug : "recipe";
    }
}
