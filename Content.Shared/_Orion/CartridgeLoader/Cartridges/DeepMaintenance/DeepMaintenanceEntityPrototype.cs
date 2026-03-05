using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared._Orion.CartridgeLoader.Cartridges.DeepMaintenance;

[Prototype("deepMaintenanceEntity")]
public sealed class DeepMaintenanceEntityPrototype : IPrototype, IInheritingPrototype
{
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<DeepMaintenanceEntityPrototype>))]
    public string[]? Parents { get; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; }

    [IdDataField] public string ID { get; } = default!;

    [DataField]
    public float Radius = 0.28f;

    [DataField]
    public DeepMaintenanceHitboxShape HitboxShape = DeepMaintenanceHitboxShape.Circle;

    [DataField]
    public float HitboxWidth = 0.56f;

    [DataField]
    public float HitboxHeight = 0.56f;

    [DataField]
    public float HitboxOffsetX;

    [DataField]
    public float HitboxOffsetY;

    [DataField]
    public float MoveSpeed = 2.4f;

    [DataField]
    public float MoveAcceleration = 24f;

    [DataField]
    public float MoveFriction = 18f;

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
    public string ProjectilePrototype = "ProjectilePlayer";

    [DataField]
    public bool CanStrafe;

    [DataField]
    public bool IsBoss;

    [DataField(required: true)]
    public string SpritePath = default!;

    [DataField(required: true)]
    public string SpriteState = default!;

    [DataField]
    public float SpriteScale = 1f;

    [DataField]
    public int SpriteLayer;

    [DataField]
    public string? BodySpriteState;

    [DataField]
    public string? HeadSpriteState;

    [DataField]
    public string? ShootSpriteState;
}
