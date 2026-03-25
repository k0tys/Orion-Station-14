using Robust.Shared.GameStates;

namespace Content.Shared._Orion.Silicons.StationAi;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AiBootComponent : Component
{
    [DataField, AutoNetworkedField]
    public new bool Initialized;

    /// <summary>
    /// True when this AI must complete the boot flow before regular operations.
    /// </summary>
    [DataField, AutoNetworkedField]
    public bool ShowBootFlow = true;

    [DataField, AutoNetworkedField]
    public string AiName = string.Empty;

    [DataField, AutoNetworkedField]
    public bool IsMalf;
}
