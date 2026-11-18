using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorTestPlatform.Bunit;

/// <summary>
/// Finds what a story's components leave behind once they are disposed: <c>DotNetObjectReference</c>s they passed to
/// JavaScript and did not dispose, and handlers they added to events of services they injected and did not remove. Both
/// keep a removed component alive (for a circuit's lifetime, or the app's with singletons) and let it be called later.
/// </summary>
/// <remarks>
/// <see cref="Observe"/> runs while components are alive (after the first render, after every action and before
/// disposal), so components that play removed are checked too; <see cref="CaptureReferences"/> runs before disposal and
/// <see cref="Find"/> after the renderer disposed the components.
/// </remarks>
internal sealed class LeakCheck
{
    private readonly BunitContext _context;
    private readonly HashSet<object> _components = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<object> _seenServices = new(ReferenceEqualityComparer.Instance);
    private readonly List<(object Service, Type Declared)> _services = [];
    private readonly Dictionary<Type, ServiceLifetime> _lifetimes = [];
    private readonly HashSet<Type> _registeredTypes = [];
    private List<(object Reference, string Call)> _references = [];

    /// <summary>Starts tracking the story rendered in <paramref name="context"/>.</summary>
    public LeakCheck(BunitContext context)
    {
        _context = context;

        // Registered lifetimes (the last registration wins, as in resolution). A transient service belongs to the
        // component that got it and goes away with it, so a handler on it is no leak; singletons and scoped services
        // (a circuit's lifetime) outlive the component. Unregistered types (framework services) count as long-lived.
        foreach (var descriptor in context.Services)
        {
            _lifetimes[descriptor.ServiceType] = descriptor.Lifetime;
            _registeredTypes.Add(descriptor.ServiceType);
            var implementation = descriptor.IsKeyedService
                ? descriptor.KeyedImplementationType ?? descriptor.KeyedImplementationInstance?.GetType()
                : descriptor.ImplementationType ?? descriptor.ImplementationInstance?.GetType();
            if (implementation is not null)
            {
                _registeredTypes.Add(implementation);
            }
        }
    }

    /// <summary>Records the live components of the story and the long-lived services they hold.</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "bUnit runs in test processes, which are not trimmed.")]
    public void Observe(IRenderedComponent<IComponent> root)
    {
        var added = root.FindComponents<IComponent>().Select(c => (object)c.Instance).Prepend(root.Instance).Where(_components.Add).ToArray();
        foreach (var component in added)
        {
            for (var type = component.GetType(); type is not null && type != typeof(ComponentBase) && type != typeof(object); type = type.BaseType)
            {
                const BindingFlags Declared = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

                // [Inject] properties, and fields of service types (constructor injection, services kept from an IServiceProvider).
                var held = type.GetProperties(Declared)
                    .Where(p => p.GetIndexParameters().Length == 0 && p.IsDefined(typeof(InjectAttribute), inherit: true))
                    .Select(p => (Value: Read(() => p.GetValue(component)), Declared: p.PropertyType))
                    .Concat(type.GetFields(Declared)
                        .Where(f => !f.FieldType.IsValueType && f.FieldType != typeof(string) && IsService(f.FieldType))
                        .Select(f => (Value: Read(() => f.GetValue(component)), Declared: f.FieldType)));
                foreach (var (value, declared) in held)
                {
                    if (value is not null && OutlivesComponents(declared) && !_components.Contains(value) && _seenServices.Add(value))
                    {
                        _services.Add((value, declared));
                    }
                }
            }
        }
    }

    /// <summary>Records the <c>DotNetObjectReference</c>s passed to JavaScript (before the services are disposed).</summary>
    public void CaptureReferences()
    {
        var found = new List<(object Reference, string Call)>();
        var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
        void Add(string call, IEnumerable<object?> arguments)
        {
            foreach (var argument in arguments)
            {
                if (DotNetCallbacks.IsReference(argument) && seen.Add(argument!))
                {
                    found.Add((argument!, call));
                }
            }
        }

        foreach (var invocation in _context.JSInterop.Invocations)
        {
            Add(invocation.Identifier, invocation.Arguments);
        }

        if (_context.Services.GetService<JSCallLog>() is { } log)
        {
            foreach (var call in log.Calls.Where(c => c.DotNetReferences > 0))
            {
                Add((call.Module is null ? string.Empty : call.Module + ":") + call.Identifier, Enumerable.Range(0, call.DotNetReferences).Select(i => log.DotNetReference(call.Sequence, i)));
            }
        }

        _references = found;
    }

    private bool IsService(Type type) =>
        _lifetimes.ContainsKey(type) || (type.IsConstructedGenericType && _lifetimes.ContainsKey(type.GetGenericTypeDefinition()));

    private bool OutlivesComponents(Type type) =>
        !(_lifetimes.TryGetValue(type, out var lifetime) || (type.IsConstructedGenericType && _lifetimes.TryGetValue(type.GetGenericTypeDefinition(), out lifetime)))
        || lifetime != ServiceLifetime.Transient;

    // A reference to a service (injected by a component, or of a registered type) is an app-level registration that may
    // live as long as the service; references to components and to the helpers they made are theirs to dispose.
    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "bUnit runs in test processes, which are not trimmed.")]
    private bool BelongsToTheApp(object target) =>
        !_components.Contains(target)
        && (_seenServices.Contains(target) || _registeredTypes.Contains(target.GetType()) || target.GetType().GetInterfaces().Any(_registeredTypes.Contains));

    /// <summary>What the disposed components left behind, one description per leak (empty when nothing leaked).</summary>
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "bUnit runs in test processes, which are not trimmed.")]
    public IReadOnlyList<string> Find()
    {
        var leaks = new List<string>();
        foreach (var (reference, call) in _references)
        {
            if (!IsDisposed(reference) && Read(() => reference.GetType().GetProperty("Value")!.GetValue(reference)) is { } value && !BelongsToTheApp(value))
            {
                var target = reference.GetType().GetGenericArguments()[0];
                leaks.Add($"DotNetObjectReference<{target.Name}> passed to JavaScript ({call}) was not disposed: JavaScript can still call the disposed {target.Name}, and it stays in memory. Dispose the reference when the component is disposed.");
            }
        }

        foreach (var (service, declared) in _services)
        {
            for (var type = service.GetType(); type is not null && type != typeof(object); type = type.BaseType)
            {
                foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    foreach (var handler in Delegates(Read(() => field.GetValue(service))))
                    {
                        if (HeldComponent(handler.Target, depth: 0) is { } component)
                        {
                            leaks.Add($"{declared.Name}.{EventName(field.Name)} still calls {component.GetType().Name}.{HandlerName(handler.Method)} after the component was disposed: the service keeps the component alive and calls it later. Remove the handler when the component is disposed.");
                        }
                    }
                }
            }
        }

        return leaks.Distinct(StringComparer.Ordinal).ToArray();
    }

    // DotNetObjectReference<T>.Value throws ObjectDisposedException once the reference is disposed.
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "bUnit runs in test processes, which are not trimmed.")]
    private static bool IsDisposed(object reference)
    {
        try
        {
            _ = reference.GetType().GetProperty("Value")!.GetValue(reference);
            return false;
        }
        catch (TargetInvocationException ex) when (ex.InnerException is ObjectDisposedException)
        {
            return true;
        }
    }

    private static IEnumerable<Delegate> Delegates(object? value)
    {
        switch (value)
        {
            case Delegate single:
                return single.GetInvocationList();
            case System.Collections.IEnumerable items when HoldsDelegates(items.GetType()):
                // Subscriber lists a service keeps itself (List<Action>, Func<...>[]...). Other collections are not
                // enumerated: they may be lazy, large or have side effects.
                var found = new List<Delegate>();
                try
                {
                    foreach (var item in items)
                    {
                        if (item is Delegate handler)
                        {
                            found.AddRange(handler.GetInvocationList());
                        }
                    }
                }
                catch (InvalidOperationException)
                {
                    // Changed while enumerating: nothing reliable to report.
                }

                return found;
            default:
                return [];
        }
    }

    [UnconditionalSuppressMessage("Trimming", "IL2070", Justification = "bUnit runs in test processes, which are not trimmed.")]
    private static bool HoldsDelegates(Type type) =>
        (type.IsArray && typeof(Delegate).IsAssignableFrom(type.GetElementType()))
        || type.GetInterfaces().Any(i => i.IsConstructedGenericType && i.GetGenericTypeDefinition() == typeof(IEnumerable<>) && typeof(Delegate).IsAssignableFrom(i.GetGenericArguments()[0]));

    // The component a handler's target is, or captures (lambdas compile to closure classes that hold `this`).
    [UnconditionalSuppressMessage("Trimming", "IL2075", Justification = "bUnit runs in test processes, which are not trimmed.")]
    private object? HeldComponent(object? target, int depth)
    {
        if (target is null)
        {
            return null;
        }

        if (_components.Contains(target))
        {
            return target;
        }

        if (depth >= 2 || !target.GetType().IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
        {
            return null;
        }

        foreach (var field in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (!field.FieldType.IsValueType && HeldComponent(Read(() => field.GetValue(target)), depth + 1) is { } component)
            {
                return component;
            }
        }

        return null;
    }

    private static string EventName(string field) => field.TrimStart('_') is { Length: > 0 } name ? char.ToUpperInvariant(name[0]) + name[1..] : field;

    // Lambdas are named <Method>b__0_0: show the method that declared them.
    private static string HandlerName(MethodInfo method) =>
        method.Name.StartsWith('<') && method.Name.IndexOf('>') is > 1 and var end ? method.Name[1..end] + " (lambda)" : method.Name;

    private static object? Read(Func<object?> read)
    {
        try
        {
            return read();
        }
        catch (Exception ex) when (ex is TargetInvocationException or InvalidOperationException or ObjectDisposedException or NotSupportedException)
        {
            return null;
        }
    }
}
