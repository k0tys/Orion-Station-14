using Content.Shared.EntityTable;
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

    [DataField]
    public bool Solid;

    [DataField(required: true)]
    public string SpritePath = default!;

    [DataField]
    public string? SpriteState;

    [DataField]
    public int SpriteLayer;
}

[Prototype("deepMaintenanceProjectile")]
public sealed class DeepMaintenanceProjectilePrototype : IPrototype, IInheritingPrototype
{
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<DeepMaintenanceProjectilePrototype>))]
    public string[]? Parents { get; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; }

    [IdDataField] public string ID { get; } = default!;

    [DataField]
    public float Radius = 0.09f;

    [DataField]
    public float Speed = 5f;

    [DataField]
    public int Damage = 1;

    [DataField]
    public float Lifetime = 2.2f;

    [DataField(required: true)]
    public string SpritePath = default!;

    [DataField(required: true)]
    public string SpriteState = default!;

    [DataField]
    public float SpriteScale = 1f;

    [DataField]
    public float Drag;

    [DataField]
    public float GravityScale;

    [DataField]
    public bool RotateToVelocity;
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

    [DataField]
    public float Radius = 0.28f;

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
    public string ProjectilePrototype = "DeepMaintenanceProjectilePlayer";

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

[Prototype("deepMaintenanceRelic")]
public sealed class DeepMaintenanceRelicPrototype : IPrototype, IInheritingPrototype
{
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<DeepMaintenanceRelicPrototype>))]
    public string[]? Parents { get; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; }

    [IdDataField] public string ID { get; } = default!;

    [DataField]
    public string? HudIconSpritePath;

    [DataField]
    public string? HudIconSpriteState;

    [DataField]
    public string? VisualEffectSpritePath;

    [DataField]
    public string? VisualEffectSpriteState;

    [DataField]
    public float ProjectileSpeedMultiplier = 1f;

    [DataField]
    public float DamageFlatBonus;

    [DataField]
    public float ShootCooldownMultiplier = 1f;

    [DataField]
    public bool TripleShotAlternating;

    [DataField]
    public bool MeleeOnShoot;

    [DataField]
    public float MeleeRange = 1.05f;

    [DataField]
    public int MeleeDamage = 2;

    [DataField]
    public string? MeleeArcSpritePath;

    [DataField]
    public string? MeleeArcSpriteState;

    [DataField]
    public bool MeleeArcAnimated;

    [DataField]
    public float MeleeArcAnimationFps = 12f;

    [DataField]
    public string[]? ProjectileTintPaletteHex;

    [DataField]
    public float HalfHeartRestoreChanceOnDamage;
}

[Prototype("deepMaintenanceTreasure")]
public sealed class DeepMaintenanceTreasurePrototype : IPrototype, IInheritingPrototype
{
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<DeepMaintenanceTreasurePrototype>))]
    public string[]? Parents { get; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; }

    [IdDataField] public string ID { get; } = default!;

    [DataField(required: true)]
    public string ClosedCrateSpritePath = default!;

    [DataField(required: true)]
    public string ClosedCrateSpriteState = default!;

    [DataField(required: true)]
    public string OpenCrateSpriteState = default!;

    [DataField(required: true)]
    public ProtoId<EntityTablePrototype> LootTable = default!;
}

[Prototype("deepMaintenanceModifier")]
public sealed class DeepMaintenanceModifierPrototype : IPrototype, IInheritingPrototype
{
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<DeepMaintenanceModifierPrototype>))]
    public string[]? Parents { get; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; }

    [IdDataField] public string ID { get; } = default!;

    [DataField]
    public float ProjectileScaleMultiplier = 1f;

    [DataField]
    public float PlayerScaleMultiplier = 1f;

    [DataField]
    public string? HudIconSpritePath;

    [DataField]
    public string? HudIconSpriteState;
}
