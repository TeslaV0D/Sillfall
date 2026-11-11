using Weatherly.Sandbox;

// Debug app: the effects in a normal window, over fake windows that can be dragged with the mouse.
// Keys: D drops on/off, R rain on/off, W snow on/off, F fill all windows with snow, S surfaces on/off,
//       N new window, Delete close top window, Left/Right wind, Escape quit.
// Snow demo: press F, then shake a window (drag it quickly back and forth), fling it, or close it with Delete:
// the snow slides off and piles up wherever it lands.
//
//   Weatherly.Sandbox [--effect=drops|rain|snow] [--fill-snow] [--exit-after=<seconds>]
//
// --exit-after closes the sandbox after the given time and returns 0 if it rendered frames: a CI self-check.

double? exitAfter = null;
string effect = "drops";
bool fillSnow = false;
foreach (string arg in args)
{
    if (arg.StartsWith("--exit-after=", StringComparison.Ordinal)
        && double.TryParse(arg["--exit-after=".Length..], System.Globalization.CultureInfo.InvariantCulture, out double seconds))
    {
        exitAfter = seconds;
    }
    else if (arg.StartsWith("--effect=", StringComparison.Ordinal))
    {
        effect = arg["--effect=".Length..];
    }
    else if (arg == "--fill-snow")
    {
        fillSnow = true;
    }
}

using var sandbox = new SandboxWindow(effect, fillSnow);
long frames = sandbox.Run(exitAfter);
Console.WriteLine($"Rendered {frames} frame(s).");
return frames > 0 ? 0 : 1;
