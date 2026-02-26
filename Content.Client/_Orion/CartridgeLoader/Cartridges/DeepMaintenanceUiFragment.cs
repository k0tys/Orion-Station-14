using System.Linq;
using System.Numerics;
using Content.Shared._Orion.CartridgeLoader.Cartridges;
using Content.Shared.CCVar;
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
using Robust.Shared.Input;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Orion.CartridgeLoader.Cartridges;

public sealed class DeepMaintenanceUiFragment : BoxContainer
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
        _hud.Text = Loc.GetString("deep-maintenance-ui-hud", ("room", _game.RoomIndex + 1), ("rooms", _game.RoomCount), ("enemies", _game.AliveEnemies));
        _status.Text = _game.Status;
    }

    public void EnsureInputFocus()
    {
        _game.EnsureInputFocus();
    }

    private sealed class DeepMaintenanceGameControl : Control
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

        private readonly DeepMaintenanceInputState _inputState;

        private DeepMaintenanceEntityPrototype _playerProto = default!;
        private DeepMaintenanceEntityPrototype _chaserProto = default!;
        private DeepMaintenanceEntityPrototype _shooterProto = default!;
        private DeepMaintenanceEntityPrototype _bossProto = default!;

        private DeepMaintenanceTreasurePrototype _treasurePrototype = default!;
        private bool _hasTreasurePrototype;
        private readonly List<DeepMaintenanceRelicPrototype> _activeRelics = new();

        private DeepMaintenanceTilePrototype _floorProto = default!;
        private DeepMaintenanceTilePrototype _wallProto = default!;
        private DeepMaintenanceTilePrototype _obstacleProto = default!;
        private DeepMaintenanceTilePrototype _mushroomProto = default!;

        private Vector2 _playerPos;
        private Vector2 _playerVelocity;
        private FacingDirection _playerBodyFacing = FacingDirection.Down;
        private FacingDirection _playerShootFacing = FacingDirection.Down;
        private float _playerBodyFacingResetTimer;
        private float _playerShootFacingResetTimer;
        private float _playerShootCooldown;
        private int _invulnerabilityTicks;
        private bool _paused;
        private bool _gameOver;
        private bool _victory;
        private float _accumulator;
        private float _heartDamageFlash;
        private float _playerDamageFlash;
        private float _meleeSwingTimer;
        private FacingDirection _meleeSwingFacing = FacingDirection.Down;

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

        private const int GridWidth = 12;
        private const int GridHeight = 13;
        private const float TickSeconds = 0.1f;
        private const int InvulnerabilityTicks = 10;
        private const float DoorTransitionMargin = 0.05f;
        private const float EntitySpriteTileSize = 1f;
        private const float FacingResetDelaySeconds = 0.18f;
        private const float EnemyHitKnockback = 0.22f;
        private const float BossSpreadAngleDegrees = 22f;
        private const int EnemyAggroDelayTicksMin = 3;
        private const int EnemyAggroDelayTicksMax = 7;
        private const int EnemyEscapeWallContactThreshold = 6;
        private const int EnemyEscapeTicks = 8;
        private const float EnemyEscapeSpeedMultiplier = 1.22f;

        private const float TreasureObjectRadius = 0.34f;
        private const double TreasureEnemySpawnChance = 0.1;
        private const double TreasureShooterSpawnChance = 0.5;
        private const string TreasurePrototypeId = "DeepMaintenanceTreasureConfig";
        private const float MeleeSwingDuration = 0.12f;
        private const int TreasureEnemySpawnGraceTicks = 6;

        private const int TotalFloors = 6;
        private const int FirstFloorMinRooms = 8;
        private const int FloorMaxRooms = 18;

        private int _currentFloor = 1;
        private bool _floorExitSpawned;
        private Vector2? _floorExitPosition;

        private const string EntityPlayerPrototypeId = "DeepMaintenancePlayer";
        private const string EntityChaserPrototypeId = "DeepMaintenanceChaser";
        private const string EntityShooterPrototypeId = "DeepMaintenanceShooter";
        private const string EntityBossPrototypeId = "DeepMaintenanceBoss";

        private const string TileFloorPrototypeId = "DeepMaintenanceFloor";
        private const string TileWallPrototypeId = "DeepMaintenanceWall";
        private const string TileObstaclePrototypeId = "DeepMaintenanceObstacle";

        private static readonly SoundSpecifier SfxPlayerShoot = new SoundPathSpecifier("/Audio/Weapons/pop.ogg");
        private static readonly SoundSpecifier SfxEnemyShoot = new SoundPathSpecifier("/Audio/Weapons/emitter.ogg");
        private static readonly SoundSpecifier SfxProjectileHit = new SoundPathSpecifier("/Audio/Effects/weak_hit1.ogg");
        private static readonly SoundSpecifier SfxPlayerDamage = new SoundPathSpecifier("/Audio/Effects/hit_kick.ogg");
        private static readonly SoundSpecifier SfxEnemyDeath = new SoundPathSpecifier("/Audio/Effects/bodyfall1.ogg");
        private static readonly SoundSpecifier SfxPlayerDeath = new SoundPathSpecifier("/Audio/Effects/tesla_collapse.ogg");

        private const string HeartSpritePath = "/Textures/_Orion/DeepMaintenance/HUD/hearts.rsi";
        private const string HeartFullState = "full";
        private const string HeartHalfState = "half";
        private const string HeartEmptyState = "empty";
        private const float HeartDamageFlashDuration = 0.35f;
        private const float EntityDamageFlashDuration = 0.2f;
        private const float HeartRowsStartY = 6f;
        private const float DefaultSpritePixelsPerTile = 32f;

        public event Action? StateChanged;

        public int PlayerHp { get; private set; }
        public int MaxPlayerHp { get; private set; }
        public int RoomIndex { get; private set; }
        public int RoomCount => _rooms.Count;
        public int AliveEnemies
        {
            get
            {
                if (_rooms.Count == 0 || RoomIndex < 0 || RoomIndex >= _rooms.Count)
                    return 0;

                return _rooms[RoomIndex].Enemies.Count(e => e.Hp > 0);
            }
        }

        public string Status => _victory
            ? Loc.GetString("deep-maintenance-ui-status-victory")
            : _gameOver
                ? Loc.GetString("deep-maintenance-ui-status-game-over")
                : _paused
                    ? Loc.GetString("deep-maintenance-ui-status-paused")
                    : Loc.GetString("deep-maintenance-ui-status-controls");

        private RoomData CurrentRoom => _rooms[RoomIndex];

        #region Initialization

        public DeepMaintenanceGameControl()
        {
            IoCManager.InjectDependencies(this);
            _sprite = _entity.System<SpriteSystem>();
            _audio = _entity.System<AudioSystem>();
            _entityTable = _entity.System<EntityTableSystem>();
            _inputState = new DeepMaintenanceInputState(_input);

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
            _playerProjectiles.Clear();
            _enemyProjectiles.Clear();
            _playerVelocity = Vector2.Zero;
            _activeRelics.Clear();
            _inputState.Clear();

            RoomIndex = 0;
            SetPlayerHealth(_playerProto.MaxHp, _playerProto.MaxHp);
            _playerPos = new Vector2(GridWidth * 0.5f, GridHeight * 0.5f);
            _playerVelocity = Vector2.Zero;
            _playerShootCooldown = 0f;
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
            _floorExitSpawned = false;
            _floorExitPosition = null;
            _treasureOpeningAnimation = false;
            _treasureOpenAnimationTimer = 0f;
            _treasurePendingEnemySpawn = false;
            _treasurePendingRelicSpawn = false;
            _treasureRelicPickupGraceTimer = 0f;
            _treasureRelicAppearTimer = 0f;

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

        #endregion

        #region Update

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);

            _debugHitboxes = _cfg.GetCVar(CCVars.DeepMaintenanceDebugHitboxes);

            if (_paused || _gameOver || _victory)
                return;

            MovePlayer(args.DeltaSeconds);

            if (_heartDamageFlash > 0f)
                _heartDamageFlash = MathF.Max(0f, _heartDamageFlash - args.DeltaSeconds);

            if (_playerDamageFlash > 0f)
                _playerDamageFlash = MathF.Max(0f, _playerDamageFlash - args.DeltaSeconds);

            _accumulator += args.DeltaSeconds;
            while (_accumulator >= TickSeconds)
            {
                _accumulator -= TickSeconds;
                TickGame(TickSeconds);
            }
        }

        private void TickGame(float dt)
        {
            if (_playerShootCooldown > 0f)
                _playerShootCooldown = MathF.Max(0f, _playerShootCooldown - dt);

            if (_invulnerabilityTicks > 0)
                _invulnerabilityTicks--;

            HandleHeldShootKeys();
            UpdateFacingResetTimers(dt);
            TickDamageFlashes(dt);
            TickMeleeSwing(dt);
            TickTreasureAnimations(dt);
            TickEnemies(dt);
            TickProjectiles(_playerProjectiles, true, dt);
            TickProjectiles(_enemyProjectiles, false, dt);
            HandleContactDamage();
            HandleTreasureInteractions();
            HandleRoomState();
            TickDoorAnimations();

            InvalidateMeasure();
            StateChanged?.Invoke();
        }

        private void TickTreasureAnimations(float dt)
        {
            if (_treasureOpenAnimationTimer > 0f)
                _treasureOpenAnimationTimer = MathF.Max(0f, _treasureOpenAnimationTimer - dt);

            if (_treasureRelicPickupGraceTimer > 0f)
                _treasureRelicPickupGraceTimer = MathF.Max(0f, _treasureRelicPickupGraceTimer - dt);

            if (_treasureRelicAppearTimer > 0f)
                _treasureRelicAppearTimer = MathF.Max(0f, _treasureRelicAppearTimer - dt);

            if (!_treasureOpeningAnimation || _treasureOpenAnimationTimer > 0f)
                return;

            _treasureOpeningAnimation = false;
            ResolveTreasureSpawnAfterOpen();
        }

        private void TickDamageFlashes(float dt)
        {
            foreach (var enemy in CurrentRoom.Enemies)
            {
                if (enemy.DamageFlash <= 0f)
                    continue;

                enemy.DamageFlash = MathF.Max(0f, enemy.DamageFlash - dt);
            }
        }

        private void TickMeleeSwing(float dt)
        {
            if (_meleeSwingTimer <= 0f)
                return;

            _meleeSwingTimer = MathF.Max(0f, _meleeSwingTimer - dt);
        }

        private void MovePlayer(float dt)
        {
            var moveDirection = GetMoveDirection();
            var targetVelocity = moveDirection * _playerProto.MoveSpeed;
            var acceleration = MathF.Max(0f, _playerProto.MoveAcceleration);
            var friction = MathF.Max(0f, _playerProto.MoveFriction);
            var blend = moveDirection == Vector2.Zero
                ? Math.Clamp(friction * dt, 0f, 1f)
                : Math.Clamp(acceleration * dt, 0f, 1f);

            _playerVelocity = Vector2.Lerp(_playerVelocity, targetVelocity, blend);
            if (_playerVelocity.LengthSquared() <= 0.0002f)
                _playerVelocity = Vector2.Zero;

            if (_playerVelocity != Vector2.Zero)
            {
                _playerBodyFacing = FacingFromVector(_playerVelocity, _playerBodyFacing);
                _playerBodyFacingResetTimer = FacingResetDelaySeconds;
            }

            var target = _playerPos + _playerVelocity * dt;
            _playerPos = ResolveCircleTileCollision(target, _playerProto.Radius, CurrentRoom);
            TryRoomTransition();
        }

        private void HandleHeldShootKeys()
        {
            if (_playerShootCooldown > 0f)
                return;

            var shootDirection = GetHeldShootDirection();
            if (shootDirection == Vector2.Zero)
                return;

            TryShoot(shootDirection);
        }

        private void UpdateFacingResetTimers(float dt)
        {
            if (_inputState.AnyShootKeyHeld())
            {
                _playerShootFacingResetTimer = FacingResetDelaySeconds;
            }
            else if (_playerShootFacingResetTimer > 0f)
            {
                _playerShootFacingResetTimer = MathF.Max(0f, _playerShootFacingResetTimer - dt);
                if (_playerShootFacingResetTimer <= 0f)
                    _playerShootFacing = FacingDirection.Down;
            }

            if (_playerVelocity != Vector2.Zero)
            {
                _playerBodyFacingResetTimer = FacingResetDelaySeconds;
            }
            else if (_playerBodyFacingResetTimer > 0f)
            {
                _playerBodyFacingResetTimer = MathF.Max(0f, _playerBodyFacingResetTimer - dt);
                if (_playerBodyFacingResetTimer <= 0f)
                    _playerBodyFacing = FacingDirection.Down;
            }
        }

        private Vector2 GetMoveDirection()
        {
            var x = 0;
            var y = 0;

            if (IsMoveUpHeld())
                y -= 1;
            if (IsMoveDownHeld())
                y += 1;
            if (IsMoveLeftHeld())
                x -= 1;
            if (IsMoveRightHeld())
                x += 1;

            return NormalizeSafe(new Vector2(x, y));
        }

        private Vector2 GetHeldShootDirection()
        {
            var x = 0;
            var y = 0;

            if (IsShootUpHeld())
                y -= 1;
            if (IsShootDownHeld())
                y += 1;
            if (IsShootLeftHeld())
                x -= 1;
            if (IsShootRightHeld())
                x += 1;

            return NormalizeSafe(new Vector2(x, y));
        }

        private bool IsMoveUpHeld()
        {
            return _inputState.IsMoveUpHeld();
        }

        private bool IsMoveDownHeld()
        {
            return _inputState.IsMoveDownHeld();
        }

        private bool IsMoveLeftHeld()
        {
            return _inputState.IsMoveLeftHeld();
        }

        private bool IsMoveRightHeld()
        {
            return _inputState.IsMoveRightHeld();
        }

        private bool IsShootUpHeld()
        {
            return _inputState.IsShootUpHeld();
        }

        private bool IsShootDownHeld()
        {
            return _inputState.IsShootDownHeld();
        }

        private bool IsShootLeftHeld()
        {
            return _inputState.IsShootLeftHeld();
        }

        private bool IsShootRightHeld()
        {
            return _inputState.IsShootRightHeld();
        }

        private void TickEnemies(float dt)
        {
            foreach (var enemy in CurrentRoom.Enemies.Where(enemy => enemy.Hp > 0))
            {
                enemy.PreviousPosition = enemy.Position;

                if (enemy.SpawnGraceTicks > 0)
                    enemy.SpawnGraceTicks--;

                var toPlayer = _playerPos - enemy.Position;
                var predictedPlayerPos = _playerPos + _playerVelocity * 0.4f;
                var toPredictedPlayer = predictedPlayerPos - enemy.Position;
                var directionToPlayer = NormalizeSafe(toPlayer);
                var directionToPredictedPlayer = NormalizeSafe(toPredictedPlayer);
                enemy.ShootFacing = FacingFromVector(directionToPlayer, enemy.ShootFacing);

                if (enemy.AggroDelayTicks > 0)
                {
                    enemy.AggroDelayTicks--;
                    continue;
                }

                if (enemy.ShootCooldownTicks > 0)
                    enemy.ShootCooldownTicks--;

                var escaped = TryEscapeWallTrap(enemy, dt);
                if (escaped)
                    continue;

                if (enemy.Prototype.Shooter)
                {
                    if (enemy.ShootCooldownTicks <= 0)
                    {
                        FireEnemyProjectiles(enemy, directionToPlayer);
                        PlaySfx(SfxEnemyShoot, -10f);

                        enemy.ShootCooldownTicks = enemy.Prototype.ShootCooldownTicks;
                    }

                    if (enemy.Prototype.CanStrafe)
                    {
                        if (enemy.StrafeSwapTicks <= 0)
                        {
                            enemy.StrafeDirection *= -1;
                            enemy.StrafeSwapTicks = _random.Next(6, 14);
                        }
                        else
                        {
                            enemy.StrafeSwapTicks--;
                        }

                        var toPlayerDistance = toPlayer.Length();
                        var strafe = new Vector2(-directionToPredictedPlayer.Y, directionToPredictedPlayer.X) * enemy.StrafeDirection;
                        var kiting = Vector2.Zero;
                        if (toPlayerDistance < 3.2f)
                            kiting = -directionToPredictedPlayer * 0.65f;
                        else if (toPlayerDistance > 5.8f)
                            kiting = directionToPredictedPlayer * 0.45f;

                        var movement = NormalizeSafe(strafe + kiting);
                        enemy.BodyFacing = FacingFromVector(movement, enemy.BodyFacing);
                        var target = enemy.Position + movement * enemy.Prototype.MoveSpeed * 0.5f * dt;
                        enemy.Position = ResolveCircleTileCollision(target, enemy.Prototype.Radius, CurrentRoom);
                    }

                    continue;
                }

                if (enemy.StrafeSwapTicks <= 0)
                {
                    enemy.StrafeDirection *= -1;
                    enemy.StrafeSwapTicks = _random.Next(10, 18);
                }
                else
                {
                    enemy.StrafeSwapTicks--;
                }

                var flank = new Vector2(-directionToPredictedPlayer.Y, directionToPredictedPlayer.X) * enemy.StrafeDirection * 0.32f;
                var chaseDirection = NormalizeSafe(directionToPredictedPlayer + flank);
                enemy.BodyFacing = FacingFromVector(chaseDirection, enemy.BodyFacing);
                var chase = enemy.Position + chaseDirection * enemy.Prototype.MoveSpeed * dt;
                enemy.Position = ResolveCircleTileCollision(chase, enemy.Prototype.Radius, CurrentRoom);
            }

            ResolveEntityCollisions(CurrentRoom.Enemies);
        }

        private bool TryEscapeWallTrap(EnemyData enemy, float dt)
        {
            var pushAway = GetWallPushDirection(enemy.Position, enemy.Prototype.Radius, CurrentRoom);
            if (pushAway == Vector2.Zero)
            {
                enemy.WallContactTicks = 0;
                enemy.EscapeTicksRemaining = 0;
                return false;
            }

            enemy.WallContactTicks++;
            if (enemy.WallContactTicks >= EnemyEscapeWallContactThreshold)
                enemy.EscapeTicksRemaining = EnemyEscapeTicks;

            if (enemy.EscapeTicksRemaining <= 0)
                return false;

            enemy.EscapeTicksRemaining--;
            var velocity = pushAway * enemy.Prototype.MoveSpeed * EnemyEscapeSpeedMultiplier * dt;
            enemy.BodyFacing = FacingFromVector(pushAway, enemy.BodyFacing);
            var target = enemy.Position + velocity;
            enemy.Position = ResolveCircleTileCollision(target, enemy.Prototype.Radius, CurrentRoom);
            return true;
        }

        private static Vector2 GetWallPushDirection(Vector2 position, float radius, RoomData room)
        {
            var push = Vector2.Zero;

            if (TouchesBlockedTile(position + new Vector2(-radius, 0f), room))
                push.X += 1f;

            if (TouchesBlockedTile(position + new Vector2(radius, 0f), room))
                push.X -= 1f;

            if (TouchesBlockedTile(position + new Vector2(0f, -radius), room))
                push.Y += 1f;

            if (TouchesBlockedTile(position + new Vector2(0f, radius), room))
                push.Y -= 1f;

            return NormalizeSafe(push);
        }

        private void FireEnemyProjectiles(EnemyData enemy, Vector2 directionToPlayer)
        {
            var projectilePrototype = _prototype.Index<DeepMaintenanceProjectilePrototype>(enemy.Prototype.ProjectilePrototype);
            var baseVelocity = directionToPlayer * projectilePrototype.Speed;

            SpawnProjectile(_enemyProjectiles, enemy.Position, baseVelocity, projectilePrototype, 1f, projectilePrototype.Damage, Color.White);

            if (!enemy.Prototype.IsBoss)
                return;

            const float angle = MathF.PI * BossSpreadAngleDegrees / 180f;
            var leftVelocity = Rotate(baseVelocity, -angle);
            var rightVelocity = Rotate(baseVelocity, angle);
            SpawnProjectile(_enemyProjectiles, enemy.Position, leftVelocity, projectilePrototype, 1f, projectilePrototype.Damage, Color.White);
            SpawnProjectile(_enemyProjectiles, enemy.Position, rightVelocity, projectilePrototype, 1f, projectilePrototype.Damage, Color.White);
        }

        private void ResolveEntityCollisions(List<EnemyData> enemies)
        {
            for (var i = 0; i < enemies.Count; i++)
            {
                var left = enemies[i];
                if (left.Hp <= 0)
                    continue;

                for (var j = i + 1; j < enemies.Count; j++)
                {
                    var right = enemies[j];
                    if (right.Hp <= 0)
                        continue;

                    var delta = right.Position - left.Position;
                    var distance = delta.Length();
                    var minDistance = left.Prototype.Radius + right.Prototype.Radius;

                    if (distance <= 0.0001f)
                    {
                        delta = new Vector2(1f, 0f);
                        distance = 1f;
                    }

                    if (distance >= minDistance)
                        continue;

                    var normal = delta / distance;
                    var correction = (minDistance - distance) * 0.5f;
                    left.Position = ResolveCircleTileCollision(left.Position - normal * correction, left.Prototype.Radius, CurrentRoom);
                    right.Position = ResolveCircleTileCollision(right.Position + normal * correction, right.Prototype.Radius, CurrentRoom);
                }
            }
        }

        private void TickProjectiles(List<ProjectileData> projectiles, bool playerProjectile, float dt)
        {
            for (var i = projectiles.Count - 1; i >= 0; i--)
            {
                var projectile = projectiles[i];
                projectile.PreviousPosition = projectile.Position;
                projectile.Velocity *= MathF.Max(0f, 1f - projectile.Prototype.Drag * dt);
                projectile.Position += projectile.Velocity * dt;
                projectile.Lifetime -= dt;

                if (projectile.Lifetime <= 0f)
                {
                    projectiles.RemoveAt(i);
                    continue;
                }

                if (!InsideMap(projectile.Position) || IsSolid(projectile.Position, CurrentRoom))
                {
                    PlaySfx(SfxProjectileHit, -12f);
                    projectiles.RemoveAt(i);
                    continue;
                }

                if (playerProjectile)
                {
                    if (!TryHitEnemy(projectile))
                        continue;

                    projectiles.RemoveAt(i);
                    continue;
                }

                if (Vector2.Distance(projectile.Position, _playerPos) > projectile.Radius + _playerProto.Radius)
                    continue;

                DamagePlayer();
                PlaySfx(SfxProjectileHit, -10f);
                projectiles.RemoveAt(i);
            }
        }

        private bool TryHitEnemy(ProjectileData projectile)
        {
            foreach (var enemy in CurrentRoom.Enemies)
            {
                if (enemy.Hp <= 0)
                    continue;

                if (Vector2.Distance(projectile.Position, enemy.Position) > projectile.Radius + enemy.Prototype.Radius)
                    continue;

                enemy.Hp -= (int) MathF.Ceiling(projectile.Damage);
                enemy.DamageFlash = EntityDamageFlashDuration;

                var knockbackDir = NormalizeSafe(enemy.Position - projectile.SourcePosition);
                if (knockbackDir != Vector2.Zero)
                {
                    var knockbackTarget = enemy.Position + knockbackDir * EnemyHitKnockback;
                    enemy.Position = ResolveCircleTileCollision(knockbackTarget, enemy.Prototype.Radius, CurrentRoom);
                }

                if (enemy.Hp <= 0)
                    PlaySfx(SfxEnemyDeath, -7f);
                else
                    PlaySfx(SfxProjectileHit, -11f);

                return true;
            }

            return false;
        }

        private void HandleContactDamage()
        {
            if (_invulnerabilityTicks > 0)
                return;

            foreach (var enemy in CurrentRoom.Enemies)
            {
                if (enemy.Hp <= 0 || enemy.SpawnGraceTicks > 0)
                    continue;

                if (Vector2.Distance(_playerPos, enemy.Position) > _playerProto.Radius + enemy.Prototype.Radius)
                    continue;

                DamagePlayer();
                return;
            }
        }

        private void HandleTreasureInteractions()
        {
            if (CurrentRoom.Type != RoomType.Treasure || !_hasTreasurePrototype)
                return;

            if (!_treasureBoxOpened && _treasureBoxPosition is { } boxPosition)
            {
                if (Vector2.Distance(_playerPos, boxPosition) <= _playerProto.Radius + TreasureObjectRadius)
                    OpenTreasureBox();
            }

            if (_treasureRelicPickupGraceTimer > 0f)
                return;

            if (_treasureRelicPosition is not { } relicPosition || string.IsNullOrWhiteSpace(_treasureRelicId))
                return;

            if (Vector2.Distance(_playerPos, relicPosition) > _playerProto.Radius + TreasureObjectRadius)
                return;

            if (!_prototype.TryIndex<DeepMaintenanceRelicPrototype>(_treasureRelicId, out var relicPrototype))
                return;

            PickupRelic(relicPrototype);
            _treasureRelicPosition = null;
            CurrentRoom.TreasureRelicTaken = true;
            StateChanged?.Invoke();
        }

        private void OpenTreasureBox()
        {
            _treasureBoxOpened = true;
            CurrentRoom.TreasureBoxOpened = true;

            _treasurePendingEnemySpawn = _random.NextDouble() < TreasureEnemySpawnChance;
            _treasurePendingRelicSpawn = !_treasurePendingEnemySpawn;
            _treasureOpeningAnimation = true;
            _treasureOpenAnimationTimer = MathF.Max(0f, _treasurePrototype.OpenAnimationDuration);

            if (_treasureOpenAnimationTimer <= 0f)
                ResolveTreasureSpawnAfterOpen();
        }

        private void ResolveTreasureSpawnAfterOpen()
        {
            if (_treasurePendingEnemySpawn)
            {
                _treasurePendingEnemySpawn = false;
                var pos = new Vector2(GridWidth * 0.5f, GridHeight * 0.5f);
                var proto = _random.NextDouble() < TreasureShooterSpawnChance ? _chaserProto : _shooterProto;
                CurrentRoom.Enemies.Add(CreateEnemyData(proto, pos, TreasureEnemySpawnGraceTicks));
                return;
            }

            if (!_treasurePendingRelicSpawn)
                return;

            _treasurePendingRelicSpawn = false;
            var relicId = RollTreasureRelicId();
            if (string.IsNullOrWhiteSpace(relicId))
                return;

            _treasureRelicId = relicId;
            CurrentRoom.TreasureRelicId = relicId;
            CurrentRoom.HasTreasureRelic = true;
            _treasureRelicPosition = new Vector2(GridWidth * 0.5f, GridHeight * 0.5f);
            _treasureRelicPickupGraceTimer = MathF.Max(0f, _treasurePrototype.RelicPickupGraceDuration);
            _treasureRelicAppearTimer = MathF.Max(0f, _treasurePrototype.RelicAppearDuration);
        }

        private string? RollTreasureRelicId()
        {
            if (!_hasTreasurePrototype)
                return null;

            var lootTable = _prototype.Index(_treasurePrototype.LootTable);
            var rolls = _entityTable.GetSpawns(lootTable, _random).ToList();
            return rolls.Count == 0 ? null : rolls[0].ToString();
        }

        private void PickupRelic(DeepMaintenanceRelicPrototype relic)
        {
            if (_activeRelics.Any(active => active.ID == relic.ID))
                return;

            _activeRelics.Add(relic);
            WarmupSprite(relic.HudIconSpritePath, relic.HudIconSpriteState);
            WarmupSprite(relic.VisualEffectSpritePath, relic.VisualEffectSpriteState);
            WarmupSprite(relic.BodyAttachedSpritePath, relic.BodyAttachedSpriteState);
            WarmupSprite(relic.HeadAttachedSpritePath, relic.HeadAttachedSpriteState);
            WarmupSprite(relic.MeleeArcSpritePath, relic.MeleeArcSpriteState);
        }

        private void DamagePlayer()
        {
            if (_invulnerabilityTicks > 0)
                return;

            SetPlayerHealth(PlayerHp - 1, MaxPlayerHp);

            if (TryRollHalfHeartRestore())
                SetPlayerHealth(PlayerHp + 1, MaxPlayerHp);

            _invulnerabilityTicks = InvulnerabilityTicks;
            _heartDamageFlash = HeartDamageFlashDuration;
            _playerDamageFlash = EntityDamageFlashDuration;

            if (PlayerHp <= 0)
            {
                _gameOver = true;
                PlaySfx(SfxPlayerDeath, -4f);
                return;
            }

            PlaySfx(SfxPlayerDamage, -8f);
        }

        private bool TryRollHalfHeartRestore()
        {
            var totalChance = _activeRelics.Sum(relic => relic.HalfHeartRestoreChanceOnDamage);
            if (totalChance <= 0f)
                return false;

            return _random.NextDouble() < totalChance;
        }

        private void SetPlayerHealth(int current, int max)
        {
            MaxPlayerHp = Math.Max(1, max);
            PlayerHp = Math.Clamp(current, 0, MaxPlayerHp);
        }

        private void TickDoorAnimations()
        {
            if (CurrentRoom.DoorTransitionTicks > 0)
                CurrentRoom.DoorTransitionTicks--;
        }

        private void HandleRoomState()
        {
            var room = CurrentRoom;
            if (room.Type != RoomType.Boss)
            {
                _floorExitSpawned = false;
                _floorExitPosition = null;
            }

            var hasEnemies = room.Enemies.Any(enemy => enemy.Hp > 0);
            SetDoorState(room, !hasEnemies);

            if (room.Cleared)
                return;

            if (hasEnemies)
                return;

            room.Cleared = true;

            if (!room.DoorTargetOpen)
                SetDoorState(room, true);

            if (room.Type == RoomType.Boss)
            {
                _floorExitSpawned = true;
                _floorExitPosition = GetRoomCenter();
            }
        }

        private void SetDoorState(RoomData room, bool open)
        {
            if (room.DoorTargetOpen == open && room.DoorTransitionTicks <= 0)
                return;

            room.DoorTargetOpen = open;
            var doorPrototype = GetDoorPrototype(room.Type);
            room.DoorTransitionTicks = Math.Max(1, (int) MathF.Ceiling(MathF.Max(0.01f, doorPrototype.TransitionDuration) / TickSeconds));
        }

        private void TryRoomTransition()
        {
            var room = CurrentRoom;
            if (room.Type != RoomType.Boss)
            {
                _floorExitSpawned = false;
                _floorExitPosition = null;
            }

            if (room.Type == RoomType.Boss && room.Cleared && _floorExitSpawned && _floorExitPosition is { } exitPosition)
            {
                if (Vector2.Distance(_playerPos, exitPosition) <= _playerProto.Radius + 0.45f)
                {
                    AdvanceFloor();
                    return;
                }
            }

            if (!room.Cleared)
                return;

            var threshold = _playerProto.Radius + DoorTransitionMargin;
            if (_playerPos.X < threshold && room.Neighbors.TryGetValue(new Vector2i(-1, 0), out var left))
            {
                EnterRoom(left, false);
                _playerPos = _playerPos with { X = GridWidth - 1.0f };
                return;
            }

            if (_playerPos.X > GridWidth - threshold && room.Neighbors.TryGetValue(new Vector2i(1, 0), out var right))
            {
                EnterRoom(right, false);
                _playerPos = _playerPos with { X = 1.0f };
                return;
            }

            if (_playerPos.Y < threshold && room.Neighbors.TryGetValue(new Vector2i(0, -1), out var up))
            {
                EnterRoom(up, false);
                _playerPos = _playerPos with { Y = GridHeight - 1.0f };
                return;
            }

            if (_playerPos.Y > GridHeight - threshold && room.Neighbors.TryGetValue(new Vector2i(0, 1), out var down))
            {
                EnterRoom(down, false);
                _playerPos = _playerPos with { Y = 1.0f };
            }
        }

        private void EnterRoom(int roomIndex, bool centerPlayer)
        {
            RoomIndex = roomIndex;
            _visitedRooms.Add(roomIndex);
            _playerProjectiles.Clear();
            _enemyProjectiles.Clear();
            _playerVelocity = Vector2.Zero;

            if (centerPlayer)
                _playerPos = new Vector2(GridWidth * 0.5f, GridHeight * 0.5f);

            var room = CurrentRoom;
            if (room.Type != RoomType.Boss)
            {
                _floorExitSpawned = false;
                _floorExitPosition = null;
            }

            var roomHasEnemies = room.Enemies.Any(enemy => enemy.Hp > 0);
            SetDoorState(room, !roomHasEnemies);

            if (room.Type != RoomType.Treasure)
            {
                _treasureBoxOpened = false;
                _treasureRelicPosition = null;
                _treasureRelicId = null;
                _treasureBoxPosition = null;
                _treasureOpeningAnimation = false;
                _treasureOpenAnimationTimer = 0f;
                _treasurePendingEnemySpawn = false;
                _treasurePendingRelicSpawn = false;
                _treasureRelicPickupGraceTimer = 0f;
                _treasureRelicAppearTimer = 0f;
                return;
            }

            _treasureBoxOpened = room.TreasureBoxOpened;
            _treasureBoxPosition = new Vector2(GridWidth * 0.5f, GridHeight * 0.5f);
            _treasureRelicId = room.TreasureRelicId;
            _treasureRelicPosition = room.HasTreasureRelic && !room.TreasureRelicTaken
                ? new Vector2(GridWidth * 0.5f, GridHeight * 0.5f)
                : null;
            _treasureOpeningAnimation = false;
            _treasureOpenAnimationTimer = 0f;
            _treasurePendingEnemySpawn = false;
            _treasurePendingRelicSpawn = false;
            _treasureRelicPickupGraceTimer = 0f;
            _treasureRelicAppearTimer = 0f;
        }

        private void GenerateMap()
        {
            var roomCount = GetRoomCountForFloor(_currentFloor);
            var positions = new List<Vector2i> { Vector2i.Zero };
            var indexByPos = new Dictionary<Vector2i, int> { [Vector2i.Zero] = 0 };

            for (var i = 1; i < roomCount; i++)
            {
                var created = false;
                for (var attempt = 0; attempt < 128; attempt++)
                {
                    var anchor = positions[_random.Next(positions.Count)];
                    var candidate = anchor + CardinalDirections()[_random.Next(4)];

                    if (indexByPos.ContainsKey(candidate))
                        continue;

                    positions.Add(candidate);
                    indexByPos[candidate] = i;
                    created = true;
                    break;
                }

                if (created)
                    continue;

                var fallback = positions[^1] + new Vector2i(i + 1, 0);
                positions.Add(fallback);
                indexByPos[fallback] = i;
            }

            var bossIndex = roomCount - 1;
            var treasureIndex = roomCount - 2;

            for (var i = 0; i < roomCount; i++)
            {
                var type = i switch
                {
                    0 => RoomType.Start,
                    _ when i == treasureIndex => RoomType.Treasure,
                    _ when i == bossIndex => RoomType.Boss,
                    _ => RoomType.Normal,
                };

                var room = new RoomData(type, positions[i], BuildTileMap(type));
                SpawnEnemies(room);
                room.DoorTargetOpen = room.Enemies.All(enemy => enemy.Hp <= 0);
                _rooms.Add(room);
            }

            foreach (var room in _rooms)
            {
                foreach (var direction in CardinalDirections())
                {
                    if (!indexByPos.TryGetValue(room.MapPosition + direction, out var neighborIndex))
                        continue;

                    room.Neighbors[direction] = neighborIndex;
                    AddDoorway(room, direction);
                }
            }

            EnsureBossRoomSingleConnection();

            foreach (var room in _rooms)
            {
                if (room.Type == RoomType.Start)
                {
                    room.Cleared = true;
                    room.DoorTargetOpen = true;
                }
            }
        }

        #endregion

        #region Map generation

        private TileType[,] BuildTileMap(RoomType roomType)
        {
            var tiles = new TileType[GridWidth, GridHeight];

            for (var y = 0; y < GridHeight; y++)
            {
                for (var x = 0; x < GridWidth; x++)
                {
                    tiles[x, y] = x == 0 || y == 0 || x == GridWidth - 1 || y == GridHeight - 1
                        ? TileType.Wall
                        : TileType.Floor;
                }
            }

            if (roomType is RoomType.Start or RoomType.Boss or RoomType.Treasure)
                return tiles;

            var obstacleCount = _random.Next(2, 5);
            for (var i = 0; i < obstacleCount; i++)
            {
                var x = _random.Next(2, GridWidth - 2);
                var y = _random.Next(2, GridHeight - 2);

                if (Math.Abs(x - GridWidth / 2) <= 1 && Math.Abs(y - GridHeight / 2) <= 1)
                    continue;

                tiles[x, y] = TileType.Obstacle;
            }

            return tiles;
        }

        private static void AddDoorway(RoomData room, Vector2i direction)
        {
            if (direction == new Vector2i(-1, 0))
            {
                room.Tiles[0, GridHeight / 2] = TileType.Door;
                return;
            }

            if (direction == new Vector2i(1, 0))
            {
                room.Tiles[GridWidth - 1, GridHeight / 2] = TileType.Door;
                return;
            }

            if (direction == new Vector2i(0, -1))
            {
                room.Tiles[GridWidth / 2, 0] = TileType.Door;
                return;
            }

            if (direction == new Vector2i(0, 1))
            {
                room.Tiles[GridWidth / 2, GridHeight - 1] = TileType.Door;
            }
        }

        private void EnsureBossRoomSingleConnection()
        {
            var bossIndex = _rooms.FindIndex(room => room.Type == RoomType.Boss);
            if (bossIndex < 0)
                return;

            var boss = _rooms[bossIndex];
            if (boss.Neighbors.Count <= 1)
                return;

            var keepDirection = boss.Neighbors.Keys.First();
            var removeDirections = boss.Neighbors.Keys.Where(direction => direction != keepDirection).ToArray();

            foreach (var direction in removeDirections)
            {
                var neighborIndex = boss.Neighbors[direction];
                boss.Neighbors.Remove(direction);
                boss.Tiles = SetDoorTile(boss.Tiles, direction, TileType.Wall);

                var neighbor = _rooms[neighborIndex];
                var reverse = new Vector2i(-direction.X, -direction.Y);
                neighbor.Neighbors.Remove(reverse);
                neighbor.Tiles = SetDoorTile(neighbor.Tiles, reverse, TileType.Wall);
            }
        }

        private static TileType[,] SetDoorTile(TileType[,] tiles, Vector2i direction, TileType tile)
        {
            if (direction == new Vector2i(-1, 0))
                tiles[0, GridHeight / 2] = tile;
            else if (direction == new Vector2i(1, 0))
                tiles[GridWidth - 1, GridHeight / 2] = tile;
            else if (direction == new Vector2i(0, -1))
                tiles[GridWidth / 2, 0] = tile;
            else if (direction == new Vector2i(0, 1))
                tiles[GridWidth / 2, GridHeight - 1] = tile;

            return tiles;
        }

        private EnemyData CreateEnemyData(DeepMaintenanceEntityPrototype prototype, Vector2 position, int spawnGraceTicks)
        {
            var enemy = new EnemyData(prototype, position, _random.Next(EnemyAggroDelayTicksMin, EnemyAggroDelayTicksMax + 1), spawnGraceTicks);
            if (_currentFloor <= 1)
                return enemy;

            var floorBonus = _currentFloor - 1;
            enemy.Hp += floorBonus;
            enemy.ShootCooldownTicks = Math.Max(4, enemy.ShootCooldownTicks - floorBonus / 2);
            return enemy;
        }

        private void SpawnEnemies(RoomData room)
        {
            room.Enemies.Clear();

            switch (room.Type)
            {
                case RoomType.Start:
                case RoomType.Treasure:
                    return;
                case RoomType.Boss:
                    room.Enemies.Add(CreateEnemyData(_bossProto, GetRoomCenter(), 0));
                    return;
            }

            var count = _random.Next(2 + _currentFloor, Math.Min(7, 5 + _currentFloor));
            for (var i = 0; i < count; i++)
            {
                var pos = new Vector2(_random.NextSingle() * (GridWidth - 4) + 2f, _random.NextSingle() * (GridHeight - 4) + 2f);
                var proto = _random.NextDouble() < 0.25 ? _shooterProto : _chaserProto;
                room.Enemies.Add(CreateEnemyData(proto, pos, 0));
            }
        }

        #endregion

        #region Rendering

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);

            if (_rooms.Count == 0 || RoomIndex < 0 || RoomIndex >= _rooms.Count)
                return;

            var tilePixel = MathF.Min(PixelSize.X / (float) GridWidth, PixelSize.Y / (float) GridHeight);
            var mapSize = new Vector2(tilePixel * GridWidth, tilePixel * GridHeight);
            var mapOffset = (PixelSize - mapSize) * 0.5f;

            for (var y = 0; y < GridHeight; y++)
            {
                for (var x = 0; x < GridWidth; x++)
                {
                    var tileProto = CurrentRoom.Tiles[x, y] switch
                    {
                        TileType.Floor => _floorProto,
                        TileType.Wall => _wallProto,
                        TileType.Obstacle => _obstacleProto,
                        _ => _floorProto,
                    };

                    var box = UIBox2.FromDimensions(
                        mapOffset + new Vector2(x * tilePixel, y * tilePixel),
                        new Vector2(tilePixel, tilePixel));

                    if (CurrentRoom.Tiles[x, y] is TileType.Wall or TileType.Obstacle)
                    {
                        DrawConnectedWallTile(handle, box, x, y, tileProto);
                    }
                    else if (GetSprite(tileProto.SpritePath, tileProto.SpriteState) is { } texture)
                    {
                        handle.DrawTextureRect(texture, box);
                    }
                    else
                    {
                        handle.DrawRect(box, Color.DimGray);
                    }

                    if (CurrentRoom.Tiles[x, y] == TileType.Door)
                    {
                        DrawDoor(handle, box, CurrentRoom.DoorTargetOpen, x, y);
                    }
                }
            }

            var tickAlpha = Math.Clamp(_accumulator / TickSeconds, 0f, 1f);

            DrawTreasureObjects(handle, tilePixel, mapOffset);

            foreach (var enemy in CurrentRoom.Enemies.Where(enemy => enemy.Hp > 0))
            {
                var drawPos = Vector2.Lerp(enemy.PreviousPosition, enemy.Position, tickAlpha);
                var color = enemy.DamageFlash > 0f ? Color.IndianRed : Color.White;
                DrawCharacter(handle, drawPos, enemy.Prototype, enemy.BodyFacing, enemy.ShootFacing, tilePixel, mapOffset, color);
            }

            DrawPlayer(handle, tilePixel * GetPlayerVisualScale(), mapOffset);

            foreach (var projectile in _playerProjectiles)
            {
                var drawPos = Vector2.Lerp(projectile.PreviousPosition, projectile.Position, tickAlpha);
                DrawProjectile(handle, drawPos, projectile, tilePixel, mapOffset);
            }

            foreach (var projectile in _enemyProjectiles)
            {
                var drawPos = Vector2.Lerp(projectile.PreviousPosition, projectile.Position, tickAlpha);
                DrawProjectile(handle, drawPos, projectile, tilePixel, mapOffset);
            }

            DrawFloorExit(handle, tilePixel, mapOffset);
            if (_debugHitboxes)
                DrawDebugHitboxes(handle, tilePixel, mapOffset, tickAlpha);
            DrawHealthHearts(handle);
            DrawBuffIcons(handle);
            DrawMinimap(handle);
        }

        private void DrawPlayer(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset)
        {
            var color = _playerDamageFlash > 0f ? Color.IndianRed : Color.White;
            DrawCharacter(handle, _playerPos, _playerProto, _playerBodyFacing, _playerShootFacing, tilePixel, mapOffset, color);

            foreach (var relic in _activeRelics)
            {
                if (!string.IsNullOrWhiteSpace(relic.BodyAttachedSpritePath) && !string.IsNullOrWhiteSpace(relic.BodyAttachedSpriteState))
                    DrawDirectionalEntityLayer(handle, _playerPos + relic.BodyAttachedOffset, relic.BodyAttachedSpritePath, relic.BodyAttachedSpriteState, _playerBodyFacing, tilePixel, relic.BodyAttachedSpriteScale, mapOffset, Color.White);
                else if (!string.IsNullOrWhiteSpace(relic.VisualEffectSpritePath) && !string.IsNullOrWhiteSpace(relic.VisualEffectSpriteState))
                    DrawDirectionalEntityLayer(handle, _playerPos, relic.VisualEffectSpritePath, relic.VisualEffectSpriteState, _playerBodyFacing, tilePixel, 1f, mapOffset, Color.White);
            }

            foreach (var relic in _activeRelics)
            {
                if (string.IsNullOrWhiteSpace(relic.HeadAttachedSpritePath) || string.IsNullOrWhiteSpace(relic.HeadAttachedSpriteState))
                    continue;

                DrawDirectionalEntityLayer(handle, _playerPos + relic.HeadAttachedOffset, relic.HeadAttachedSpritePath, relic.HeadAttachedSpriteState, _playerShootFacing, tilePixel, relic.HeadAttachedSpriteScale, mapOffset, Color.White);
            }

            DrawMeleeSwing(handle, tilePixel, mapOffset);
        }

        private void DrawMeleeSwing(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset)
        {
            if (_meleeSwingTimer <= 0f)
                return;

            var meleeRelic = _activeRelics.FirstOrDefault(relic => relic.MeleeOnShoot && !string.IsNullOrWhiteSpace(relic.MeleeArcSpritePath) && !string.IsNullOrWhiteSpace(relic.MeleeArcSpriteState));
            if (meleeRelic == null)
                return;

            var dir = FacingToUnitVector(_meleeSwingFacing);
            var pos = _playerPos + dir * 0.85f;
            var state = meleeRelic.MeleeArcSpriteState!;
            if (meleeRelic.MeleeArcAnimated)
            {
                var phase = 1f - (_meleeSwingTimer / MathF.Max(0.001f, MeleeSwingDuration));
                var frame = (int) (phase * MathF.Max(1f, meleeRelic.MeleeArcAnimationFps)) % 2;
                state = $"{state}{frame}";
            }

            DrawDirectionalEntityLayer(handle, pos, meleeRelic.MeleeArcSpritePath!, state, _meleeSwingFacing, tilePixel, 1f, mapOffset, Color.White);
        }

        private void DrawCharacter(DrawingHandleScreen handle, Vector2 pos, DeepMaintenanceEntityPrototype prototype, FacingDirection bodyFacing, FacingDirection shootFacing, float tilePixel, Vector2 mapOffset, Color color)
        {
            var bodyState = prototype.BodySpriteState ?? prototype.SpriteState;
            DrawDirectionalEntityLayer(handle, pos, prototype.SpritePath, bodyState, bodyFacing, tilePixel, prototype.SpriteScale, mapOffset, color);

            var headState = prototype.HeadSpriteState ?? prototype.ShootSpriteState;
            if (string.IsNullOrWhiteSpace(headState) || headState == bodyState)
                return;

            DrawDirectionalEntityLayer(handle, pos, prototype.SpritePath, headState, shootFacing, tilePixel, prototype.SpriteScale, mapOffset, color);
        }

        private void DrawDirectionalEntityLayer(DrawingHandleScreen handle, Vector2 pos, string spritePath, string spriteState, FacingDirection facing, float tilePixel, float spriteScale, Vector2 mapOffset, Color color)
        {
            var center = mapOffset + pos * tilePixel;
            var size = tilePixel * EntitySpriteTileSize * MathF.Max(0.05f, spriteScale);
            var box = UIBox2.FromDimensions(center - new Vector2(size * 0.5f, size * 0.5f), new Vector2(size, size));

            if (GetDirectionalSprite(spritePath, spriteState, facing) is not { } texture)
            {
                handle.DrawRect(box, color);
                return;
            }

            handle.DrawTextureRect(texture, box, color);
        }

        private void DrawEntity(DrawingHandleScreen handle, Vector2 pos, string spritePath, string spriteState, float tilePixel, Vector2 mapOffset)
        {
            var center = mapOffset + pos * tilePixel;
            var size = tilePixel * EntitySpriteTileSize;
            var box = UIBox2.FromDimensions(center - new Vector2(size * 0.5f, size * 0.5f), new Vector2(size, size));

            if (GetSprite(spritePath, spriteState) is { } fallbackTexture)
            {
                handle.DrawTextureRect(fallbackTexture, box);
                return;
            }

            handle.DrawRect(box, Color.White);
        }

        private void DrawConnectedWallTile(DrawingHandleScreen handle, UIBox2 box, int tileX, int tileY, DeepMaintenanceTilePrototype tilePrototype)
        {
            if (GetSprite(tilePrototype.SpritePath, "full") is { } fullTexture)
                handle.DrawTextureRect(fullTexture, box);

            var (cornerNe, cornerNw, cornerSw, cornerSe) = GetWallCornerFill(tileX, tileY, CurrentRoom);
            DrawWallCornerLayer(handle, box, tilePrototype.SpritePath, cornerSe, FacingDirection.Down);
            DrawWallCornerLayer(handle, box, tilePrototype.SpritePath, cornerNe, FacingDirection.Right);
            DrawWallCornerLayer(handle, box, tilePrototype.SpritePath, cornerNw, FacingDirection.Up);
            DrawWallCornerLayer(handle, box, tilePrototype.SpritePath, cornerSw, FacingDirection.Left);
        }

        private void DrawWallCornerLayer(DrawingHandleScreen handle, UIBox2 box, string spritePath, byte cornerFill, FacingDirection facing)
        {
            var state = $"solid{cornerFill}";
            if (GetDirectionalSprite(spritePath, state, facing) is not { } texture)
                return;

            handle.DrawTextureRect(texture, box);
        }

        private static (byte ne, byte nw, byte sw, byte se) GetWallCornerFill(int tileX, int tileY, RoomData room)
        {
            var n = IsConnectedWallTile(room, tileX, tileY - 1);
            var ne = IsConnectedWallTile(room, tileX + 1, tileY - 1);
            var e = IsConnectedWallTile(room, tileX + 1, tileY);
            var se = IsConnectedWallTile(room, tileX + 1, tileY + 1);
            var s = IsConnectedWallTile(room, tileX, tileY + 1);
            var sw = IsConnectedWallTile(room, tileX - 1, tileY + 1);
            var w = IsConnectedWallTile(room, tileX - 1, tileY);
            var nw = IsConnectedWallTile(room, tileX - 1, tileY - 1);

            byte cornerNe = 0;
            byte cornerSe = 0;
            byte cornerSw = 0;
            byte cornerNw = 0;

            if (n)
            {
                cornerNe |= 1;
                cornerNw |= 4;
            }

            if (ne)
                cornerNe |= 2;

            if (e)
            {
                cornerNe |= 4;
                cornerSe |= 1;
            }

            if (se)
                cornerSe |= 2;

            if (s)
            {
                cornerSe |= 4;
                cornerSw |= 1;
            }

            if (sw)
                cornerSw |= 2;

            if (w)
            {
                cornerSw |= 4;
                cornerNw |= 1;
            }

            if (nw)
                cornerNw |= 2;

            return (cornerNe, cornerNw, cornerSw, cornerSe);
        }

        private static bool IsConnectedWallTile(RoomData room, int tileX, int tileY)
        {
            if (tileX < 0 || tileY < 0 || tileX >= GridWidth || tileY >= GridHeight)
                return true;

            return room.Tiles[tileX, tileY] is TileType.Wall or TileType.Obstacle;
        }

        private void DrawDoor(DrawingHandleScreen handle, UIBox2 box, bool opened, int tileX, int tileY)
        {
            var doorType = GetDoorRoomType(tileX, tileY);
            var doorPrototype = GetDoorPrototype(doorType);
            var direction = FacingFromDoorTile(tileX, tileY);

            var state = GetDoorAnimationState(doorPrototype, opened);
            if (GetDirectionalSprite(doorPrototype.SpritePath, state, direction) is { } texture)
            {
                handle.DrawTextureRect(texture, box);
                return;
            }

            handle.DrawRect(box, opened ? Color.DarkSlateGray : Color.DarkRed);
        }

        private string GetDoorAnimationState(DeepMaintenanceDoorPrototype doorPrototype, bool opened)
        {
            if (CurrentRoom.DoorTransitionTicks <= 0)
                return opened ? doorPrototype.OpenState : doorPrototype.ClosedState;

            if (CurrentRoom.DoorTargetOpen)
                return doorPrototype.OpeningState ?? doorPrototype.OpenState;

            return doorPrototype.ClosingState ?? doorPrototype.ClosedState;
        }

        private RoomType GetDoorRoomType(int tileX, int tileY)
        {
            if (!TryGetDoorDirection(tileX, tileY, out var direction) || !CurrentRoom.Neighbors.TryGetValue(direction, out var neighborIndex))
                return CurrentRoom.Type;

            var currentPriority = GetDoorPriority(CurrentRoom.Type);
            var neighborType = _rooms[neighborIndex].Type;
            var neighborPriority = GetDoorPriority(neighborType);

            return neighborPriority > currentPriority ? neighborType : CurrentRoom.Type;
        }

        private static int GetDoorPriority(RoomType roomType)
        {
            return roomType switch
            {
                RoomType.Boss => 3,
                RoomType.Treasure => 2,
                _ => 1,
            };
        }

        private static bool TryGetDoorDirection(int tileX, int tileY, out Vector2i direction)
        {
            switch (tileX)
            {
                case 0 when tileY == GridHeight / 2:
                    direction = new Vector2i(-1, 0);
                    return true;
                case GridWidth - 1 when tileY == GridHeight / 2:
                    direction = new Vector2i(1, 0);
                    return true;
                case GridWidth / 2 when tileY == 0:
                    direction = new Vector2i(0, -1);
                    return true;
                case GridWidth / 2 when tileY == GridHeight - 1:
                    direction = new Vector2i(0, 1);
                    return true;
                default:
                    direction = default;
                    return false;
            }
        }

        private DeepMaintenanceDoorPrototype GetDoorPrototype(RoomType roomType)
        {
            if (_doorPrototypes.TryGetValue(roomType, out var doorPrototype))
                return doorPrototype;

            return _doorPrototypes[RoomType.Normal];
        }

        private void DrawProjectile(DrawingHandleScreen handle, Vector2 pos, ProjectileData projectile, float tilePixel, Vector2 mapOffset)
        {
            var visualDrop = GetProjectileVisualDrop(projectile);
            var center = mapOffset + (pos + visualDrop) * tilePixel;
            var size = tilePixel * projectile.Radius * 3f * MathF.Max(0.05f, projectile.SpriteScale);
            var box = UIBox2.FromDimensions(center - new Vector2(size * 0.5f, size * 0.5f), new Vector2(size, size));
            var facing = FacingFromVector(projectile.Direction == Vector2.Zero ? projectile.Velocity : projectile.Direction, FacingDirection.Down);

            var texture = GetDirectionalSprite(projectile.SpritePath, projectile.SpriteState, facing) ?? GetSprite(projectile.SpritePath, projectile.SpriteState);
            if (texture == null)
                return;

            handle.DrawTextureRect(texture, box, projectile.Tint);
        }

        private static Vector2 GetProjectileVisualDrop(ProjectileData projectile)
        {
            var initialLifetime = MathF.Max(0.001f, projectile.InitialLifetime);
            var lifeProgress = Math.Clamp(1f - projectile.Lifetime / initialLifetime, 0f, 1f);
            var start = Math.Clamp(projectile.Prototype.FinalDropStart, 0f, 1f);
            if (lifeProgress <= start)
                return Vector2.Zero;

            var t = (lifeProgress - start) / MathF.Max(0.001f, 1f - start);
            var amount = t * t * MathF.Max(0f, projectile.Prototype.FinalDropDistance);
            if (projectile.Direction == Vector2.Zero)
                return new Vector2(0f, amount);

            var normal = new Vector2(-projectile.Direction.Y, projectile.Direction.X);
            return normal * amount;
        }

        private void DrawFloorExit(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset)
        {
            if (!_floorExitSpawned || _floorExitPosition is not { } exitPos)
                return;

            var center = mapOffset + exitPos * tilePixel;
            var size = tilePixel * 0.8f;
            var box = UIBox2.FromDimensions(center - new Vector2(size * 0.5f, size * 0.5f), new Vector2(size, size));
            handle.DrawRect(box, new Color(110, 70, 30));
            var thickness = MathF.Max(1f, tilePixel * 0.06f);
            handle.DrawRect(UIBox2.FromDimensions(new Vector2(box.Left, box.Top), new Vector2(box.Width, thickness)), Color.Gold);
            handle.DrawRect(UIBox2.FromDimensions(new Vector2(box.Left, box.Bottom - thickness), new Vector2(box.Width, thickness)), Color.Gold);
            handle.DrawRect(UIBox2.FromDimensions(new Vector2(box.Left, box.Top), new Vector2(thickness, box.Height)), Color.Gold);
            handle.DrawRect(UIBox2.FromDimensions(new Vector2(box.Right - thickness, box.Top), new Vector2(thickness, box.Height)), Color.Gold);
        }

        private void DrawTreasureObjects(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset)
        {
            if (_treasureBoxPosition is { } boxPos)
            {
                var state = _treasureBoxOpened ? _treasurePrototype.OpenCrateSpriteState : _treasurePrototype.ClosedCrateSpriteState;
                DrawEntity(handle, boxPos, _treasurePrototype.ClosedCrateSpritePath, state, tilePixel, mapOffset);
            }

            if (_treasureRelicPosition is not { } relicPos || string.IsNullOrWhiteSpace(_treasureRelicId))
                return;

            if (_prototype.TryIndex<DeepMaintenanceRelicPrototype>(_treasureRelicId, out var relic))
            {
                var appearProgress = _treasurePrototype.RelicAppearDuration <= 0f
                    ? 1f
                    : 1f - Math.Clamp(_treasureRelicAppearTimer / _treasurePrototype.RelicAppearDuration, 0f, 1f);
                var rise = (1f - appearProgress) * MathF.Max(0f, _treasurePrototype.RelicAppearRise);
                DrawDirectionalEntityLayer(handle, relicPos + new Vector2(0f, -rise), relic.HudIconSpritePath ?? string.Empty, relic.HudIconSpriteState ?? string.Empty, FacingDirection.Down, tilePixel, 1f, mapOffset, Color.White);
            }
        }

        private void DrawBuffIcons(DrawingHandleScreen handle)
        {
            if (_activeRelics.Count == 0)
                return;

            const float iconSize = 24f;
            const float gap = 4f;
            for (var i = 0; i < _activeRelics.Count; i++)
            {
                var relic = _activeRelics[i];
                if (GetSprite(relic.HudIconSpritePath, relic.HudIconSpriteState) is not { } texture)
                    continue;

                var x = 6f + i * (iconSize + gap);
                var box = UIBox2.FromDimensions(new Vector2(x, 38f), new Vector2(iconSize, iconSize));
                handle.DrawTextureRect(texture, box);
            }
        }

        private void DrawHealthHearts(DrawingHandleScreen handle)
        {
            var size = Math.Clamp(PixelSize.X / 18f, 14f, 24f);
            const float gap = 3f;
            const float rowGap = 4f;
            var heartsPerRow = Math.Max(1, (int) ((Math.Max(48f, PixelSize.X - 12f) + gap) / (size + gap)));

            var maxHearts = (int) MathF.Ceiling(MaxPlayerHp / 2f);
            var damagePulse = _heartDamageFlash > 0f;
            var pulseScale = damagePulse ? 1f + 0.22f * (_heartDamageFlash / HeartDamageFlashDuration) : 1f;

            for (var i = 0; i < maxHearts; i++)
            {
                var hpOnHeart = PlayerHp - i * 2;
                var state = hpOnHeart switch
                {
                    >= 2 => HeartFullState,
                    1 => HeartHalfState,
                    _ => HeartEmptyState,
                };
                var column = i % heartsPerRow;
                var row = i / heartsPerRow;
                var basePos = new Vector2(6 + column * (size + gap), HeartRowsStartY + row * (size + rowGap));
                var drawSize = new Vector2(size * pulseScale, size * pulseScale);
                var drawPos = basePos - (drawSize - new Vector2(size, size)) * 0.5f;
                var box = UIBox2.FromDimensions(drawPos, drawSize);

                if (GetSprite(HeartSpritePath, state) is not { } texture)
                    continue;

                if (damagePulse && hpOnHeart > 0)
                    handle.DrawTextureRect(texture, box, Color.IndianRed);
                else
                    handle.DrawTextureRect(texture, box);
            }
        }

        private void DrawDebugHitboxes(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset, float tickAlpha)
        {
            DrawDebugCircle(handle, _playerPos, _playerProto.Radius, Color.LimeGreen, tilePixel, mapOffset);

            foreach (var enemy in CurrentRoom.Enemies.Where(enemy => enemy.Hp > 0))
            {
                var drawPos = Vector2.Lerp(enemy.PreviousPosition, enemy.Position, tickAlpha);
                DrawDebugCircle(handle, drawPos, enemy.Prototype.Radius, Color.Red, tilePixel, mapOffset);
            }

            foreach (var projectile in _playerProjectiles)
            {
                var drawPos = Vector2.Lerp(projectile.PreviousPosition, projectile.Position, tickAlpha);
                DrawDebugCircle(handle, drawPos, projectile.Radius, Color.Yellow, tilePixel, mapOffset);
            }

            foreach (var projectile in _enemyProjectiles)
            {
                var drawPos = Vector2.Lerp(projectile.PreviousPosition, projectile.Position, tickAlpha);
                DrawDebugCircle(handle, drawPos, projectile.Radius, Color.Yellow, tilePixel, mapOffset);
            }

            if (_treasureBoxPosition is { } chestPos)
                DrawDebugAabb(handle, chestPos, TreasureObjectRadius, Color.CornflowerBlue, tilePixel, mapOffset);

            if (_treasureRelicPosition is { } relicPos)
                DrawDebugAabb(handle, relicPos, TreasureObjectRadius, Color.CornflowerBlue, tilePixel, mapOffset);
        }

        private static void DrawDebugCircle(DrawingHandleScreen handle, Vector2 centerTile, float radiusTile, Color color, float tilePixel, Vector2 mapOffset)
        {
            var center = mapOffset + centerTile * tilePixel;
            var radius = radiusTile * tilePixel;
            const int segments = 18;
            var prev = center + new Vector2(radius, 0f);
            for (var i = 1; i <= segments; i++)
            {
                var angle = i / (float) segments * MathF.PI * 2f;
                var next = center + new Vector2(MathF.Cos(angle), MathF.Sin(angle)) * radius;
                handle.DrawLine(prev, next, color);
                prev = next;
            }
        }

        private static void DrawDebugAabb(DrawingHandleScreen handle, Vector2 centerTile, float halfExtentTile, Color color, float tilePixel, Vector2 mapOffset)
        {
            var center = mapOffset + centerTile * tilePixel;
            var half = new Vector2(halfExtentTile * tilePixel, halfExtentTile * tilePixel);
            var box = UIBox2.FromDimensions(center - half, half * 2f);
            handle.DrawRect(box, color.WithAlpha(0.15f));
            handle.DrawLine(new Vector2(box.Left, box.Top), new Vector2(box.Right, box.Top), color);
            handle.DrawLine(new Vector2(box.Right, box.Top), new Vector2(box.Right, box.Bottom), color);
            handle.DrawLine(new Vector2(box.Right, box.Bottom), new Vector2(box.Left, box.Bottom), color);
            handle.DrawLine(new Vector2(box.Left, box.Bottom), new Vector2(box.Left, box.Top), color);
        }

        private void DrawMinimap(DrawingHandleScreen handle)
        {
            const float roomSize = 10f;
            const float spacing = 6f;
            var current = CurrentRoom.MapPosition;
            var origin = new Vector2(PixelSize.X - 96f, 8f);

            for (var i = 0; i < _rooms.Count; i++)
            {
                var room = _rooms[i];
                if (room.IsSecret && !_visitedRooms.Contains(i))
                    continue;

                var rel = room.MapPosition - current;
                var start = origin + new Vector2(rel.X * (roomSize + spacing), rel.Y * (roomSize + spacing)) + new Vector2(roomSize * 0.5f, roomSize * 0.5f);

                foreach (var (direction, neighborIndex) in room.Neighbors)
                {
                    if (neighborIndex <= i)
                        continue;

                    var neighbor = _rooms[neighborIndex];
                    if (neighbor.IsSecret && !_visitedRooms.Contains(neighborIndex))
                        continue;

                    var end = start + new Vector2(direction.X * (roomSize + spacing), direction.Y * (roomSize + spacing));
                    handle.DrawLine(start, end, Color.DarkSlateGray);
                }
            }

            for (var i = 0; i < _rooms.Count; i++)
            {
                var room = _rooms[i];

                if (room.IsSecret && !_visitedRooms.Contains(i))
                    continue;

                var rel = room.MapPosition - current;
                var center = origin + new Vector2(rel.X * (roomSize + spacing), rel.Y * (roomSize + spacing));
                var box = UIBox2.FromDimensions(center, new Vector2(roomSize, roomSize));

                var discovered = _visitedRooms.Contains(i);
                var color = room.Type switch
                {
                    RoomType.Boss => discovered ? Color.Red : new Color(110, 35, 35),
                    RoomType.Treasure => discovered ? Color.Gold : new Color(110, 95, 32),
                    _ => discovered ? Color.LightGray : new Color(55, 55, 55),
                };

                if (i == RoomIndex)
                    color = Color.White;

                handle.DrawRect(box, color);
            }
        }

        #endregion

        #region Helpers

        private void LoadPrototypes()
        {
            _playerProto = _prototype.Index<DeepMaintenanceEntityPrototype>(EntityPlayerPrototypeId);
            _chaserProto = _prototype.Index<DeepMaintenanceEntityPrototype>(EntityChaserPrototypeId);
            _shooterProto = _prototype.Index<DeepMaintenanceEntityPrototype>(EntityShooterPrototypeId);
            _bossProto = _prototype.Index<DeepMaintenanceEntityPrototype>(EntityBossPrototypeId);

            _floorProto = _prototype.Index<DeepMaintenanceTilePrototype>(TileFloorPrototypeId);
            _wallProto = _prototype.Index<DeepMaintenanceTilePrototype>(TileWallPrototypeId);
            _obstacleProto = _prototype.Index<DeepMaintenanceTilePrototype>(TileObstaclePrototypeId);
            _hasTreasurePrototype = _prototype.TryIndex<DeepMaintenanceTreasurePrototype>(TreasurePrototypeId, out var treasurePrototype);
            if (_hasTreasurePrototype)
                _treasurePrototype = treasurePrototype!;

            _doorPrototypes[RoomType.Normal] = _prototype.Index<DeepMaintenanceDoorPrototype>("DeepMaintenanceDoorNormal");
            _doorPrototypes[RoomType.Treasure] = _prototype.Index<DeepMaintenanceDoorPrototype>("DeepMaintenanceDoorTreasure");
            _doorPrototypes[RoomType.Boss] = _prototype.Index<DeepMaintenanceDoorPrototype>("DeepMaintenanceDoorBoss");
            _doorPrototypes[RoomType.Start] = _doorPrototypes[RoomType.Normal];

            WarmupSprite(_floorProto.SpritePath, _floorProto.SpriteState);
            WarmupSprite(_wallProto.SpritePath, _wallProto.SpriteState);
            WarmupSprite(_wallProto.SpritePath, "full");
            WarmupSprite(_wallProto.SpritePath, "solid0");
            WarmupSprite(_wallProto.SpritePath, "solid1");
            WarmupSprite(_wallProto.SpritePath, "solid2");
            WarmupSprite(_wallProto.SpritePath, "solid3");
            WarmupSprite(_wallProto.SpritePath, "solid4");
            WarmupSprite(_wallProto.SpritePath, "solid5");
            WarmupSprite(_wallProto.SpritePath, "solid6");
            WarmupSprite(_wallProto.SpritePath, "solid7");
            WarmupSprite(_obstacleProto.SpritePath, _obstacleProto.SpriteState);

            WarmupSprite(_playerProto.SpritePath, _playerProto.SpriteState);
            WarmupSprite(_playerProto.SpritePath, _playerProto.BodySpriteState);
            WarmupSprite(_playerProto.SpritePath, _playerProto.HeadSpriteState);
            WarmupSprite(_playerProto.SpritePath, _playerProto.ShootSpriteState);
            WarmupSprite(_chaserProto.SpritePath, _chaserProto.SpriteState);
            WarmupSprite(_shooterProto.SpritePath, _shooterProto.SpriteState);
            WarmupSprite(_bossProto.SpritePath, _bossProto.SpriteState);

            WarmupSprite(HeartSpritePath, HeartFullState);
            WarmupSprite(HeartSpritePath, HeartHalfState);
            WarmupSprite(HeartSpritePath, HeartEmptyState);

            foreach (var door in _doorPrototypes.Values.Distinct())
            {
                WarmupSprite(door.SpritePath, door.ClosedState);
                WarmupSprite(door.SpritePath, door.OpenState);
                WarmupSprite(door.SpritePath, door.OpeningState);
                WarmupSprite(door.SpritePath, door.ClosingState);
            }

            if (_hasTreasurePrototype)
            {
                WarmupSprite(_treasurePrototype.ClosedCrateSpritePath, _treasurePrototype.ClosedCrateSpriteState);
                WarmupSprite(_treasurePrototype.ClosedCrateSpritePath, _treasurePrototype.OpenCrateSpriteState);
            }

            foreach (var relic in _prototype.EnumeratePrototypes<DeepMaintenanceRelicPrototype>())
            {
                WarmupSprite(relic.HudIconSpritePath, relic.HudIconSpriteState);
                WarmupSprite(relic.VisualEffectSpritePath, relic.VisualEffectSpriteState);
                WarmupSprite(relic.BodyAttachedSpritePath, relic.BodyAttachedSpriteState);
                WarmupSprite(relic.HeadAttachedSpritePath, relic.HeadAttachedSpriteState);
                WarmupSprite(relic.MeleeArcSpritePath, relic.MeleeArcSpriteState);
            }

            ApplySpriteBasedPhysicsScales();
        }

        private void WarmupSprite(string? spritePath, string? spriteState)
        {
            if (string.IsNullOrWhiteSpace(spritePath) || string.IsNullOrWhiteSpace(spriteState))
                return;

            var key = (spritePath, spriteState);
            if (_spriteCache.ContainsKey(key))
                return;

            var specifier = new SpriteSpecifier.Rsi(new ResPath(spritePath), spriteState);
            _spriteCache[key] = _sprite.Frame0(specifier);
        }

        private void ApplySpriteBasedPhysicsScales()
        {
            _playerProto.Radius = GetSpriteDrivenRadius(_playerProto.Radius, _playerProto.SpritePath, _playerProto.BodySpriteState ?? _playerProto.SpriteState, _playerProto.SpriteScale, 0.78f);
            _chaserProto.Radius = GetSpriteDrivenRadius(_chaserProto.Radius, _chaserProto.SpritePath, _chaserProto.BodySpriteState ?? _chaserProto.SpriteState, _chaserProto.SpriteScale, 0.78f);
            _shooterProto.Radius = GetSpriteDrivenRadius(_shooterProto.Radius, _shooterProto.SpritePath, _shooterProto.BodySpriteState ?? _shooterProto.SpriteState, _shooterProto.SpriteScale, 0.8f);
            _bossProto.Radius = GetSpriteDrivenRadius(_bossProto.Radius, _bossProto.SpritePath, _bossProto.BodySpriteState ?? _bossProto.SpriteState, _bossProto.SpriteScale, 0.8f);

            ApplySpriteBasedProjectileRadius(_playerProto.ProjectilePrototype);
            ApplySpriteBasedProjectileRadius(_chaserProto.ProjectilePrototype);
            ApplySpriteBasedProjectileRadius(_shooterProto.ProjectilePrototype);
            ApplySpriteBasedProjectileRadius(_bossProto.ProjectilePrototype);
        }

        private void ApplySpriteBasedProjectileRadius(string projectilePrototypeId)
        {
            var projectilePrototype = _prototype.Index<DeepMaintenanceProjectilePrototype>(projectilePrototypeId);
            projectilePrototype.Radius = GetSpriteDrivenRadius(projectilePrototype.Radius, projectilePrototype.SpritePath, projectilePrototype.SpriteState, projectilePrototype.SpriteScale, 0.7f);
        }

        private float GetSpriteDrivenRadius(float configuredRadius, string? spritePath, string? spriteState, float spriteScale, float spriteWeight)
        {
            if (GetSprite(spritePath, spriteState) is not { } texture)
                return configuredRadius;

            var dominantPixels = MathF.Max(texture.Width, texture.Height);
            var spriteRadius = dominantPixels / DefaultSpritePixelsPerTile * 0.5f * MathF.Max(0.05f, spriteScale);
            var blended = configuredRadius * (1f - spriteWeight) + spriteRadius * spriteWeight;
            return Math.Clamp(blended, 0.04f, 0.95f);
        }

        private Texture? GetSprite(string? spritePath, string? spriteState)
        {
            if (string.IsNullOrWhiteSpace(spritePath) || string.IsNullOrWhiteSpace(spriteState))
                return null;

            var key = (spritePath, spriteState);
            if (_spriteCache.TryGetValue(key, out var texture))
                return texture;

            WarmupSprite(spritePath, spriteState);
            return _spriteCache.GetValueOrDefault(key);
        }

        private void TryShoot(Vector2 direction)
        {
            if (_paused || _gameOver || _victory || _playerShootCooldown > 0f)
                return;

            var normalized = NormalizeSafe(direction);
            if (normalized == Vector2.Zero)
                return;

            _playerShootFacing = FacingFromVector(normalized, _playerShootFacing);
            _playerShootFacingResetTimer = FacingResetDelaySeconds;

            if (_activeRelics.Any(relic => relic.MeleeOnShoot))
            {
                PerformMeleeStrike(normalized);
            }
            else
            {
                FirePlayerProjectile(normalized);
            }

            _playerShootCooldown = GetShootCooldown(_playerProto) * GetShootCooldownMultiplier();
            PlaySfx(SfxPlayerShoot, -6f);
        }

        private void FirePlayerProjectile(Vector2 direction)
        {
            var projectilePrototype = _prototype.Index<DeepMaintenanceProjectilePrototype>(_playerProto.ProjectilePrototype);
            var speed = projectilePrototype.Speed * GetProjectileSpeedMultiplier();
            var damage = projectilePrototype.Damage + GetDamageFlatBonus();
            var tintPalette = GetProjectileTintPalette();

            if (_activeRelics.Any(relic => relic.TripleShotAlternating))
            {
                foreach (var spreadDegrees in new[] { -12f, 0f, 12f })
                {
                    var shotDirection = NormalizeSafe(Rotate(direction, MathF.PI * spreadDegrees / 180f));
                    SpawnProjectile(_playerProjectiles, _playerPos, shotDirection * speed, projectilePrototype, 1f, damage, PickTintFromPalette(tintPalette));
                }

                return;
            }

            SpawnProjectile(_playerProjectiles, _playerPos, direction * speed, projectilePrototype, 1f, damage, PickTintFromPalette(tintPalette));
        }

        private void PerformMeleeStrike(Vector2 direction)
        {
            var meleeRange = 0f;
            var meleeDamage = 0;
            foreach (var relic in _activeRelics.Where(relic => relic.MeleeOnShoot))
            {
                meleeRange = MathF.Max(meleeRange, relic.MeleeRange);
                meleeDamage = Math.Max(meleeDamage, relic.MeleeDamage);
                _meleeSwingFacing = FacingFromVector(direction, _meleeSwingFacing);
                _meleeSwingTimer = MeleeSwingDuration;
            }

            if (meleeRange <= 0f || meleeDamage <= 0)
                return;

            var strikeCenter = _playerPos + direction * meleeRange;
            foreach (var enemy in CurrentRoom.Enemies)
            {
                if (enemy.Hp <= 0)
                    continue;

                if (Vector2.Distance(enemy.Position, strikeCenter) > enemy.Prototype.Radius + 0.65f)
                    continue;

                enemy.Hp -= meleeDamage;
                enemy.DamageFlash = EntityDamageFlashDuration;
                if (enemy.Hp <= 0)
                    PlaySfx(SfxEnemyDeath, -7f);
            }
        }

        private static void SpawnProjectile(List<ProjectileData> container, Vector2 position, Vector2 velocity, DeepMaintenanceProjectilePrototype projectilePrototype, float radiusScale, float damage, Color tint)
        {
            container.Add(new ProjectileData(
                position,
                position,
                velocity,
                projectilePrototype.Radius * radiusScale,
                damage,
                projectilePrototype.Lifetime,
                projectilePrototype.SpritePath,
                projectilePrototype.SpriteState,
                projectilePrototype.SpriteScale,
                projectilePrototype,
                tint));
        }

        private float GetProjectileSpeedMultiplier()
        {
            var multiplier = 1f;
            foreach (var relic in _activeRelics)
            {
                multiplier *= relic.ProjectileSpeedMultiplier;
            }

            return multiplier;
        }

        private float GetDamageFlatBonus()
        {
            return _activeRelics.Sum(relic => relic.DamageFlatBonus);
        }

        private float GetShootCooldownMultiplier()
        {
            var multiplier = 1f;
            foreach (var relic in _activeRelics)
            {
                multiplier *= relic.ShootCooldownMultiplier;
            }

            return MathF.Max(0.1f, multiplier);
        }

        private List<Color> GetProjectileTintPalette()
        {
            var colors = new List<Color>();
            foreach (var relic in _activeRelics)
            {
                if (relic.ProjectileTintPaletteHex == null)
                    continue;

                foreach (var hex in relic.ProjectileTintPaletteHex)
                {
                    if (string.IsNullOrWhiteSpace(hex))
                        continue;

                    try
                    {
                        colors.Add(Color.FromHex(hex));
                    }
                    catch
                    {
                        // Ignore invalid color values from content data.
                    }
                }
            }

            return colors;
        }

        private Color PickTintFromPalette(List<Color> colors)
        {
            if (colors.Count == 0)
                return Color.White;

            return colors[_random.Next(colors.Count)];
        }

        private static float GetPlayerVisualScale()
        {
            return 1f;
        }

        private float GetShootCooldown(DeepMaintenanceEntityPrototype prototype)
        {
            if (prototype.ShootCooldownSeconds is { } cooldownSeconds)
                return cooldownSeconds;

            return MathF.Max(0.05f, prototype.ShootCooldownTicks * TickSeconds);
        }

        private Vector2 GetRoomCenter()
        {
            return new Vector2(GridWidth * 0.5f, GridHeight * 0.5f);
        }

        private int GetRoomCountForFloor(int floor)
        {
            var min = Math.Min(FloorMaxRooms, FirstFloorMinRooms + Math.Max(0, floor - 1));
            var max = Math.Min(FloorMaxRooms, 15 + Math.Max(0, floor - 1));
            if (max < min)
                max = min;

            return _random.Next(min, max + 1);
        }

        private void AdvanceFloor()
        {
            if (_currentFloor >= TotalFloors)
            {
                _victory = true;
                return;
            }

            _currentFloor++;
            _rooms.Clear();
            _visitedRooms.Clear();
            _playerProjectiles.Clear();
            _enemyProjectiles.Clear();
            RoomIndex = 0;
            GenerateMap();
            EnterRoom(0, true);
            StateChanged?.Invoke();
        }

        private FacingDirection FacingFromDoorTile(int tileX, int tileY)
        {
            if (tileX == 0)
                return FacingDirection.Left;
            if (tileX == GridWidth - 1)
                return FacingDirection.Right;
            if (tileY == 0)
                return FacingDirection.Up;

            return FacingDirection.Down;
        }

        private void PlaySfx(SoundSpecifier sound, float volume)
        {
            _audio.PlayGlobal(sound, Filter.Local(), false, AudioParams.Default.WithVolume(volume));
        }

        private static FacingDirection FacingFromVector(Vector2 direction, FacingDirection fallback)
        {
            if (direction == Vector2.Zero)
                return fallback;

            if (MathF.Abs(direction.X) > MathF.Abs(direction.Y))
                return direction.X > 0f ? FacingDirection.Right : FacingDirection.Left;

            return direction.Y > 0f ? FacingDirection.Down : FacingDirection.Up;
        }

        private static Vector2 FacingToUnitVector(FacingDirection facing)
        {
            return facing switch
            {
                FacingDirection.Up => new Vector2(0f, -1f),
                FacingDirection.Left => new Vector2(-1f, 0f),
                FacingDirection.Right => new Vector2(1f, 0f),
                _ => new Vector2(0f, 1f),
            };
        }

        private Texture? GetDirectionalSprite(string? spritePath, string? spriteState, FacingDirection facing)
        {
            if (string.IsNullOrWhiteSpace(spritePath) || string.IsNullOrWhiteSpace(spriteState))
                return null;

            var direction = FacingToRsiDirection(facing);
            var key = (spritePath, spriteState, direction);
            if (_directionalSpriteCache.TryGetValue(key, out var texture))
                return texture;

            if (!_resourceCache.TryGetResource<RSIResource>(new ResPath(spritePath), out var resource) ||
                !resource.RSI.TryGetState(new RSI.StateId(spriteState), out var state))
                return GetSprite(spritePath, spriteState);

            if (!TryGetDirectionalFrame(state, direction, out texture) &&
                !TryGetDirectionalFrame(state, RsiDirection.South, out texture) &&
                !TryGetDirectionalFrame(state, RsiDirection.North, out texture) &&
                !TryGetDirectionalFrame(state, RsiDirection.East, out texture) &&
                !TryGetDirectionalFrame(state, RsiDirection.West, out texture))
                return GetSprite(spritePath, spriteState);

            _directionalSpriteCache[key] = texture;
            return texture;

        }

        private static bool TryGetDirectionalFrame(RSI.State state, RsiDirection direction, out Texture texture)
        {
            try
            {
                var frames = state.GetFrames(direction);
                if (frames.Length > 0)
                {
                    texture = frames[0];
                    return true;
                }
            }
            catch (IndexOutOfRangeException)
            {
                // Some states provide fewer directional sets than expected.
            }

            texture = default!;
            return false;
        }

        private static RsiDirection FacingToRsiDirection(FacingDirection facing)
        {
            return facing switch
            {
                FacingDirection.Up => RsiDirection.North,
                FacingDirection.Left => RsiDirection.West,
                FacingDirection.Right => RsiDirection.East,
                _ => RsiDirection.South,
            };
        }

        private static Vector2 NormalizeSafe(Vector2 value)
        {
            if (value == Vector2.Zero)
                return Vector2.Zero;

            var len = value.Length();
            return len <= 0f ? Vector2.Zero : value / len;
        }

        private static Vector2 Rotate(Vector2 value, float radians)
        {
            var sin = MathF.Sin(radians);
            var cos = MathF.Cos(radians);
            return new Vector2(value.X * cos - value.Y * sin, value.X * sin + value.Y * cos);
        }

        private static Vector2i[] CardinalDirections()
        {
            return
            [
                new Vector2i(0, -1),
                new Vector2i(1, 0),
                new Vector2i(0, 1),
                new Vector2i(-1, 0),
            ];
        }

        private static bool InsideMap(Vector2 pos)
        {
            return pos is { X: >= 0 and <= GridWidth, Y: >= 0 and <= GridHeight };
        }

        private static bool IsSolidTile(RoomData room, int tx, int ty)
        {
            if (tx < 0 || ty < 0 || tx >= GridWidth || ty >= GridHeight)
                return true;

            if (room.Tiles[tx, ty] == TileType.Door)
                return !room.Cleared;

            return room.Tiles[tx, ty] != TileType.Floor;
        }

        private static bool IsSolid(Vector2 pos, RoomData room)
        {
            var tx = (int) MathF.Floor(pos.X);
            var ty = (int) MathF.Floor(pos.Y);
            return IsSolidTile(room, tx, ty);
        }

        private static bool TouchesBlockedTile(Vector2 pos, RoomData room)
        {
            var tx = (int) MathF.Floor(pos.X);
            var ty = (int) MathF.Floor(pos.Y);
            return IsSolidTile(room, tx, ty);
        }

        private static Vector2 ResolveCircleTileCollision(Vector2 target, float radius, RoomData room)
        {
            var resolved = target;

            var minX = Math.Max(0, (int)MathF.Floor(resolved.X - radius) - 1);
            var maxX = Math.Min(GridWidth - 1, (int)MathF.Floor(resolved.X + radius) + 1);
            var minY = Math.Max(0, (int)MathF.Floor(resolved.Y - radius) - 1);
            var maxY = Math.Min(GridHeight - 1, (int)MathF.Floor(resolved.Y + radius) + 1);

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    if (!IsSolidTile(room, x, y))
                        continue;

                    var nearestX = Math.Clamp(resolved.X, x, x + 1f);
                    var nearestY = Math.Clamp(resolved.Y, y, y + 1f);
                    var delta = resolved - new Vector2(nearestX, nearestY);
                    var distance = delta.Length();

                    if (distance >= radius)
                        continue;

                    if (distance <= 0.0001f)
                    {
                        delta = new Vector2(1, 0);
                        distance = 1f;
                    }

                    resolved += delta / distance * (radius - distance);
                }
            }

            resolved.X = Math.Clamp(resolved.X, radius, GridWidth - radius);
            resolved.Y = Math.Clamp(resolved.Y, radius, GridHeight - radius);
            return resolved;
        }

        #endregion

        #region Input State

        private sealed class DeepMaintenanceInputState
        {
            private static readonly BoundKeyFunction[] SupportedKeyFunctions =
            [
                ContentKeyFunctions.DeepMaintenanceMoveUp,
                ContentKeyFunctions.DeepMaintenanceMoveDown,
                ContentKeyFunctions.DeepMaintenanceMoveLeft,
                ContentKeyFunctions.DeepMaintenanceMoveRight,
                ContentKeyFunctions.DeepMaintenanceShootUp,
                ContentKeyFunctions.DeepMaintenanceShootDown,
                ContentKeyFunctions.DeepMaintenanceShootLeft,
                ContentKeyFunctions.DeepMaintenanceShootRight,
                EngineKeyFunctions.MoveUp,
                EngineKeyFunctions.MoveDown,
                EngineKeyFunctions.MoveLeft,
                EngineKeyFunctions.MoveRight,
                ContentKeyFunctions.ArcadeUp,
                ContentKeyFunctions.ArcadeDown,
                ContentKeyFunctions.ArcadeLeft,
                ContentKeyFunctions.ArcadeRight,
            ];

            private readonly IInputManager _input;
            private readonly HashSet<BoundKeyFunction> _heldKeys = new();

            public DeepMaintenanceInputState(IInputManager input)
            {
                _input = input;
            }

            #region State Mutation

            public void Add(BoundKeyFunction function)
            {
                _heldKeys.Add(function);
            }

            public void Remove(BoundKeyFunction function)
            {
                _heldKeys.Remove(function);
            }

            public void Clear()
            {
                _heldKeys.Clear();
            }

            #endregion

            #region State Queries

            public bool IsRelevantKey(BoundKeyFunction function)
            {
                return SupportedKeyFunctions.Contains(function);
            }

            public bool AnyShootKeyHeld()
            {
                return _heldKeys.Any(IsShootKey);
            }

            public bool IsMoveUpHeld() => _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceMoveUp) || _heldKeys.Contains(EngineKeyFunctions.MoveUp);
            public bool IsMoveDownHeld() => _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceMoveDown) || _heldKeys.Contains(EngineKeyFunctions.MoveDown);
            public bool IsMoveLeftHeld() => _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceMoveLeft) || _heldKeys.Contains(EngineKeyFunctions.MoveLeft);
            public bool IsMoveRightHeld() => _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceMoveRight) || _heldKeys.Contains(EngineKeyFunctions.MoveRight);
            public bool IsShootUpHeld() => _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceShootUp) || _heldKeys.Contains(ContentKeyFunctions.ArcadeUp);
            public bool IsShootDownHeld() => _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceShootDown) || _heldKeys.Contains(ContentKeyFunctions.ArcadeDown);
            public bool IsShootLeftHeld() => _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceShootLeft) || _heldKeys.Contains(ContentKeyFunctions.ArcadeLeft);
            public bool IsShootRightHeld() => _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceShootRight) || _heldKeys.Contains(ContentKeyFunctions.ArcadeRight);

            #endregion

            #region Binding Resolution

            public bool TryGetBoundFunction(KeyEventArgs keyEvent, out BoundKeyFunction function)
            {
                foreach (var keyFunction in SupportedKeyFunctions)
                {
                    if (!IsKeyBindingMatch(keyFunction, keyEvent))
                        continue;

                    function = keyFunction;
                    return true;
                }

                function = default!;
                return false;
            }

            public static bool TryGetShootDirection(BoundKeyFunction function, out Vector2 direction)
            {
                if (function == ContentKeyFunctions.DeepMaintenanceShootUp || function == ContentKeyFunctions.ArcadeUp)
                {
                    direction = new Vector2(0, -1);
                    return true;
                }

                if (function == ContentKeyFunctions.DeepMaintenanceShootDown || function == ContentKeyFunctions.ArcadeDown)
                {
                    direction = new Vector2(0, 1);
                    return true;
                }

                if (function == ContentKeyFunctions.DeepMaintenanceShootLeft || function == ContentKeyFunctions.ArcadeLeft)
                {
                    direction = new Vector2(-1, 0);
                    return true;
                }

                if (function == ContentKeyFunctions.DeepMaintenanceShootRight || function == ContentKeyFunctions.ArcadeRight)
                {
                    direction = new Vector2(1, 0);
                    return true;
                }

                direction = Vector2.Zero;
                return false;
            }

            #endregion

            #region Utilities

            private bool IsKeyBindingMatch(BoundKeyFunction function, KeyEventArgs keyEvent)
            {
                if (!_input.TryGetKeyBinding(function, out var binding))
                    return false;

                if (binding.BaseKey != keyEvent.Key)
                    return false;

                if (keyEvent.Shift && !HasModifier(binding, Keyboard.Key.Shift))
                    return false;

                if (keyEvent.Alt && !HasModifier(binding, Keyboard.Key.Alt))
                    return false;

                if (keyEvent.Control && !HasModifier(binding, Keyboard.Key.Control))
                    return false;

                return true;
            }

            private static bool HasModifier(IKeyBinding binding, Keyboard.Key key)
            {
                return binding.Mod1 == key || binding.Mod2 == key || binding.Mod3 == key;
            }

            private static bool IsShootKey(BoundKeyFunction function)
            {
                return function == ContentKeyFunctions.DeepMaintenanceShootUp ||
                       function == ContentKeyFunctions.DeepMaintenanceShootDown ||
                       function == ContentKeyFunctions.DeepMaintenanceShootLeft ||
                       function == ContentKeyFunctions.DeepMaintenanceShootRight ||
                       function == ContentKeyFunctions.ArcadeUp ||
                       function == ContentKeyFunctions.ArcadeDown ||
                       function == ContentKeyFunctions.ArcadeLeft ||
                       function == ContentKeyFunctions.ArcadeRight;
            }

            #endregion
        }

        #endregion

        private enum FacingDirection : byte
        {
            Down,
            Left,
            Right,
            Up,
        }

        private enum TileType : byte
        {
            Floor,
            Wall,
            Obstacle,
            Door,
        }

        private enum RoomType : byte
        {
            Start,
            Normal,
            Treasure,
            Boss,
        }

        private sealed class RoomData
        {
            public readonly RoomType Type;
            public readonly Vector2i MapPosition;
            public TileType[,] Tiles;
            public readonly List<EnemyData> Enemies = new();
            public readonly Dictionary<Vector2i, int> Neighbors = new();
            public bool Cleared;
            public bool IsSecret;
            public bool TreasureBoxOpened;
            public bool HasTreasureRelic;
            public bool TreasureRelicTaken;
            public string? TreasureRelicId;
            public bool DoorTargetOpen;
            public int DoorTransitionTicks;

            public RoomData(RoomType type, Vector2i mapPosition, TileType[,] tiles)
            {
                Type = type;
                MapPosition = mapPosition;
                Tiles = tiles;
            }
        }

        private sealed class EnemyData
        {
            public readonly DeepMaintenanceEntityPrototype Prototype;
            public Vector2 Position;
            public Vector2 PreviousPosition;
            public int Hp;
            public int ShootCooldownTicks;
            public int AggroDelayTicks;
            public int WallContactTicks;
            public int EscapeTicksRemaining;
            public int StrafeDirection = 1;
            public int StrafeSwapTicks;
            public int SpawnGraceTicks;
            public FacingDirection BodyFacing = FacingDirection.Down;
            public FacingDirection ShootFacing = FacingDirection.Down;
            public float DamageFlash;

            public EnemyData(DeepMaintenanceEntityPrototype prototype, Vector2 position, int aggroDelayTicks, int spawnGraceTicks)
            {
                Prototype = prototype;
                Position = position;
                PreviousPosition = position;
                Hp = prototype.MaxHp;
                ShootCooldownTicks = prototype.ShootCooldownTicks;
                AggroDelayTicks = aggroDelayTicks;
                StrafeSwapTicks = 8;
                SpawnGraceTicks = spawnGraceTicks;
            }
        }

        private sealed class ProjectileData
        {
            public Vector2 Position;
            public Vector2 PreviousPosition;
            public readonly Vector2 SourcePosition;
            public Vector2 Velocity;
            public readonly Vector2 Direction;
            public readonly float InitialLifetime;
            public readonly float Radius;
            public readonly float Damage;
            public float Lifetime;
            public readonly string SpritePath;
            public readonly string SpriteState;
            public readonly float SpriteScale;
            public readonly DeepMaintenanceProjectilePrototype Prototype;
            public readonly Color Tint;

            public ProjectileData(Vector2 position, Vector2 sourcePosition, Vector2 velocity, float radius, float damage, float lifetime, string spritePath, string spriteState, float spriteScale, DeepMaintenanceProjectilePrototype prototype, Color tint)
            {
                Position = position;
                PreviousPosition = position;
                SourcePosition = sourcePosition;
                Velocity = velocity;
                Direction = NormalizeSafe(velocity);
                Radius = radius;
                Damage = damage;
                Lifetime = lifetime;
                InitialLifetime = lifetime;
                SpritePath = spritePath;
                SpriteState = spriteState;
                SpriteScale = spriteScale;
                Prototype = prototype;
                Tint = tint;
            }
        }
    }
}
