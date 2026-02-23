using Content.Client.UserInterface.Fragments;
using Robust.Client.UserInterface;

namespace Content.Client._Orion.CartridgeLoader.Cartridges;

public sealed partial class DeepMaintenanceUi : UIFragment
{
    private DeepMaintenanceUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new DeepMaintenanceUiFragment();
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        _fragment?.EnsureInputFocus();
    }
}
