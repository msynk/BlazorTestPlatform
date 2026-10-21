namespace BlazorTestPlatform;

/// <summary>Thrown when an expectation in a play function or quality gate fails.</summary>
public class StoryAssertionException : Exception
{
    /// <summary>Creates the exception.</summary>
    public StoryAssertionException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an inner exception.</summary>
    public StoryAssertionException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception.</summary>
    public StoryAssertionException()
    {
    }
}

/// <summary>Thrown when a locator cannot be resolved to an actionable element in time.</summary>
public sealed class ElementNotActionableException : StoryAssertionException
{
    /// <summary>Creates the exception.</summary>
    public ElementNotActionableException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an inner exception.</summary>
    public ElementNotActionableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception.</summary>
    public ElementNotActionableException()
    {
    }
}

/// <summary>
/// Thrown (via <see cref="StoryContext.Require"/>) when the current driver lacks a capability the story needs.
/// Runners report the story as skipped for that driver instead of failed.
/// </summary>
public sealed class StorySkippedException : Exception
{
    /// <summary>Creates the exception.</summary>
    public StorySkippedException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with an inner exception.</summary>
    public StorySkippedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates the exception.</summary>
    public StorySkippedException()
    {
    }
}
