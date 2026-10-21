using System.Diagnostics.CodeAnalysis;

namespace BlazorTestPlatform;

/// <summary>
/// Supplies an assembly's story collections and configurations without reflection. Implemented by code that the
/// BlazorTestPlatform source generator emits into every assembly declaring stories.
/// </summary>
public interface IStoryCatalogProvider
{
    /// <summary>New instances of every story collection in the assembly.</summary>
    IReadOnlyList<StoryCollection> CreateCollections();

    /// <summary>New instances of every story configuration in the assembly.</summary>
    IReadOnlyList<IStoryConfiguration> CreateConfigurations();
}

/// <summary>Points <see cref="StoryCatalog"/> at an assembly's generated <see cref="IStoryCatalogProvider"/>.</summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class StoryCatalogProviderAttribute(
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type providerType) : Attribute
{
    /// <summary>The provider type.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type ProviderType { get; } = providerType ?? throw new ArgumentNullException(nameof(providerType));
}
