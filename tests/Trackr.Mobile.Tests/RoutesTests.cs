using System.Reflection;
using Trackr.Mobile.Core.ViewModels;

namespace Trackr.Mobile.Tests;

/// <summary>
/// Holds <see cref="Routes"/> and the Shell XAML together.
/// </summary>
/// <remarks>
/// The XAML binds each route with <c>{x:Static vm:Routes.X}</c>, so the two can no longer
/// disagree about a route's *value* - the source generator emits
/// <c>shellContent.Route = Routes.X</c> and a rename breaks the build.
/// <para>
/// What that does not catch is a constant with no <c>ShellContent</c> behind it. Adding a
/// route here and forgetting the XAML compiles perfectly and throws only when something
/// navigates to it - and navigation happens inside fire-and-forget handlers and relay
/// commands, so it surfaces as a button that does nothing. That is the gap this covers.
/// </para>
/// <para>
/// Reading the XAML off disk rather than reflecting over the built Shell because this project
/// references Trackr.Mobile.Core only: the MAUI project needs the Android SDK to compile, and
/// keeping this suite free of it is what lets it run under a plain <c>dotnet test</c>.
/// </para>
/// </remarks>
public class RoutesTests
{
    public static TheoryData<string> RouteNames()
    {
        var data = new TheoryData<string>();

        foreach (var name in Constants().Select(f => f.Name))
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RouteNames))]
    public void Every_route_constant_is_declared_in_a_shell(string name)
    {
        var declarations = ShellXaml();

        Assert.True(
            declarations.Contains($"Routes.{name}", StringComparison.Ordinal),
            $"Routes.{name} has no ShellContent in any *Shell.xaml. Navigating to it would "
                + "throw at runtime inside a command, which looks like a dead button.");
    }

    [Fact]
    public void The_constants_and_the_shell_xaml_are_both_being_read()
    {
        // Guards the guard: an empty constant list or an unfound XAML file would make the
        // theory above vacuously pass.
        Assert.NotEmpty(Constants());
        Assert.Contains("ShellContent", ShellXaml(), StringComparison.Ordinal);
    }

    private static FieldInfo[] Constants() =>
        typeof(Routes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f is { IsLiteral: true, IsInitOnly: false })
            .ToArray();

    private static string ShellXaml() =>
        string.Concat(RepoRoot.Glob("src/Trackr.Mobile", "*Shell.xaml").Select(File.ReadAllText));
}
