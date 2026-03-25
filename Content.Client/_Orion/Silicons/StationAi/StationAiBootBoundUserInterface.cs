using Content.Shared._Orion.Silicons.StationAi;

namespace Content.Client._Orion.Silicons.StationAi;

public sealed class StationAiBootBoundUserInterface : BoundUserInterface
{
    private StationAiBootWindow? _window;

    public StationAiBootBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey) { }

    protected override void Open()
    {
        base.Open();

        _window = new StationAiBootWindow();
        _window.OpenCentered();
        _window.OnClose += Close;
        _window.InitializationConfirmed += OnInitializationConfirmed;
    }

    private void OnInitializationConfirmed()
    {
        SendPredictedMessage(new StationAiBootCompleteMessage());
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);
        if (state is not StationAiBootBuiState bootState)
            return;

        _window?.ApplyState(bootState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
            return;

        if (_window == null)
            return;

        _window.InitializationConfirmed -= OnInitializationConfirmed;
        _window.Dispose();
    }
}
