using Weatherly.Sandbox;

// Debug app: the effects in a normal window, over fake windows that can be dragged with the mouse.
// Keys: D drops on/off, S surfaces on/off, N new window, Delete close top window,
//       Left/Right wind, Escape quit. Shake a window by dragging it quickly back and forth.
//
//   Weatherly.Sandbox [--exit-after=<seconds>]
//
// --exit-after closes the sandbox after the given time and returns 0 if it rendered frames: a CI self-check.

double? exitAfter = null;
foreach (string arg in args)
{
    if (arg.StartsWith("--exit-after=", StringComparison.Ordinal)
        && double.TryParse(arg["--exit-after=".Length..], System.Globalization.CultureInfo.InvariantCulture, out double seconds))
    {
        exitAfter = seconds;
    }
}

using var sandbox = new SandboxWindow();
long frames = sandbox.Run(exitAfter);
Console.WriteLine($"Rendered {frames} frame(s).");
return frames > 0 ? 0 : 1;
