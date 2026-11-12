using Sillfall.Core.Visitors;

namespace Sillfall.Platform.Overlay;

/// <summary>A visitor was collected: who, and the weather at that moment (for the points).</summary>
public sealed class VisitorCollectedEventArgs(VisitorKind kind, WeatherMood mood) : EventArgs
{
    public VisitorKind Kind { get; } = kind;

    public WeatherMood Mood { get; } = mood;
}
