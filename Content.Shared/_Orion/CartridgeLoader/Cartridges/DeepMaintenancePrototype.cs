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

    [DataField]
    public int BasePrice = 15;

    [DataField]
    public List<DeepMaintenanceResourceGrantEntry> ResourceGrants = new();

    [DataField]
    public float ShopPriceMultiplier = 1f;

    [DataField]
    public float FearAuraRadius;

    [DataField]
    public float FearLingerSeconds = 2f;

    [DataField]
    public float FearBossCooldownSeconds = 8f;

    [DataField]
    public float ExtraShotChance;

    [DataField]
    public int ExtraShotMaxCount;

    [DataField]
    public bool ChainLightningEnabled;

    [DataField]
    public float ChainLightningRate = 5.3f;

    [DataField]
    public float ChainLightningDamageMultiplier = 0.75f;

    [DataField]
    public float ChainLightningRadius = 4.5f;

    [DataField]
    public float ChainLightningJumpRadius = 2.2f;

    [DataField]
    public int ChainLightningMaxTargets = 4;

    [DataField]
    public int OnDamageRadialProjectileCount;

    [DataField]
    public float OnDamageRadialProjectileDamage;

    [DataField]
    public float OnDamageFireRateFirstBonus;

    [DataField]
    public float OnDamageFireRateStackBonus;

    [DataField]
    public float PassiveFireRateBonus;

    [DataField]
    public float PassiveProjectileSpeedBonus;

    [DataField]
    public float PassiveDamageBonus;

    [DataField]
    public float PassiveLuckBonus;

    [DataField]
    public float NoDamageRoomFireRatePerRoom;

    [DataField]
    public float NoDamageRoomFireRateMax;

    [DataField]
    public float NoDamageRoomStartBonus;

    [DataField]
    public bool ResetNoDamageBonusOnHit;

    [DataField]
    public bool ClaymoreEnabled;

    [DataField]
    public float ClaymoreMeleeDamageMultiplier = 3f;

    [DataField]
    public float ClaymoreChargedDamageMultiplier = 8f;

    [DataField]
    public float ClaymoreChargeDuration = 0.8f;

    [DataField]
    public float ClaymoreSwingRadius = 1.2f;

    [DataField]
    public float ClaymoreReflectRadius = 1.4f;

    [DataField]
    public bool ClaymoreProjectileOnFullHealth;

    [DataField]
    public float ClaymoreProjectileBonusDamage = 2f;

    [DataField]
    public bool CaretakerEnabled;

    [DataField]
    public float CaretakerFollowDistance = 1.1f;

    [DataField]
    public float CaretakerMoveSpeed = 7f;

    [DataField]
    public float CaretakerInterceptRadius = 4f;

    [DataField]
    public float CaretakerDashCooldown = 0.4f;

    [DataField]
    public float CaretakerRestChance = 0.2f;

    [DataField]
    public float CaretakerRestDuration = 0.5f;

    [DataField]
    public float CaretakerContactDps = 15f;

    [DataField]
    public float LeftEyeProjectileSpeedMultiplier = 1f;

    [DataField]
    public float LeftEyeFallbackFireRateBonus;

    [DataField]
    public float RightEyeDamageMultiplier = 1f;

    [DataField]
    public float RightEyeRangeFlatBonus;

    [DataField]
    public float RightEyeProjectileSpeedBonus;

    [DataField]
    public float RightEyeFallbackChance;

    [DataField]
    public float NonTearDamageProcChance;

    [DataField]
    public float ContactOrProjectileBlockChance;

    [DataField]
    public float ContactBlockKnockback = 2.8f;

    [DataField]
    public float CollisionDamageBase = 8f;

    [DataField]
    public float CollisionDamagePerFloor = 2f;

    [DataField]
    public float RangeFlatBonus;

    [DataField]
    public float RangeMultiplier = 1f;


    [DataField]
    public List<DeepMaintenanceFamiliarConfig> FamiliarConfigs = new();

    [DataField]
    public float OnEnemyKillRoomDamageBonus;

    [DataField]
    public float OnEnemyKillRoomDamageBonusMax;

    [DataField]
    public List<float> OnDamagedFloorDamageBonusSequence = new();

    [DataField]
    public int MaxHealthBonusOnPickup;

    [DataField]
    public bool FullHealOnPickup;

    [DataField]
    public bool EnemyDeathBurstEnabled;

    [DataField]
    public int EnemyDeathBurstMinProjectiles = 1;

    [DataField]
    public int EnemyDeathBurstMaxProjectiles = 16;

    [DataField]
    public float EnemyDeathBurstProjectilesPerMaxHp = 1f;

    [DataField]
    public float EnemyDeathBurstDamageBase = 3.2f;

    [DataField]
    public float EnemyDeathBurstDamagePerFloor = 0.3f;

    [DataField]
    public float TearHeightBonus;
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

    [DataField]
    public int BasePrice;
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
public sealed partial class DeepMaintenanceFamiliarConfig
{
    [DataField]
    public string Behavior = "Orbit";

    [DataField]
    public int Count = 1;

    [DataField]
    public float FollowDistance = 1f;

    [DataField]
    public float MoveSpeed = 7f;

    [DataField]
    public float ShootInterval = 0.75f;

    [DataField]
    public float ProjectileDamage = 3f;

    [DataField]
    public float ProjectileSpeed = 6f;

    [DataField]
    public bool ShootNearestEnemy = true;

    [DataField]
    public bool ShootFourDirections;

    [DataField]
    public bool ShootAlongPlayerAim;

    [DataField]
    public bool FixedRedTint;

    [DataField]
    public bool IgnorePlayerDamageModifiers = true;

    [DataField]
    public bool UsePlayerCurrentDamage;

    [DataField]
    public float PlayerDamageScale = 1f;

    [DataField]
    public bool CanFreezeOnHit;

    [DataField]
    public float FreezeChance = 1f;

    [DataField]
    public bool FreezeBosses;


    [DataField]
    public float InterceptRadius = 4f;

    [DataField]
    public float InterceptCooldown = 0.4f;

    [DataField]
    public float RestChance = 0.2f;

    [DataField]
    public float RestDuration = 0.5f;

    [DataField]
    public float ContactDps;

    [DataField]
    public bool SpawnBloodTrail;

    [DataField]
    public float BloodTrailLifetime = 2.5f;

    [DataField]
    public float BloodTrailRadius = 0.6f;

    [DataField]
    public float BloodTrailDps;

    [DataField]
    public int RoomRewardEvery;

    [DataField]
    public int RoomRewardAmount = 1;

    [DataField]
    public string RoomRewardPickupType = "Heart";

    [DataField]
    public int BurstCount = 8;

    [DataField]
    public List<float> BurstDamageOptions = new();

    [DataField]
    public float BurstInterval = 1.2f;
}

[DataDefinition]
public sealed partial class DeepMaintenanceResourceGrantEntry
{
    [DataField(required: true)]
    public string ResourceType = default!;

    [DataField]
    public int Amount;
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
