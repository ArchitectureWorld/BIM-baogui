namespace BIMBaoGui.Stage01.Core
{
  internal sealed class ExplicitExecutionGate
  {
    private bool _sampled;
    private bool _previous;

    public bool Observe(bool current)
    {
      if (!_sampled)
      {
        _sampled = true;
        _previous = current;
        return false;
      }

      bool risingEdge = current && !_previous;
      _previous = current;
      return risingEdge;
    }
  }
}
