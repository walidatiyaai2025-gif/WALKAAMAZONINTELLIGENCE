namespace WalkaAmazonIntelligence.Application.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void ApplicationLayerOnlyReferencesDomainFromSolutionAssemblies()
    {
        var applicationAssembly = typeof(IReportConnector).Assembly;

        var solutionReferences = applicationAssembly
            .GetReferencedAssemblies()
            .Select(reference => reference.Name)
            .Where(name => name is not null && name.StartsWith("WalkaAmazonIntelligence.", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(new[] { "WalkaAmazonIntelligence.Domain" }, solutionReferences);
    }
}
