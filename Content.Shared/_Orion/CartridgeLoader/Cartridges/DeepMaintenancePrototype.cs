using System.Numerics;
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

    [DataField]
    public float FinalDropStart = 0.85f;

    [DataField]
    public float FinalDropDistance = 0.35f;
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
    public string? BodyAttachedSpritePath;

    [DataField]
    public string? BodyAttachedSpriteState;

    [DataField]
    public float BodyAttachedSpriteScale = 1f;

    [DataField]
    public Vector2 BodyAttachedOffset;

    [DataField]
    public string? HeadAttachedSpritePath;

    [DataField]
    public string? HeadAttachedSpriteState;

    [DataField]
    public float HeadAttachedSpriteScale = 1f;

    [DataField]
    public Vector2 HeadAttachedOffset;

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

[Prototype("deepMaintenancePickup")]
public sealed class DeepMaintenancePickupPrototype : IPrototype, IInheritingPrototype
{
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<DeepMaintenancePickupPrototype>))]
    public string[]? Parents { get; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; }

    [IdDataField]
    public string ID { get; } = default!;

    [DataField("pickupType", required: true)]
    public string PickupType = default!;

    [DataField(required: true)]
    public string SpritePath = default!;

    [DataField(required: true)]
    public string SpriteState = default!;

    [DataField]
    public float SpriteScale = 1f;
}

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

[DataDefinition]
public sealed partial class DeepMaintenanceWeightedEntityEntry
{
    [DataField(required: true)]
    public ProtoId<DeepMaintenanceEntityPrototype> Entity;

    [DataField]
    public float Weight = 1f;
}

[Prototype("deepMaintenanceFloor")]
public sealed class DeepMaintenanceFloorPrototype : IPrototype, IInheritingPrototype
{
    [ParentDataField(typeof(AbstractPrototypeIdArraySerializer<DeepMaintenanceFloorPrototype>))]
    public string[]? Parents { get; }

    [NeverPushInheritance]
    [AbstractDataField]
    public bool Abstract { get; }

    [IdDataField]
    public string ID { get; } = default!;

    [DataField]
    public int FloorNumber = 1;

    [DataField(required: true)]
    public ProtoId<DeepMaintenanceTilePrototype> FloorTile;

    [DataField(required: true)]
    public ProtoId<DeepMaintenanceTilePrototype> WallTile;

    [DataField(required: true)]
    public ProtoId<DeepMaintenanceTilePrototype> ObstacleTile;

    [DataField(required: true)]
    public ProtoId<DeepMaintenanceTilePrototype> MushroomTile;

    [DataField(required: true)]
    public ProtoId<DeepMaintenanceDoorPrototype> DoorNormal;

    [DataField(required: true)]
    public ProtoId<DeepMaintenanceDoorPrototype> DoorTreasure;

    [DataField(required: true)]
    public ProtoId<DeepMaintenanceDoorPrototype> DoorBoss;

    [DataField]
    public float BaseLight = 0.74f;

    [DataField]
    public float BossRoomBaseLight = 0.45f;

    [DataField]
    public float ShopRoomBaseLight = 0.85f;

    [DataField]
    public float VignetteStrength = 0.2f;

    [DataField]
    public float PlayerLightRadius = 3.6f;

    [DataField]
    public float PlayerLightStrength = 0.48f;

    [DataField]
    public List<DeepMaintenanceWeightedEntityEntry> EnemyPool = new();

    [DataField]
    public List<DeepMaintenanceWeightedEntityEntry> BossPool = new();
}
