; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
BTP001 | BlazorTestPlatform.Stories | Error | StoryAnalyzer: story argument is not a component parameter
BTP002 | BlazorTestPlatform.Stories | Error | StoryAnalyzer: bound parameter has no matching Changed callback
BTP003 | BlazorTestPlatform.Stories | Warning | StoryAnalyzer: use Arg for declared parameters
BTP004 | BlazorTestPlatform.Stories | Warning | StoryAnalyzer: component does not accept unmatched attributes
BTP005 | BlazorTestPlatform.Stories | Error | StoryAnalyzer: duplicate story
