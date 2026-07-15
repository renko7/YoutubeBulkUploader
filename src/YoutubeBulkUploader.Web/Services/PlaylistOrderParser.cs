using System.Text.RegularExpressions;

namespace YoutubeBulkUploader.Web.Services;

public static partial class PlaylistOrderParser
{
    public static int? TryParseLeadingNumber(string fileNameWithoutExtension)
    {
        var match = LeadingNumberRegex().Match(fileNameWithoutExtension);
        return match.Success ? int.Parse(match.Groups[1].Value) : null;
    }

    [GeneratedRegex(@"^\s*(\d+)")]
    private static partial Regex LeadingNumberRegex();
}
