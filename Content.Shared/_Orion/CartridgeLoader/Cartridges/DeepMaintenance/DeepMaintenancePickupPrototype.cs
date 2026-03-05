using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared._Orion.CartridgeLoader.Cartridges.DeepMaintenance;

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

    [DataField(required: true)]
    public string PickupType = default!;

    [DataField(required: true)]
    public string SpritePath = default!;

    [DataField(required: true)]
    public string SpriteState = default!;

    [DataField]
    public float SpriteScale = 1f;

    [DataField]
    public int BasePrice;

    [DataField]
    public float PickupRadius = 0.85f;

    [DataField]
    public float CollisionRadius = 0.28f;

    [DataField]
    public float SpawnAnimationDuration = 0.24f;

    [DataField]
    public float BombFuseSeconds = 1.35f;

    [DataField]
    public float BombExplosionRadius = 1.65f;

    [DataField]
    public int BombEnemyDamage = 4;

    [DataField]
    public float BombObjectDamageRadius = 1.3f;

    [DataField]
    public float SecretRevealBombRadius = 1.4f;

    [DataField]
    public float BombExplosionVisualDuration = 0.28f;

    [DataField]
    public SoundSpecifier? BombExplosionSound;
}
