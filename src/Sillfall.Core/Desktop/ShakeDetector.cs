namespace Sillfall.Core.Desktop;

/// <summary>
/// Detects shaking along one axis: at least <see cref="RequiredReversals"/> direction reversals within
/// <see cref="Window"/> seconds, each swing larger than <see cref="MinimumAmplitude"/> pixels
/// (PLAN.md, section 3.2: three reversals within 600 ms, amplitude above 20 px).
/// </summary>
public struct ShakeDetector
{
    public const int RequiredReversals = 3;
    public const float MinimumAmplitude = 20f;
    public const double Window = 0.6;

    private int _direction;
    private float _extreme;
    private bool _initialized;
    private ReversalTimes _reversals;
    private int _reversalCount;

    /// <summary>Feeds the next position. Returns <see langword="true"/> once per detected shake.</summary>
    public bool Add(double time, float position)
    {
        if (!_initialized)
        {
            _initialized = true;
            _extreme = position;
            return false;
        }

        if (_direction == 0)
        {
            // The first swing that is large enough decides the direction.
            if (MathF.Abs(position - _extreme) > MinimumAmplitude)
            {
                _direction = Math.Sign(position - _extreme);
                _extreme = position;
            }
            else if (MathF.Abs(position - _extreme) < 0.5f)
            {
                _extreme = position;
            }

            return false;
        }

        // Keep following the current direction to the furthest point of the swing.
        if ((_direction > 0 && position >= _extreme) || (_direction < 0 && position <= _extreme))
        {
            _extreme = position;
            return false;
        }

        // Moved back from the extreme: a reversal once the swing back is large enough.
        if (MathF.Abs(position - _extreme) <= MinimumAmplitude)
        {
            return false;
        }

        _direction = -_direction;
        _extreme = position;
        return RecordReversal(time);
    }

    public void Reset() => this = default;

    private bool RecordReversal(double time)
    {
        // Ring of the last reversal times. Older ones than the window are irrelevant.
        _reversals[_reversalCount % RequiredReversals] = time;
        _reversalCount++;

        if (_reversalCount < RequiredReversals)
        {
            return false;
        }

        double oldest = _reversals[_reversalCount % RequiredReversals];
        if (time - oldest > Window)
        {
            return false;
        }

        // One event per shake gesture: start counting again.
        _reversalCount = 0;
        return true;
    }

    [System.Runtime.CompilerServices.InlineArray(RequiredReversals)]
    private struct ReversalTimes
    {
        private double _element;
    }
}
