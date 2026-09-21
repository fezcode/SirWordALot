using Xunit;

namespace SirWordALot.Tests;

public class SmokeTest
{
    [Fact]
    public void CoreAssemblyDoesNotReferenceAvalonia()
    {
        var referenced = typeof(SirWordALot.Core.Marker).Assembly
            .GetReferencedAssemblies()
            .Select(a => a.Name ?? "");
        Assert.DoesNotContain(referenced, name => name.StartsWith("Avalonia"));
    }
}
