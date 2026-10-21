using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.AspNetCore.Components;

namespace BlazorTestPlatform;

/// <summary>Reflection helpers for component parameters.</summary>
internal static class ComponentParameters
{
    private static readonly ConcurrentDictionary<Type, IReadOnlyDictionary<string, PropertyInfo>> Cache = new();

    public static IReadOnlyDictionary<string, PropertyInfo> Of([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type componentType)
    {
        if (Cache.TryGetValue(componentType, out var cached))
        {
            return cached;
        }

        // Discover outside a lambda so the trimming annotation flows to GetProperties.
        return Cache.GetOrAdd(componentType, Discover(componentType));
    }

    private static IReadOnlyDictionary<string, PropertyInfo> Discover([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.SetMethod is not null && (p.IsDefined(typeof(ParameterAttribute), inherit: true) || p.IsDefined(typeof(CascadingParameterAttribute), inherit: true)))
            .GroupBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

    public static PropertyInfo? Find([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)] Type componentType, string name) => Of(componentType).GetValueOrDefault(name);

    public static string GetName<TComponent, TValue>(Expression<Func<TComponent, TValue>> parameter)
    {
        ArgumentNullException.ThrowIfNull(parameter);
        var body = parameter.Body is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } unary
            ? unary.Operand
            : parameter.Body;

        if (body is MemberExpression { Member: PropertyInfo property } member && member.Expression is ParameterExpression)
        {
            if (!property.IsDefined(typeof(ParameterAttribute), inherit: true) && !property.IsDefined(typeof(CascadingParameterAttribute), inherit: true))
            {
                throw new ArgumentException(
                    $"'{typeof(TComponent).Name}.{property.Name}' is not a component parameter. Add [Parameter] to the property or use Attribute(\"name\", value) for unmatched attributes.",
                    nameof(parameter));
            }

            return property.Name;
        }

        throw new ArgumentException($"The expression '{parameter}' must select a parameter property directly, e.g. c => c.Title.", nameof(parameter));
    }

    public static bool IsEventCallback(Type type) =>
        type == typeof(EventCallback) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(EventCallback<>));

    private static readonly MethodInfo CreateTypedActionMethod =
        typeof(ComponentParameters).GetMethod(nameof(CreateTypedAction), BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Creates an <c>EventCallback</c> or <c>EventCallback&lt;T&gt;</c> that records into the action log.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2060", Justification = "CreateTypedAction<T> places no trimming requirements on T; the closed type EventCallback<T> is already referenced by the component parameter being filled.")]
    public static object CreateActionCallback(Type callbackType, string name, StoryRenderContext context)
    {
        if (callbackType == typeof(EventCallback))
        {
            return EventCallback.Factory.Create(context.ActionReceiver, () => context.Actions.Record(name, null));
        }

        var argumentType = callbackType.GetGenericArguments()[0];
        return CreateTypedActionMethod.MakeGenericMethod(argumentType).Invoke(null, [name, context])!;
    }

    private static EventCallback<T> CreateTypedAction<T>(string name, StoryRenderContext context) =>
        EventCallback.Factory.Create<T>(context.ActionReceiver, (T value) => context.Actions.Record(name, value));
}
