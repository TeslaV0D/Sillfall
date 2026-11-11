using Weatherly.Core.Desktop;
using Weatherly.Core.Rendering;

namespace Weatherly.Core.Effects;

/// <summary>
/// One weather effect (rain, snow, leaves, fog, sun, grass). Every effect implements the same contract
/// (PLAN.md, section 4), so the engine can run any combination of them side by side.
/// <para>Implementations must not allocate in <see cref="Simulate"/> and <see cref="Emit"/>: they run every frame.</para>
/// </summary>
public interface IEffectSystem
{
    /// <summary>Short name for diagnostics, for example "rain".</summary>
    string Name { get; }

    /// <summary>
    /// Whether the effect has anything to simulate or draw. When no effect is active, the render loop
    /// stops completely (PLAN.md, section 2: 0 % GPU while idle).
    /// </summary>
    bool IsActive { get; }

    /// <summary>Advances the simulation by one fixed step.</summary>
    void Simulate(float dt, SimulationContext context);

    /// <summary>Reacts to a window that appeared, disappeared, moved, was resized or shaken.</summary>
    void OnWindowEvent(in WindowEvent windowEvent, SimulationContext context);

    /// <summary>Adds the current state as sprites. Data only: no graphics API calls.</summary>
    void Emit(RenderFrame frame, SimulationContext context);
}
