namespace WindexBar.Core.Windowing;

public sealed class AutoVisibilityStabilityFilter
{
    private readonly int _inactiveSamplesBeforeHide;
    private int _inactiveSamples;
    private bool _stableActive;

    public AutoVisibilityStabilityFilter(int inactiveSamplesBeforeHide)
    {
        _inactiveSamplesBeforeHide = Math.Max(1, inactiveSamplesBeforeHide);
    }

    public bool ShouldTreatAsActive(bool isActiveSample)
    {
        if (isActiveSample)
        {
            _inactiveSamples = 0;
            _stableActive = true;
            return true;
        }

        if (!_stableActive)
        {
            return false;
        }

        _inactiveSamples++;
        if (_inactiveSamples < _inactiveSamplesBeforeHide)
        {
            return true;
        }

        _inactiveSamples = 0;
        _stableActive = false;
        return false;
    }

    public void Reset()
    {
        _inactiveSamples = 0;
        _stableActive = false;
    }
}
