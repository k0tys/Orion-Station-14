using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared._Orion.CartridgeLoader.Cartridges;

[Prototype("deepMaintenanceTile")]
public sealed class DeepMaintenanceTilePrototype : IPrototype, IInheritingPrototype
{
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<DeepMaintenanceTilePrototype>))]
    public string[]? Parents { get; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; }

    [IdDataField] public string ID { get; } = default!;

    [DataField(required: true)]
    public Color Color = Color.Black;

    [DataField]
    public bool Solid;

    [DataField]
    public string? SpritePath;

    [DataField]
    public string? SpriteState;

    [DataField]
    public int SpriteLayer;
}

[Prototype("deepMaintenanceEntity")]
public sealed class DeepMaintenanceEntityPrototype : IPrototype, IInheritingPrototype
{
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<DeepMaintenanceEntityPrototype>))]
    public string[]? Parents { get; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; }

    [IdDataField] public string ID { get; } = default!;

    [DataField(required: true)]
    public Color Color = Color.White;

    [DataField]
    public float Radius = 0.28f;

    [DataField]
    public float MoveSpeed = 2.4f;

    [DataField]
    public int MaxHp = 1;

    [DataField]
    public int ContactDamage = 1;

    [DataField]
    public bool Shooter;

    [DataField]
    public int ShootCooldownTicks = 8;

    [DataField]
    public float? ShootCooldownSeconds;

    [DataField]
    public float ProjectileSpeed = 6f;

    [DataField]
    public string? ProjectileEntityId;

    [DataField]
    public string? SpritePath;

    [DataField]
    public string? SpriteState;

    [DataField]
    public int SpriteLayer;
}
