using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared._Orion.CartridgeLoader.Cartridges.DeepMaintenance;

[Prototype("deepMaintenanceModifier")]
public sealed class DeepMaintenanceModifierPrototype : IPrototype, IInheritingPrototype
{
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<DeepMaintenanceModifierPrototype>))]
    public string[]? Parents { get; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; }

    [IdDataField]
    public string ID { get; } = default!;

    [DataField]
    public float ProjectileScaleMultiplier = 1f;

    [DataField]
    public float PlayerScaleMultiplier = 1f;

    [DataField]
    public string? HudIconSpritePath;

    [DataField]
    public string? HudIconSpriteState;
}
