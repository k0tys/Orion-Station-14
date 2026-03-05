using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.Array;

namespace Content.Shared._Orion.CartridgeLoader.Cartridges.DeepMaintenance;

[DataDefinition]
public sealed partial class DeepMaintenanceWeightedEntityEntry
{
    [DataField(required: true)]
    public ProtoId<DeepMaintenanceEntityPrototype> Entity;

    [DataField]
    public float Weight = 1f;
}

[DataDefinition]
public sealed partial class DeepMaintenanceMusicRoomEntry
{
    [DataField(required: true)]
    public string RoomType = default!;

    [DataField(required: true)]
    public SoundSpecifier Music = default!;

    [DataField]
    public float Volume = -8f;
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

    [DataField]
    public List<DeepMaintenanceMusicRoomEntry> MusicByRoom = new();

    [DataField]
    public float MusicFadeOut = 0.7f;

    [DataField]
    public float MusicFadeIn = 0.7f;
}
