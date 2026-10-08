using WindowGather.Presentation;
using Xunit;

namespace WindowGather.Core.Tests;

public sealed class ArchitectureTests
{
    [Fact]
    public void DomainDependsOnlyOnBclAndPortableFramework()
    {
        var assembly = typeof(Geometry).Assembly;
        Assert.Equal("WindowGather.Domain", assembly.GetName().Name);
        Assert.All(assembly.GetReferencedAssemblies(), reference =>
            Assert.True(reference.Name == "netstandard" || reference.Name!.StartsWith("System."),
                $"Domain has an outward dependency: {reference.Name}"));
        Assert.Contains("Version=v10.0", assembly.GetCustomAttributes(false)
            .OfType<System.Runtime.Versioning.TargetFrameworkAttribute>().Single().FrameworkName);
        Assert.DoesNotContain("Windows", assembly.GetCustomAttributes(false)
            .OfType<System.Runtime.Versioning.TargetFrameworkAttribute>().Single().FrameworkName);
    }

    [Fact]
    public void ApplicationAndPresentationPointInwardWithoutNativeUiReferences()
    {
        Assert.All(typeof(GatherEngine).Assembly.GetReferencedAssemblies(), reference =>
            Assert.True(reference.Name is "WindowGather.Domain" or "netstandard" or "Microsoft.Win32.Primitives" ||
                reference.Name!.StartsWith("System."), reference.Name));
        Assert.All(typeof(MainViewModel).Assembly.GetReferencedAssemblies(), reference =>
            Assert.True(reference.Name is "WindowGather.Domain" or "WindowGather.Application" or "CommunityToolkit.Mvvm" or "netstandard" ||
                reference.Name!.StartsWith("System."), reference.Name));
        Assert.DoesNotContain(typeof(Geometry).Assembly.GetTypes().SelectMany(type => type.GetMethods()),
            method => method.IsDefined(typeof(System.Runtime.InteropServices.DllImportAttribute), inherit: false));
    }
}
