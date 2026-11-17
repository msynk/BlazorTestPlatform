namespace BlazorTestPlatform;

/// <summary>CSS user-action pseudo-classes a play script can force on an element (<c>ForceStateAsync</c>).</summary>
[Flags]
public enum PseudoState
{
    /// <summary>None: clears forced states.</summary>
    None = 0,

    /// <summary><c>:hover</c> (the element and its ancestors, as a real pointer).</summary>
    Hover = 1,

    /// <summary><c>:active</c> (the element and its ancestors).</summary>
    Active = 2,

    /// <summary><c>:focus</c>.</summary>
    Focus = 4,

    /// <summary><c>:focus-visible</c>.</summary>
    FocusVisible = 8,

    /// <summary><c>:focus-within</c> (the element and its ancestors).</summary>
    FocusWithin = 16,

    /// <summary><c>:visited</c>.</summary>
    Visited = 32,
}
