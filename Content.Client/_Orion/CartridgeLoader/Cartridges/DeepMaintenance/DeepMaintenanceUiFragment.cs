using System.Linq;
using System.Numerics;
using Content.Client.Resources;
using Content.Shared._Orion.CartridgeLoader.Cartridges.DeepMaintenance;
using Content.Shared.EntityTable;
using Content.Shared.Input;
using Robust.Client.Audio;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Prototypes;

namespace Content.Client._Orion.CartridgeLoader.Cartridges.DeepMaintenance;

public sealed partial class DeepMaintenanceUiFragment : BoxContainer
{
    private readonly Label _hud;
    private readonly Label _status;
    private readonly DeepMaintenanceGameControl _game;

    public DeepMaintenanceUiFragment()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;

        _hud = new Label { Margin = new Thickness(4, 2, 4, 2) };
        _status = new Label { Margin = new Thickness(4, 0, 4, 4) };

        _game = new DeepMaintenanceGameControl
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            MinSize = new Vector2(192, 128),
            Margin = new Thickness(4, 0, 4, 4)
        };
        _game.StateChanged += UpdateHud;

        AddChild(_hud);
        AddChild(_status);
        AddChild(_game);
        AddChild(BuildButtons());

        UpdateHud();
    }

    private Control BuildButtons()
    {
        var controls = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
            HorizontalExpand = true,
            SeparationOverride = 8,
            Margin = new Thickness(4),
        };

        controls.AddChild(MakeButton(Loc.GetString("deep-maintenance-ui-button-pause"), _game.TogglePause));
        controls.AddChild(MakeButton(Loc.GetString("deep-maintenance-ui-button-restart"), _game.Restart));

        return controls;

        Button MakeButton(string text, Action action)
        {
            var button = new Button { Text = text };
            button.OnPressed += _ => action();
            return button;
        }
    }

    private void UpdateHud()
    {
        _hud.Text = Loc.GetString("deep-maintenance-ui-hud", ("room", _game.RoomIndex + 1), ("rooms", _game.RoomCount), ("enemies", _game.AliveEnemies), ("floor", _game.Floor), ("coins", _game.Coins), ("keys", _game.Keys), ("bombs", _game.Bombs));
        _status.Text = _game.BossName == null
            ? _game.Status
            : $"{_game.Status} | {_game.BossName}: {_game.BossHp}/{_game.BossMaxHp}";
    }

    public void EnsureInputFocus()
    {
        _game.EnsureInputFocus();
    }

    private sealed partial class DeepMaintenanceGameControl : Control
    {
        [Dependency] private readonly IPrototypeManager _prototype = default!;
        [Dependency] private readonly IEntityManager _entity = default!;
        [Dependency] private readonly IInputManager _input = default!;
        [Dependency] private readonly IResourceCache _resourceCache = default!;
        [Dependency] private readonly IConfigurationManager _cfg = default!;

        private readonly SpriteSystem _sprite;
        private readonly AudioSystem _audio;
        private readonly EntityTableSystem _entityTable;
        private readonly Random _random = new();
        private readonly Dictionary<(string, string), Texture> _spriteCache = new();
        private readonly Dictionary<(string, string, RsiDirection), Texture> _directionalSpriteCache = new();
        private readonly Dictionary<RoomType, DeepMaintenanceDoorPrototype> _doorPrototypes = new();

        private readonly List<RoomData> _rooms = new();
        private readonly List<ProjectileData> _playerProjectiles = new();
        private readonly List<ProjectileData> _enemyProjectiles = new();
        private readonly HashSet<int> _visitedRooms = new();
        private readonly HashSet<int> _knownRooms = new();
        private readonly HashSet<string> _runSeenRelicIds = new();

        private readonly DeepMaintenanceInputState _inputState;

        private DeepMaintenanceEntityPrototype _playerProto = default!;
        private DeepMaintenanceEntityPrototype _chaserProto = default!;
        private DeepMaintenanceEntityPrototype _shooterProto = default!;
        private DeepMaintenanceEntityPrototype _bossProto = default!;
        private readonly Dictionary<int, DeepMaintenanceFloorPrototype> _floorConfigs = new();
        private DeepMaintenanceFloorPrototype _activeFloorConfig = default!;
        private float _roomBaseLight = 0.72f;
        private float _roomVignetteStrength = 0.2f;
        private float _playerLightRadius = 3.8f;
        private float _playerLightStrength = 0.5f;

        private DeepMaintenanceTreasurePrototype _treasurePrototype = default!;
        private bool _hasTreasurePrototype;
        private readonly List<DeepMaintenanceRelicPrototype> _activeRelics = new();

        private DeepMaintenanceTilePrototype _floorProto = default!;
        private DeepMaintenanceTilePrototype _wallProto = default!;
        private DeepMaintenanceTilePrototype _obstacleProto = default!;
        private DeepMaintenanceTilePrototype _mushroomProto = default!;

        private DeepMaintenancePickupPrototype _coinPickupProto = default!;
        private DeepMaintenancePickupPrototype _bombPickupProto = default!;
        private DeepMaintenancePickupPrototype _heartPickupProto = default!;

        private Vector2 _playerPos;
        private Vector2 _playerVelocity;
        private FacingDirection _playerBodyFacing = FacingDirection.Down;
        private FacingDirection _playerShootFacing = FacingDirection.Down;
        private float _playerBodyFacingResetTimer;
        private float _playerShootFacingResetTimer;
        private float _playerShootCooldown;
        private float _playerShootAnimationTimer;
        private int _invulnerabilityTicks;
        private bool _paused;
        private bool _gameOver;
        private bool _victory;
        private float _accumulator;
        private float _heartDamageFlash;
        private float _playerDamageFlash;
        private float _meleeSwingTimer;
        private float _animationClock;
        private Vector2 _lastMousePosition;
        private FacingDirection _meleeSwingFacing = FacingDirection.Down;
        private float _emoteTimer;

        private readonly Font _shopPriceFont;
        private readonly Font _tooltipFont;

        private Vector2? _treasureBoxPosition;
        private bool _treasureBoxOpened;
        private Vector2? _treasureRelicPosition;
        private string? _treasureRelicId;
        private bool _treasureOpeningAnimation;
        private float _treasureOpenAnimationTimer;
        private bool _treasurePendingEnemySpawn;
        private bool _treasurePendingRelicSpawn;
        private float _treasureRelicPickupGraceTimer;
        private float _treasureRelicAppearTimer;

        private bool _debugHitboxes;

        private int _coins;
        private int _keys;
        private int _bombs;
        private readonly List<BombData> _activeBombs = new();
        private readonly List<BombExplosionData> _bombExplosions = new();
        private float _chainLightningAccumulator;
        private bool _tookDamageInRoom;
        private float _electroRakRoomFireRateBonus;
        private int _electroRakDamageTriggersInRoom;
        private float _rhythmicKnifeBonus;
        private bool _nextShotLeftEye = true;
        private bool _claymoreCharging;
        private float _claymoreChargeTimer;
        private Vector2 _claymoreChargeDirection = Vector2.UnitX;

        private readonly List<FamiliarData> _familiars = new();
        private readonly List<BloodTrailData> _bloodTrails = new();
        private Vector2 _lastPlayerShotDirection = Vector2.UnitX;
        private float _roomKillDamageBonus;
        private float _floorDamageBonus;
        private int _floorDamageBonusStacks;
        private int _roomsEnteredCounter;

        private const int GridWidth = 12;
        private const int GridHeight = 11;
        private const float TickSeconds = 0.1f;
        private const int InvulnerabilityTicks = 10;
        private const float DoorTransitionMargin = 0.05f;
        private const float DoorSpawnExclusionRadius = 2f;
        private const float DoorInnerSafeZoneDepth = 2f;
        private const float EntitySpriteTileSize = 1f;
        private const float FacingResetDelaySeconds = 0.18f;
        private const float EnemyHitKnockback = 0.22f;
        private const float BossSpreadAngleDegrees = 22f;
        private const int EnemyAggroDelayTicksMin = 3;
        private const int EnemyAggroDelayTicksMax = 7;
        private const int EnemyEscapeWallContactThreshold = 6;
        private const int EnemyEscapeTicks = 8;
        private const float EnemyEscapeSpeedMultiplier = 1.22f;
        private const float ShootAnimationDuration = 0.14f;
        private const float EnemyVisionSamplesPerTile = 2.5f;
        private const int EnemyAvoidanceLockTicks = 8;
        private const float EnemyAvoidanceCheckDistance = 0.8f;

        private const int MaxBombs = 99;
        private const int MaxKeys = 99;
        private const float BombTimerSeconds = 1.35f;
        private const float BombExplosionRadius = 1.65f;
        private const int BombEnemyDamage = 4;
        private const float BombObjectDamageRadius = 1.3f;
        private const float BombExplosionVisualDuration = 0.28f;
        private const float SecretRevealBombRadius = 1.4f;
        private const float PickupRadius = 0.85f;
        private const float PickupCollisionRadius = 0.28f;
        private const float FamiliarCollisionRadius = 0.22f;
        private const float EntitySeparationBias = 0.05f;
        private const float PickupPushStrength = 0.8f;
        private const float PickupSpawnAnimationDuration = 0.24f;
        private const float ShopPurchaseRadius = 0.72f;
        private const int RoomClearCoinMin = 1;
        private const int RoomClearCoinMax = 3;
        private const int ShopSlotCount = 3;
        private const float EmoteAnimationDuration = 0.45f;
        private const string EmotePlaceholderState = "emote";

        private const float TreasureObjectRadius = 0.34f;
        private const double TreasureEnemySpawnChance = 0.1;
        private const double TreasureShooterSpawnChance = 0.5;
        private const string TreasurePrototypeId = "TreasureConfig";
        private const float MeleeSwingDuration = 0.12f;
        private const int TreasureEnemySpawnGraceTicks = 6;

        private const int TotalFloors = 6;
        private const int FirstFloorMinRooms = 8;
        private const int FloorMaxRooms = 18;

        private int _currentFloor = 1;
        private bool _floorExitSpawned;
        private Vector2? _floorExitPosition;

        private const string EntityPlayerPrototypeId = "Player";
        private const string EntityChaserPrototypeId = "Chaser";
        private const string EntityShooterPrototypeId = "Shooter";
        private const string EntityBossPrototypeId = "Boss";

        private const string TileFloorPrototypeId = "Floor";
        private const string TileWallPrototypeId = "Wall";
        private const string TileObstaclePrototypeId = "Obstacle";
        private const string TileMushroomPrototypeId = "Mushroom";

        private const string PickupCoinPrototypeId = "PickupCoin";
        private const string PickupBombPrototypeId = "PickupBomb";
        private const string PickupHeartPrototypeId = "PickupHeart";

        private static readonly SoundSpecifier SfxPlayerShoot = new SoundPathSpecifier("/Audio/Weapons/pop.ogg");
        private static readonly SoundSpecifier SfxEnemyShoot = new SoundPathSpecifier("/Audio/Weapons/emitter.ogg");
        private static readonly SoundSpecifier SfxProjectileHit = new SoundPathSpecifier("/Audio/Effects/weak_hit1.ogg");
        private static readonly SoundSpecifier SfxPlayerDamage = new SoundPathSpecifier("/Audio/Effects/hit_kick.ogg");
        private static readonly SoundSpecifier SfxEnemyDeath = new SoundPathSpecifier("/Audio/Effects/bodyfall1.ogg");
        private static readonly SoundSpecifier SfxPlayerDeath = new SoundPathSpecifier("/Audio/Effects/tesla_collapse.ogg");
        private static readonly SoundSpecifier SfxPlayerEmote = new SoundPathSpecifier("/Audio/Effects/pop_expl.ogg");

        private const string HeartSpritePath = "/Textures/_Orion/DeepMaintenance/HUD/hearts.rsi";
        private const string HeartFullState = "full";
        private const string HeartHalfState = "half";
        private const string HeartEmptyState = "empty";
        private const float HeartDamageFlashDuration = 0.35f;
        private const float EntityDamageFlashDuration = 0.2f;
        private const float HeartRowsStartY = 6f;
        private const float DefaultSpritePixelsPerTile = 32f;
        private const string BombPrimedState = "primed";
        private const string TreasureOpeningState = "opening";
        private const float ProjectileRadiusMin = 0.06f;
        private const float ProjectileRadiusMax = 1.1f;

        public event Action? StateChanged;

        public int PlayerHp { get; private set; }
        public int MaxPlayerHp { get; private set; }
        public int RoomIndex { get; private set; }
        public int RoomCount => _rooms.Count;
        public int Coins => _coins;
        public int Bombs => _bombs;
        public int Keys => _keys;
        public int Floor => _currentFloor;
        public int AliveEnemies
        {
            get
            {
                if (_rooms.Count == 0 || RoomIndex < 0 || RoomIndex >= _rooms.Count)
                    return 0;

                return _rooms[RoomIndex].Enemies.Count(e => e.Hp > 0);
            }
        }

        public string? BossName
        {
            get
            {
                var boss = GetCurrentBoss();
                return boss == null ? null : GetBossDisplayName(boss.Prototype.ID);
            }
        }

        public int BossHp => GetCurrentBoss()?.Hp ?? 0;
        public int BossMaxHp => GetCurrentBoss()?.Prototype.MaxHp ?? 0;

        public string Status
        {
            get
            {
                var baseStatus = _victory
                    ? Loc.GetString("deep-maintenance-ui-status-victory")
                    : _gameOver
                        ? Loc.GetString("deep-maintenance-ui-status-game-over")
                        : _paused
                            ? Loc.GetString("deep-maintenance-ui-status-paused")
                            : Loc.GetString("deep-maintenance-ui-status-controls");

                return baseStatus;
            }
        }

        private RoomData CurrentRoom => _rooms[RoomIndex];

        #region Initialization

        public DeepMaintenanceGameControl()
        {
            IoCManager.InjectDependencies(this);
            _sprite = _entity.System<SpriteSystem>();
            _audio = _entity.System<AudioSystem>();
            _entityTable = _entity.System<EntityTableSystem>();
            _inputState = new DeepMaintenanceInputState(_input);
            _shopPriceFont = _resourceCache.GetFont("/Fonts/NotoSans/NotoSans-Bold.ttf", 10);
            _tooltipFont = _resourceCache.GetFont("/Fonts/NotoSans/NotoSans-Regular.ttf", 11);

            CanKeyboardFocus = true;
            KeyboardFocusOnClick = true;

            LoadPrototypes();
            Restart();
        }

        protected override void EnteredTree()
        {
            base.EnteredTree();
            _input.FirstChanceOnKeyEvent += OnFirstChanceKeyEvent;
            EnsureInputFocus();
        }

        protected override void ExitedTree()
        {
            base.ExitedTree();

            _input.FirstChanceOnKeyEvent -= OnFirstChanceKeyEvent;
            _inputState.Clear();
            if (HasKeyboardFocus())
                ReleaseKeyboardFocus();
        }

        #endregion

        #region Input

        private void OnFirstChanceKeyEvent(KeyEventArgs keyEvent, KeyEventType type)
        {
            if (!IsInsideTree || !HasKeyboardFocus() || _paused || _gameOver || _victory)
                return;

            if (keyEvent.Handled)
                return;

            if (!_inputState.TryGetBoundFunction(keyEvent, out var function))
                return;

            switch (type)
            {
                case KeyEventType.Down:
                    _inputState.Add(function);

                    if (DeepMaintenanceInputState.TryGetShootDirection(function, out var shootDirection))
                        TryShoot(shootDirection);

                    if (function == ContentKeyFunctions.DeepMaintenanceBomb)
                        TryPlaceBomb();

                    if (function == ContentKeyFunctions.Arcade3)
                        TriggerEmote();

                    keyEvent.Handle();
                    break;
                case KeyEventType.Up:
                    _inputState.Remove(function);
                    keyEvent.Handle();
                    break;
            }
        }

        public void EnsureInputFocus()
        {
            if (_paused || _gameOver || _victory)
                return;

            if (!IsInsideTree || !CanKeyboardFocus || HasKeyboardFocus())
                return;

            GrabKeyboardFocus();
        }

        public void Restart()
        {
            _rooms.Clear();
            _visitedRooms.Clear();
            _knownRooms.Clear();
            _runSeenRelicIds.Clear();
            _playerProjectiles.Clear();
            _enemyProjectiles.Clear();
            _playerVelocity = Vector2.Zero;
            _activeRelics.Clear();
            _activeBombs.Clear();
            _bombExplosions.Clear();
            _inputState.Clear();

            RoomIndex = 0;
            SetPlayerHealth(_playerProto.MaxHp, _playerProto.MaxHp);
            _playerPos = new Vector2(GridWidth * 0.5f, GridHeight * 0.5f);
            _playerVelocity = Vector2.Zero;
            _playerShootCooldown = 0f;
            _playerShootAnimationTimer = 0f;
            _playerBodyFacingResetTimer = 0f;
            _playerShootFacingResetTimer = 0f;
            _playerBodyFacing = FacingDirection.Down;
            _playerShootFacing = FacingDirection.Down;
            _invulnerabilityTicks = 0;
            _paused = false;
            _currentFloor = 1;
            _gameOver = false;
            _victory = false;
            _accumulator = 0f;
            _heartDamageFlash = 0f;
            _playerDamageFlash = 0f;
            _meleeSwingTimer = 0f;
            _emoteTimer = 0f;
            _floorExitSpawned = false;
            _floorExitPosition = null;
            _treasureOpeningAnimation = false;
            _treasureOpenAnimationTimer = 0f;
            _treasurePendingEnemySpawn = false;
            _treasurePendingRelicSpawn = false;
            _treasureRelicPickupGraceTimer = 0f;
            _treasureRelicAppearTimer = 0f;
            _coins = 0;
            _keys = 0;
            _bombs = 1;
            _chainLightningAccumulator = 0f;
            _tookDamageInRoom = false;
            _electroRakRoomFireRateBonus = 0f;
            _electroRakDamageTriggersInRoom = 0;
            _roomKillDamageBonus = 0f;
            _claymoreCharging = false;
            _claymoreChargeTimer = 0f;
            _rhythmicKnifeBonus = 0f;
            _nextShotLeftEye = true;
            _claymoreCharging = false;
            _claymoreChargeTimer = 0f;
            _familiars.Clear();
            _bloodTrails.Clear();
            _lastPlayerShotDirection = Vector2.UnitX;
            _roomKillDamageBonus = 0f;
            _floorDamageBonus = 0f;
            _floorDamageBonusStacks = 0;
            _roomsEnteredCounter = 0;

            ApplyFloorTheme(_currentFloor);
            GenerateMap();
            EnterRoom(0, true);

            EnsureInputFocus();
            StateChanged?.Invoke();
            InvalidateMeasure();
        }

        public void TogglePause()
        {
            if (_gameOver || _victory)
                return;

            _paused = !_paused;
            if (_paused && HasKeyboardFocus())
                ReleaseKeyboardFocus();
            else
                EnsureInputFocus();

            StateChanged?.Invoke();
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            base.KeyBindDown(args);
            EnsureInputFocus();

            if (!_inputState.IsRelevantKey(args.Function))
                return;

            _inputState.Add(args.Function);

            if (DeepMaintenanceInputState.TryGetShootDirection(args.Function, out var shootDirection))
                TryShoot(shootDirection);

            if (args.Function == ContentKeyFunctions.DeepMaintenanceBomb)
                TryPlaceBomb();

            if (args.Function == ContentKeyFunctions.Arcade3)
                TriggerEmote();

            args.Handle();
        }

        protected override void KeyBindUp(GUIBoundKeyEventArgs args)
        {
            base.KeyBindUp(args);

            if (!_inputState.IsRelevantKey(args.Function))
                return;

            _inputState.Remove(args.Function);
            args.Handle();
        }

        protected override void MouseMove(GUIMouseMoveEventArgs args)
        {
            base.MouseMove(args);
            _lastMousePosition = args.RelativePosition;
        }

        #endregion
    }
}
