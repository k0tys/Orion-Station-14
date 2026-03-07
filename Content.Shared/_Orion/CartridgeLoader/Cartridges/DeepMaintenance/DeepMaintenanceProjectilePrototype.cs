using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared._Orion.CartridgeLoader.Cartridges.DeepMaintenance;

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
    public DeepMaintenanceHitboxShape HitboxShape = DeepMaintenanceHitboxShape.Circle;

    [DataField]
    public float HitboxWidth = 0.18f;

    [DataField]
    public float HitboxHeight = 0.18f;

    [DataField]
    public float HitboxOffsetX;

    [DataField]
    public float HitboxOffsetY;

    [DataField]
    public float Speed = 5f;

    [DataField]
    public float Damage = 1f;

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
    public bool BallisticEnabled = true;

    [DataField]
    public float Gravity = 18f;

    [DataField]
    public float InitialHeight;

    [DataField]
    public float InitialVerticalVelocity = 3.2f;

    [DataField]
    public float ArcHeight = 0.5f;

    [DataField]
    public float MaxHeight = 3.5f;

    [DataField]
    public float TerminalFallSpeed = 11f;

    [DataField]
    public bool LandOnZeroHeight = true;

    [DataField]
    public bool CollideOnlyWhenLow;

    [DataField]
    public float CollisionMaxHeight = 0.2f;

    [DataField]
    public bool DestroyOnLanding = true;

    [DataField]
    public bool ImpactOnLanding = true;

    [DataField]
    public float ShadowScaleByHeight = 0.22f;

    [DataField]
    public float SpriteLiftMultiplier = 0.75f;

    [DataField]
    public bool Spectral;

    [DataField]
    public bool Piercing;

    [DataField]
    public int PierceCount;

    [DataField]
    public List<DeepMaintenanceProjectileModifierEntry> Modifiers = new();

    [DataField]
    public bool RotateToVelocity;

    [DataField]
    public float FinalDropStart = 0.85f;

    [DataField]
    public float FinalDropDistance = 0.35f;
}

[DataDefinition]
public sealed partial class DeepMaintenanceProjectileModifierEntry
{
    [DataField(required: true)]
    public string Type = "Fast";

    [DataField]
    public float Strength = 1f;

    [DataField]
    public int Intensity = 1;
}
