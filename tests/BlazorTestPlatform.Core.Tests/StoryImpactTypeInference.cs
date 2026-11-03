using Microsoft.AspNetCore.Components.Rendering;

// The shape Razor generates for <ImpactGrid Items="people" /> in ImpactDirectory.razor, where TItem is inferred: the
// OpenComponent call lives in this helper, not in ImpactDirectory's render method.
namespace __Blazor.BlazorTestPlatform.Core.Tests.ImpactDirectory
{
    internal static class TypeInference
    {
        public static void CreateImpactGrid_0<TItem>(RenderTreeBuilder __builder, int seq, int __seq0, IEnumerable<TItem> __arg0)
        {
            __builder.OpenComponent<global::BlazorTestPlatform.Core.Tests.ImpactGrid<TItem>>(seq);
            __builder.AddComponentParameter(__seq0, "Items", __arg0);
            __builder.CloseComponent();
        }
    }
}
