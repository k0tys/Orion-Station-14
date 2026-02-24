using System.Linq;
using System.Numerics;
using Content.Shared._Orion.CartridgeLoader.Cartridges;
using Content.Shared.Input;
using Robust.Client.Audio;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Audio;
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

        private readonly SpriteSystem _sprite;
        private readonly AudioSystem _audio;
        private readonly Random _random = new();
        private readonly Dictionary<(string, string), Texture> _spriteCache = new();
        private readonly Dictionary<(string, string, RsiDirection), Texture> _directionalSpriteCache = new();

        private readonly List<RoomData> _rooms = new();
        private readonly List<ProjectileData> _playerProjectiles = new();
        private readonly List<ProjectileData> _enemyProjectiles = new();
        private readonly HashSet<int> _visitedRooms = new();

        private readonly HashSet<BoundKeyFunction> _heldKeys = new();
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

        private DeepMaintenanceEntityPrototype _playerProto = default!;
        private DeepMaintenanceEntityPrototype _chaserProto = default!;
        private DeepMaintenanceEntityPrototype _shooterProto = default!;
        private DeepMaintenanceEntityPrototype _bossProto = default!;

        private DeepMaintenanceModifierPrototype _projectileGrowthModifier = default!;

        private DeepMaintenanceTilePrototype _floorProto = default!;
        private DeepMaintenanceTilePrototype _wallProto = default!;
        private DeepMaintenanceTilePrototype _obstacleProto = default!;

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
        private bool _projectileGrowthBuff;

        private Vector2? _treasureBoxPosition;
        private bool _treasureBoxOpened;
        private Vector2? _treasureToolboxPosition;

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
        private const string TreasureCrateSpritePath = "/Textures/_Orion/DeepMaintenance/Crates/treasure.rsi";

        private const string EntityPlayerPrototypeId = "DeepMaintenancePlayer";
        private const string EntityChaserPrototypeId = "DeepMaintenanceChaser";
        private const string EntityShooterPrototypeId = "DeepMaintenanceShooter";
        private const string EntityBossPrototypeId = "DeepMaintenanceBoss";

        private const string TileFloorPrototypeId = "DeepMaintenanceFloor";
        private const string TileWallPrototypeId = "DeepMaintenanceWall";
        private const string TileObstaclePrototypeId = "DeepMaintenanceObstacle";

        private const string ModifierProjectileGrowthPrototypeId = "DeepMaintenanceModifierProjectileGrowth";

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
            _heldKeys.Clear();
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

            if (!TryGetBoundFunction(keyEvent, out var function))
                return;

            switch (type)
            {
                case KeyEventType.Down:
                    _heldKeys.Add(function);

                    if (TryGetShootDirection(function, out var shootDirection))
                        TryShoot(shootDirection);

                    keyEvent.Handle();
                    break;
                case KeyEventType.Up:
                    _heldKeys.Remove(function);
                    keyEvent.Handle();
                    break;
            }
        }

        private bool TryGetBoundFunction(KeyEventArgs keyEvent, out BoundKeyFunction function)
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
            _heldKeys.Clear();

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
            _gameOver = false;
            _victory = false;
            _accumulator = 0f;
            _heartDamageFlash = 0f;
            _playerDamageFlash = 0f;

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

            if (!IsRelevantKey(args.Function))
                return;

            _heldKeys.Add(args.Function);

            if (TryGetShootDirection(args.Function, out var shootDirection))
                TryShoot(shootDirection);

            args.Handle();
        }

        protected override void KeyBindUp(GUIBoundKeyEventArgs args)
        {
            base.KeyBindUp(args);

            if (!IsRelevantKey(args.Function))
                return;

            _heldKeys.Remove(args.Function);
            args.Handle();
        }

        #endregion

        #region Update

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);

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
            TickEnemies(dt);
            TickProjectiles(_playerProjectiles, true, dt);
            TickProjectiles(_enemyProjectiles, false, dt);
            HandleContactDamage();
            HandleTreasureInteractions();
            HandleRoomState();

            InvalidateMeasure();
            StateChanged?.Invoke();
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

        private void MovePlayer(float dt)
        {
            var moveDirection = GetMoveDirection();
            if (moveDirection == Vector2.Zero)
            {
                _playerVelocity = Vector2.Zero;
                return;
            }

            _playerBodyFacing = FacingFromVector(moveDirection, _playerBodyFacing);
            _playerBodyFacingResetTimer = FacingResetDelaySeconds;
            _playerVelocity = moveDirection * _playerProto.MoveSpeed;

            var target = _playerPos + moveDirection * _playerProto.MoveSpeed * dt;
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
            if (_heldKeys.Any(IsShootKeyHeld))
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

        private static bool IsShootKeyHeld(BoundKeyFunction function)
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
            return _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceMoveUp) ||
                   _heldKeys.Contains(EngineKeyFunctions.MoveUp);
        }

        private bool IsMoveDownHeld()
        {
            return _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceMoveDown) ||
                   _heldKeys.Contains(EngineKeyFunctions.MoveDown);
        }

        private bool IsMoveLeftHeld()
        {
            return _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceMoveLeft) ||
                   _heldKeys.Contains(EngineKeyFunctions.MoveLeft);
        }

        private bool IsMoveRightHeld()
        {
            return _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceMoveRight) ||
                   _heldKeys.Contains(EngineKeyFunctions.MoveRight);
        }

        private bool IsShootUpHeld()
        {
            return _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceShootUp) ||
                   _heldKeys.Contains(ContentKeyFunctions.ArcadeUp);
        }

        private bool IsShootDownHeld()
        {
            return _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceShootDown) ||
                   _heldKeys.Contains(ContentKeyFunctions.ArcadeDown);
        }

        private bool IsShootLeftHeld()
        {
            return _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceShootLeft) ||
                   _heldKeys.Contains(ContentKeyFunctions.ArcadeLeft);
        }

        private bool IsShootRightHeld()
        {
            return _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceShootRight) ||
                   _heldKeys.Contains(ContentKeyFunctions.ArcadeRight);
        }

        private void TickEnemies(float dt)
        {
            foreach (var enemy in CurrentRoom.Enemies.Where(enemy => enemy.Hp > 0))
            {
                enemy.PreviousPosition = enemy.Position;

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

            SpawnProjectile(_enemyProjectiles, enemy.Position, baseVelocity, projectilePrototype, 1f);

            if (!enemy.Prototype.IsBoss)
                return;

            const float angle = MathF.PI * BossSpreadAngleDegrees / 180f;
            var leftVelocity = Rotate(baseVelocity, -angle);
            var rightVelocity = Rotate(baseVelocity, angle);
            SpawnProjectile(_enemyProjectiles, enemy.Position, leftVelocity, projectilePrototype, 1f);
            SpawnProjectile(_enemyProjectiles, enemy.Position, rightVelocity, projectilePrototype, 1f);
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

                enemy.Hp -= projectile.Damage;
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
                if (enemy.Hp <= 0)
                    continue;

                if (Vector2.Distance(_playerPos, enemy.Position) > _playerProto.Radius + enemy.Prototype.Radius)
                    continue;

                DamagePlayer();
                return;
            }
        }

        private void HandleTreasureInteractions()
        {
            if (CurrentRoom.Type != RoomType.Treasure)
                return;

            if (!_treasureBoxOpened && _treasureBoxPosition is { } boxPosition)
            {
                if (Vector2.Distance(_playerPos, boxPosition) <= _playerProto.Radius + TreasureObjectRadius)
                {
                    OpenTreasureBox();
                }
            }

            if (_treasureToolboxPosition is not { } toolboxPosition)
                return;

            if (Vector2.Distance(_playerPos, toolboxPosition) > _playerProto.Radius + TreasureObjectRadius)
                return;

            _treasureToolboxPosition = null;
            CurrentRoom.TreasureToolboxTaken = true;
            _projectileGrowthBuff = true;
            StateChanged?.Invoke();
        }

        private void OpenTreasureBox()
        {
            _treasureBoxOpened = true;
            CurrentRoom.TreasureBoxOpened = true;

            if (_random.NextDouble() < 0.5)
            {
                var pos = new Vector2(GridWidth * 0.5f, GridHeight * 0.5f);
                var proto = _random.NextDouble() < 0.5 ? _chaserProto : _shooterProto;
                CurrentRoom.Enemies.Add(new EnemyData(proto, pos, _random.Next(EnemyAggroDelayTicksMin, EnemyAggroDelayTicksMax + 1)));
                return;
            }

            _treasureToolboxPosition = new Vector2(GridWidth * 0.5f, GridHeight * 0.5f);
            CurrentRoom.HasTreasureToolbox = true;
        }

        private void DamagePlayer()
        {
            if (_invulnerabilityTicks > 0)
                return;

            SetPlayerHealth(PlayerHp - 1, MaxPlayerHp);
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

        private void SetPlayerHealth(int current, int max)
        {
            MaxPlayerHp = Math.Max(1, max);
            PlayerHp = Math.Clamp(current, 0, MaxPlayerHp);
        }

        private void HandleRoomState()
        {
            var room = CurrentRoom;
            if (room.Cleared)
                return;

            if (room.Enemies.Any(enemy => enemy.Hp > 0))
                return;

            room.Cleared = true;
            if (room.Type == RoomType.Boss)
                _victory = true;
        }

        private void TryRoomTransition()
        {
            var room = CurrentRoom;
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

            if (centerPlayer)
                _playerPos = new Vector2(GridWidth * 0.5f, GridHeight * 0.5f);

            var room = CurrentRoom;
            if (room.Type != RoomType.Treasure)
            {
                _treasureBoxOpened = false;
                _treasureToolboxPosition = null;
                _treasureBoxPosition = null;
                return;
            }

            _treasureBoxOpened = room.TreasureBoxOpened;
            _treasureBoxPosition = new Vector2(GridWidth * 0.5f, GridHeight * 0.5f);
            _treasureToolboxPosition = room.HasTreasureToolbox && !room.TreasureToolboxTaken
                ? new Vector2(GridWidth * 0.5f, GridHeight * 0.5f)
                : null;
        }

        private void GenerateMap()
        {
            const int roomCount = 5;
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

            const int bossIndex = roomCount - 1;
            const int treasureIndex = roomCount - 2;

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
                    room.Cleared = true;
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

            if (roomType == RoomType.Start || roomType == RoomType.Boss)
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

        private void SpawnEnemies(RoomData room)
        {
            room.Enemies.Clear();

            switch (room.Type)
            {
                case RoomType.Start:
                case RoomType.Treasure:
                    return;
                case RoomType.Boss:
                    room.Enemies.Add(new EnemyData(_bossProto, new Vector2(GridWidth * 0.5f, 2.5f), _random.Next(EnemyAggroDelayTicksMin, EnemyAggroDelayTicksMax + 1)));
                    return;
            }

            var count = _random.Next(3, 6);
            for (var i = 0; i < count; i++)
            {
                var pos = new Vector2(_random.NextSingle() * (GridWidth - 4) + 2f, _random.NextSingle() * (GridHeight - 4) + 2f);
                var proto = _random.NextDouble() < 0.25 ? _shooterProto : _chaserProto;
                room.Enemies.Add(new EnemyData(proto, pos, _random.Next(EnemyAggroDelayTicksMin, EnemyAggroDelayTicksMax + 1)));
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
                        DrawDoor(handle, box, CurrentRoom.Cleared, x, y);
                    }
                }
            }

            var tickAlpha = Math.Clamp(_accumulator / TickSeconds, 0f, 1f);

            foreach (var enemy in CurrentRoom.Enemies.Where(enemy => enemy.Hp > 0))
            {
                var drawPos = Vector2.Lerp(enemy.PreviousPosition, enemy.Position, tickAlpha);
                var color = enemy.DamageFlash > 0f ? Color.IndianRed : Color.White;
                DrawCharacter(handle, drawPos, enemy.Prototype, enemy.BodyFacing, enemy.ShootFacing, tilePixel, mapOffset, color);
            }

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

            DrawPlayer(handle, tilePixel * GetPlayerVisualScale(), mapOffset);
            DrawTreasureObjects(handle, tilePixel, mapOffset);
            DrawHealthHearts(handle);
            DrawBuffIcons(handle);
            DrawMinimap(handle);
        }

        private void DrawPlayer(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset)
        {
            var color = _playerDamageFlash > 0f ? Color.IndianRed : Color.White;
            DrawCharacter(handle, _playerPos, _playerProto, _playerBodyFacing, _playerShootFacing, tilePixel, mapOffset, color);
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

            if (GetSprite(spritePath, spriteState) is { } texture)
            {
                handle.DrawTextureRect(texture, box);
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
            var doorSprite = GetDoorSpritePath(doorType);
            var state = opened ? "open" : "closed";

            if (GetSprite(doorSprite, state) is { } texture)
            {
                handle.DrawTextureRect(texture, box);
                return;
            }

            handle.DrawRect(box, opened ? Color.DarkSlateGray : Color.DarkRed);
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

        private static string GetDoorSpritePath(RoomType roomType)
        {
            return roomType switch
            {
                RoomType.Boss => "/Textures/Structures/Doors/Airlocks/Standard/syndicate.rsi",
                RoomType.Treasure => "/Textures/Structures/Doors/Airlocks/Standard/command.rsi",
                _ => "/Textures/Structures/Doors/Airlocks/Standard/maint.rsi",
            };
        }

        private void DrawProjectile(DrawingHandleScreen handle, Vector2 pos, ProjectileData projectile, float tilePixel, Vector2 mapOffset)
        {
            var center = mapOffset + pos * tilePixel;
            var size = tilePixel * projectile.Radius * 3f * MathF.Max(0.05f, projectile.SpriteScale);
            var box = UIBox2.FromDimensions(center - new Vector2(size * 0.5f, size * 0.5f), new Vector2(size, size));

            if (GetSprite(projectile.SpritePath, projectile.SpriteState) is { } texture)
                handle.DrawTextureRect(texture, box);
        }

        private void DrawTreasureObjects(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset)
        {
            if (_treasureBoxPosition is { } boxPos)
            {
                var state = _treasureBoxOpened ? "open" : "closed";
                DrawEntity(handle, boxPos, TreasureCrateSpritePath, state, tilePixel, mapOffset);
            }

            if (_treasureToolboxPosition is { } toolboxPos)
                DrawEntity(handle, toolboxPos, "/Textures/Objects/Tools/Toolboxes/toolbox_red.rsi", "icon", tilePixel, mapOffset);
        }

        private void DrawBuffIcons(DrawingHandleScreen handle)
        {
            if (!_projectileGrowthBuff)
                return;

            if (GetSprite(_projectileGrowthModifier.HudIconSpritePath, _projectileGrowthModifier.HudIconSpriteState) is not { } texture)
                return;

            var box = UIBox2.FromDimensions(new Vector2(6f, 38f), new Vector2(24f, 24f));
            handle.DrawTextureRect(texture, box);
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
            _projectileGrowthModifier = _prototype.Index<DeepMaintenanceModifierPrototype>(ModifierProjectileGrowthPrototypeId);

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

            WarmupSprite(_projectileGrowthModifier.HudIconSpritePath, _projectileGrowthModifier.HudIconSpriteState);

            WarmupSprite("/Textures/Structures/Doors/Airlocks/Standard/maint.rsi", "closed");
            WarmupSprite("/Textures/Structures/Doors/Airlocks/Standard/maint.rsi", "open");
            WarmupSprite("/Textures/Structures/Doors/Airlocks/Standard/command.rsi", "closed");
            WarmupSprite("/Textures/Structures/Doors/Airlocks/Standard/command.rsi", "open");
            WarmupSprite("/Textures/Structures/Doors/Airlocks/Standard/syndicate.rsi", "closed");
            WarmupSprite("/Textures/Structures/Doors/Airlocks/Standard/syndicate.rsi", "open");

            WarmupSprite(TreasureCrateSpritePath, "closed");
            WarmupSprite(TreasureCrateSpritePath, "open");
            WarmupSprite("/Textures/Objects/Tools/Toolboxes/toolbox_red.rsi", "icon");

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

            var projectilePrototype = _prototype.Index<DeepMaintenanceProjectilePrototype>(_playerProto.ProjectilePrototype);
            SpawnProjectile(_playerProjectiles, _playerPos, normalized * projectilePrototype.Speed, projectilePrototype, GetProjectileScaleMultiplier());
            _playerShootCooldown = GetShootCooldown(_playerProto);
            PlaySfx(SfxPlayerShoot, -6f);
        }

        private static void SpawnProjectile(List<ProjectileData> container, Vector2 position, Vector2 velocity, DeepMaintenanceProjectilePrototype projectilePrototype, float radiusScale)
        {
            container.Add(new ProjectileData(
                position,
                position,
                velocity,
                projectilePrototype.Radius * radiusScale,
                projectilePrototype.Damage,
                projectilePrototype.Lifetime,
                projectilePrototype.SpritePath,
                projectilePrototype.SpriteState,
                projectilePrototype.SpriteScale));
        }

        private float GetProjectileScaleMultiplier()
        {
            return _projectileGrowthBuff ? _projectileGrowthModifier.ProjectileScaleMultiplier : 1f;
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

        private void PlaySfx(SoundSpecifier sound, float volume)
        {
            _audio.PlayGlobal(sound, Filter.Local(), false, AudioParams.Default.WithVolume(volume));
        }

        private static bool IsRelevantKey(BoundKeyFunction function)
        {
            return function == ContentKeyFunctions.DeepMaintenanceMoveUp ||
                   function == ContentKeyFunctions.DeepMaintenanceMoveDown ||
                   function == ContentKeyFunctions.DeepMaintenanceMoveLeft ||
                   function == ContentKeyFunctions.DeepMaintenanceMoveRight ||
                   function == EngineKeyFunctions.MoveUp ||
                   function == EngineKeyFunctions.MoveDown ||
                   function == EngineKeyFunctions.MoveLeft ||
                   function == EngineKeyFunctions.MoveRight ||
                   function == ContentKeyFunctions.DeepMaintenanceShootUp ||
                   function == ContentKeyFunctions.DeepMaintenanceShootDown ||
                   function == ContentKeyFunctions.DeepMaintenanceShootLeft ||
                   function == ContentKeyFunctions.DeepMaintenanceShootRight ||
                   function == ContentKeyFunctions.ArcadeUp ||
                   function == ContentKeyFunctions.ArcadeDown ||
                   function == ContentKeyFunctions.ArcadeLeft ||
                   function == ContentKeyFunctions.ArcadeRight;
        }

        private static bool TryGetShootDirection(BoundKeyFunction function, out Vector2 direction)
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

        private static FacingDirection FacingFromVector(Vector2 direction, FacingDirection fallback)
        {
            if (direction == Vector2.Zero)
                return fallback;

            if (MathF.Abs(direction.X) > MathF.Abs(direction.Y))
                return direction.X > 0f ? FacingDirection.Right : FacingDirection.Left;

            return direction.Y > 0f ? FacingDirection.Down : FacingDirection.Up;
        }

        private Texture? GetDirectionalSprite(string? spritePath, string? spriteState, FacingDirection facing)
        {
            if (string.IsNullOrWhiteSpace(spritePath) || string.IsNullOrWhiteSpace(spriteState))
                return null;

            var direction = FacingToRsiDirection(facing);
            var key = (spritePath, spriteState, direction);
            if (_directionalSpriteCache.TryGetValue(key, out var texture))
                return texture;

            if (_resourceCache.TryGetResource<RSIResource>(new ResPath(spritePath), out var resource) && resource.RSI.TryGetState(new RSI.StateId(spriteState), out var state))
            {
                var frames = state.GetFrames(direction);
                if (frames.Length > 0)
                {
                    texture = frames[0];
                    _directionalSpriteCache[key] = texture;
                    return texture;
                }
            }

            return GetSprite(spritePath, spriteState);
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
            public bool HasTreasureToolbox;
            public bool TreasureToolboxTaken;

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
            public FacingDirection BodyFacing = FacingDirection.Down;
            public FacingDirection ShootFacing = FacingDirection.Down;
            public float DamageFlash;

            public EnemyData(DeepMaintenanceEntityPrototype prototype, Vector2 position, int aggroDelayTicks)
            {
                Prototype = prototype;
                Position = position;
                PreviousPosition = position;
                Hp = prototype.MaxHp;
                ShootCooldownTicks = prototype.ShootCooldownTicks;
                AggroDelayTicks = aggroDelayTicks;
                StrafeSwapTicks = 8;
            }
        }

        private sealed class ProjectileData
        {
            public Vector2 Position;
            public Vector2 PreviousPosition;
            public readonly Vector2 SourcePosition;
            public readonly Vector2 Velocity;
            public readonly float Radius;
            public readonly int Damage;
            public float Lifetime;
            public readonly string SpritePath;
            public readonly string SpriteState;
            public readonly float SpriteScale;

            public ProjectileData(Vector2 position, Vector2 sourcePosition, Vector2 velocity, float radius, int damage, float lifetime, string spritePath, string spriteState, float spriteScale)
            {
                Position = position;
                PreviousPosition = position;
                SourcePosition = sourcePosition;
                Velocity = velocity;
                Radius = radius;
                Damage = damage;
                Lifetime = lifetime;
                SpritePath = spritePath;
                SpriteState = spriteState;
                SpriteScale = spriteScale;
            }
        }
    }
}
