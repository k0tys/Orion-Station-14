using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared._Orion.CartridgeLoader.Cartridges.DeepMaintenance;

[Prototype("deepMaintenanceDoor")]
public sealed class DeepMaintenanceDoorPrototype : IPrototype, IInheritingPrototype
{
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<DeepMaintenanceDoorPrototype>))]
    public string[]? Parents { get; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; }

    [IdDataField]
    public string ID { get; } = default!;

    [DataField(required: true)]
    public string SpritePath = default!;

    [DataField(required: true)]
    public string ClosedState = default!;

    [DataField(required: true)]
    public string OpenState = default!;

    [DataField]
    public string? OpeningState;

    [DataField]
    public string? ClosingState;

    [DataField]
    public float TransitionDuration = 0.2f;
}
