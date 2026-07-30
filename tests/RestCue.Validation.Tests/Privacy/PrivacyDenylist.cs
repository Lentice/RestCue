using System.Text.RegularExpressions;

namespace RestCue.Validation.Tests.Privacy;

public static partial class PrivacyDenylist
{
    public static readonly string[] DeniedSubstrings =
    [
        "windowTitle",
        "clipboard",
        "screenContent",
        "http://",
        "https://",
        "documentName",
        "input",
    ];

    public static readonly string[] PayloadAllowedKeys =
    [
        "result", "previous", "current", "errorCategory"
    ];

    [GeneratedRegex(@"\.[a-zA-Z]{2,5}\b")]
    public static partial Regex FileExtensionPattern();

    public static bool ContainsDeniedContent(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var denied in DeniedSubstrings)
        {
            if (text.Contains(denied, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        var matches = FileExtensionPattern().Matches(text);
        foreach (Match match in matches)
        {
            string ext = match.Value.ToLowerInvariant();
            if (ext is ".doc" or ".docx" or ".pdf" or ".xls" or ".xlsx"
                or ".ppt" or ".pptx" or ".txt" or ".rtf" or ".csv")
            {
                return true;
            }
        }

        return false;
    }
}
