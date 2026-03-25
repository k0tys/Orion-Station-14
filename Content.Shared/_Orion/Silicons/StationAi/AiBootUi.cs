using Robust.Shared.Serialization;

namespace Content.Shared._Orion.Silicons.StationAi;

[Serializable, NetSerializable]
public enum StationAiBootUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class StationAiBootBuiState(string aiName, bool initialized, bool showBootFlow, bool isMalf) : BoundUserInterfaceState
{
    public string AiName { get; } = aiName;
    public bool Initialized { get; } = initialized;
    public bool ShowBootFlow { get; } = showBootFlow;
    public bool IsMalf { get; } = isMalf;
}

[Serializable, NetSerializable]
public sealed class StationAiBootCompleteMessage : BoundUserInterfaceMessage;
