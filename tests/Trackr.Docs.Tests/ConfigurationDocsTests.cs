using System.Text.RegularExpressions;

namespace Trackr.Docs.Tests;

/// <summary>
/// Every environment variable the deployment stack reads must be documented, and must have a
/// line in the example env file.
/// </summary>
/// <remarks>
/// This is the check CLAUDE.md section 0 describes. A self-hoster's only way to discover a
/// setting is the wiki, so a knob added to the compose file and not written down is invisible
/// - and the failure mode is someone unable to configure something that exists.
/// <para>
/// Wider than the section-9 wording ("every TRACKR_* variable"): it covers every substituted
/// variable, which also catches POSTGRES_PASSWORD and PROXY_NETWORK. All fifteen pass today.
/// </para>
/// </remarks>
public sealed class ConfigurationDocsTests
{
    private const string ComposeFile = "docker/docker-compose.yml";
    private const string ConfigurationPage = "wiki/Configuration.md";
    private const string ExampleEnvFile = "docker/.env.example";

    /// <summary>
    /// Matches Compose's variable substitution: ${NAME}, ${NAME:-default} and $NAME alike.
    /// </summary>
    private static readonly Regex Substitution =
        new(@"\$\{?(?<name>[A-Z][A-Z0-9_]*)", RegexOptions.Compiled);

    public static TheoryData<string> DeploymentVariables()
    {
        var compose = RepoRoot.ReadText(ComposeFile);

        var names = Substitution.Matches(compose)
            .Select(m => m.Groups["name"].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal);

        var data = new TheoryData<string>();

        foreach (var name in names)
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(DeploymentVariables))]
    public void Every_deployment_variable_is_documented(string name)
    {
        var page = RepoRoot.ReadText(ConfigurationPage);

        Assert.True(
            page.Contains(name, StringComparison.Ordinal),
            $"{ComposeFile} reads {name}, but {ConfigurationPage} never mentions it. Add a row " +
            $"for it - a setting a self-hoster cannot find documented may as well not exist.");
    }

    [Theory]
    [MemberData(nameof(DeploymentVariables))]
    public void Every_deployment_variable_has_an_example(string name)
    {
        var example = RepoRoot.ReadText(ExampleEnvFile);

        Assert.True(
            example.Contains(name, StringComparison.Ordinal),
            $"{ComposeFile} reads {name}, but {ExampleEnvFile} has no line for it. Copying that " +
            "file is the documented way to configure the stack, so an omission there is a knob " +
            "nobody discovers.");
    }

    /// <summary>
    /// Guards the guard. If the substitution regex ever stops matching - a Compose syntax
    /// change, a bad edit - both theories above would silently pass with nothing to check.
    /// </summary>
    [Fact]
    public void The_compose_file_actually_declares_variables()
    {
        Assert.NotEmpty(DeploymentVariables());
    }
}
