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
    public bool RotateToVelocity;

    [DataField]
    public float FinalDropStart = 0.85f;

    [DataField]
    public float FinalDropDistance = 0.35f;
}
