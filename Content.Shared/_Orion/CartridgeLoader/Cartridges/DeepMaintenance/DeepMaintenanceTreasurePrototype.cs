using Content.Shared.EntityTable;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared._Orion.CartridgeLoader.Cartridges.DeepMaintenance;

[Prototype("deepMaintenanceTreasure")]
public sealed class DeepMaintenanceTreasurePrototype : IPrototype, IInheritingPrototype
{
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<DeepMaintenanceTreasurePrototype>))]
    public string[]? Parents { get; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; }

    [IdDataField]
    public string ID { get; } = default!;

    [DataField(required: true)]
    public string ClosedCrateSpritePath = default!;

    [DataField(required: true)]
    public string ClosedCrateSpriteState = default!;

    [DataField(required: true)]
    public string OpenCrateSpriteState = default!;

    [DataField(required: true)]
    public ProtoId<EntityTablePrototype> LootTable;

    [DataField]
    public float OpenAnimationDuration = 0.3f;

    [DataField]
    public float RelicPickupGraceDuration = 0.25f;

    [DataField]
    public float RelicAppearDuration = 0.25f;

    [DataField]
    public float RelicAppearRise = 0.3f;
}
