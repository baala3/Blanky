namespace Blanky.Core;

/// <summary>
/// Product identity and outbound links. The display name lives here and in the
/// MSBuild Product property only, so a rename touches as few places as possible.
/// </summary>
public static class AppInfo
{
    public const string Name = "Blanky";
    public const string Tagline = "Ambient sound mixer for Windows";
    public const string AppUserModelId = "Blanky.App";

    public const string WebsiteUrl = "https://baala3.github.io/Blanky/";
    public const string RepositoryUrl = "https://github.com/baala3/Blanky";
    public const string IssuesUrl = RepositoryUrl + "/issues";
    public const string ReleasesUrl = RepositoryUrl + "/releases";
    public const string FaqUrl = WebsiteUrl + "faq/";
    public const string PrivacyUrl = WebsiteUrl + "privacy/";
    public const string BuyMeACoffeeUrl = "https://buymeacoffee.com/baala3";
    public const string GitHubSponsorsUrl = "https://github.com/sponsors/baala3";

    public const string BlankieUrl = "https://github.com/codybrom/Blankie";
    public const string BlanketUrl = "https://github.com/rafaelmardojai/blanket";

    /// <summary>Preset archive extension. Archives use the same layout as Blankie's.</summary>
    public const string PresetExtension = ".blanky";
    public const string BlankiePresetExtension = ".blankie";

    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
}
