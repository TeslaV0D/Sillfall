using Weatherly.Core.Visitors;

namespace Weatherly.Platform.Overlay;

/// <summary>A visitor was collected: who, and the weather at that moment (for the points).</summary>
public sealed class VisitorCollectedEventArgs(VisitorKind kind, WeatherMood mood) : EventArgs
{
    public VisitorKind Kind { get; } = kind;

    public WeatherMood Mood { get; } = mood;
}
