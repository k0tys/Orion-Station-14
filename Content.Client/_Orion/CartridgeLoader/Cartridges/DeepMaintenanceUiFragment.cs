using System.Linq;
using System.Numerics;
using Content.Shared._Orion.CartridgeLoader.Cartridges;
using Content.Shared.Input;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
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

        controls.AddChild(MakeButton("Pause", _game.TogglePause));
        controls.AddChild(MakeButton("Restart", _game.Restart));

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
        _hud.Text = $"HP: {_game.PlayerHp}/{_game.MaxPlayerHp} | Room: {_game.RoomIndex + 1}/{_game.RoomCount} | Enemies: {_game.AliveEnemies}";
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

        private readonly SpriteSystem _sprite;
        private readonly Random _random = new();
        private readonly Dictionary<(string, string), Texture> _spriteCache = new();

        private readonly List<RoomData> _rooms = new();
        private readonly List<ProjectileData> _playerProjectiles = new();
        private readonly List<ProjectileData> _enemyProjectiles = new();

        private readonly HashSet<BoundKeyFunction> _heldKeys = new();

        private DeepMaintenanceEntityPrototype _playerProto = default!;
        private DeepMaintenanceEntityPrototype _chaserProto = default!;
        private DeepMaintenanceEntityPrototype _shooterProto = default!;
        private DeepMaintenanceEntityPrototype _bossProto = default!;

        private DeepMaintenanceTilePrototype _floorProto = default!;
        private DeepMaintenanceTilePrototype _wallProto = default!;
        private DeepMaintenanceTilePrototype _obstacleProto = default!;

        private Vector2 _playerPos;
        private float _playerShootCooldown;
        private int _invulnerabilityTicks;
        private bool _paused;
        private bool _gameOver;
        private bool _victory;
        private float _accumulator;

        private const int GridWidth = 12;
        private const int GridHeight = 8;
        private const float TickSeconds = 0.1f;
        private const float ProjectileRadius = 0.09f;
        private const float PlayerShootCooldownSeconds = 0.28f;
        private const int InvulnerabilityTicks = 10;

        public event Action? StateChanged;

        public int PlayerHp { get; private set; }
        public int MaxPlayerHp { get; private set; }
        public int RoomIndex { get; private set; }
        public int RoomCount => _rooms.Count;
        public int AliveEnemies => CurrentRoom.Enemies.Count(e => e.Hp > 0);

        public string Status => _victory
            ? "Victory!"
            : _gameOver
                ? "Game over. Press Restart."
                : _paused
                    ? "Paused"
                    : "WASD = move, Arrows = shoot";

        private RoomData CurrentRoom => _rooms[RoomIndex];

        public DeepMaintenanceGameControl()
        {
            IoCManager.InjectDependencies(this);
            _sprite = _entity.System<SpriteSystem>();

            CanKeyboardFocus = true;
            KeyboardFocusOnClick = true;

            LoadPrototypes();
            Restart();
        }

        protected override void EnteredTree()
        {
            base.EnteredTree();
            EnsureInputFocus();
        }

        protected override void ExitedTree()
        {
            base.ExitedTree();

            _heldKeys.Clear();
            if (HasKeyboardFocus())
                ReleaseKeyboardFocus();
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
            _playerProjectiles.Clear();
            _enemyProjectiles.Clear();
            _heldKeys.Clear();

            RoomIndex = 0;
            MaxPlayerHp = _playerProto.MaxHp;
            PlayerHp = MaxPlayerHp;
            _playerPos = new Vector2(GridWidth * 0.5f, GridHeight * 0.5f);
            _playerShootCooldown = 0f;
            _invulnerabilityTicks = 0;
            _paused = false;
            _gameOver = false;
            _victory = false;
            _accumulator = 0f;

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

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);

            if (_paused || _gameOver || _victory)
                return;

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

            MovePlayer(dt);
            HandleHeldShootKeys();
            TickEnemies(dt);
            TickProjectiles(_playerProjectiles, true, dt);
            TickProjectiles(_enemyProjectiles, false, dt);
            HandleContactDamage();
            HandleRoomState();

            InvalidateMeasure();
            StateChanged?.Invoke();
        }

        private void MovePlayer(float dt)
        {
            var moveDirection = GetMoveDirection();
            if (moveDirection == Vector2.Zero)
                return;

            var target = _playerPos + moveDirection * _playerProto.MoveSpeed * dt;
            _playerPos = ResolveCircleTileCollision(target, _playerProto.Radius, CurrentRoom.Tiles);
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
                if (enemy.ShootCooldownTicks > 0)
                    enemy.ShootCooldownTicks--;

                var toPlayer = _playerPos - enemy.Position;
                var directionToPlayer = NormalizeSafe(toPlayer);

                if (enemy.Prototype.Shooter)
                {
                    if (enemy.ShootCooldownTicks <= 0)
                    {
                        _enemyProjectiles.Add(new ProjectileData(
                            enemy.Position,
                            directionToPlayer * enemy.Prototype.ProjectileSpeed,
                            ProjectileRadius,
                            1));

                        enemy.ShootCooldownTicks = enemy.Prototype.ShootCooldownTicks;
                    }

                    var strafe = new Vector2(-directionToPlayer.Y, directionToPlayer.X);
                    var target = enemy.Position + strafe * enemy.Prototype.MoveSpeed * 0.45f * dt;
                    enemy.Position = ResolveCircleTileCollision(target, enemy.Prototype.Radius, CurrentRoom.Tiles);
                    continue;
                }

                var chase = enemy.Position + directionToPlayer * enemy.Prototype.MoveSpeed * dt;
                enemy.Position = ResolveCircleTileCollision(chase, enemy.Prototype.Radius, CurrentRoom.Tiles);
            }
        }

        private void TickProjectiles(List<ProjectileData> projectiles, bool playerProjectile, float dt)
        {
            for (var i = projectiles.Count - 1; i >= 0; i--)
            {
                var projectile = projectiles[i];
                projectile.Position += projectile.Velocity * dt;

                if (!InsideMap(projectile.Position) || IsSolid(projectile.Position, CurrentRoom.Tiles))
                {
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

        private void DamagePlayer()
        {
            if (_invulnerabilityTicks > 0)
                return;

            PlayerHp--;
            _invulnerabilityTicks = InvulnerabilityTicks;
            if (PlayerHp <= 0)
                _gameOver = true;
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

            const float threshold = 0.25f;
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
            _playerProjectiles.Clear();
            _enemyProjectiles.Clear();
            if (centerPlayer)
                _playerPos = new Vector2(GridWidth * 0.5f, GridHeight * 0.5f);
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
                    OpenDoorway(room.Tiles, direction);
                }
            }
        }

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

        private static void OpenDoorway(TileType[,] tiles, Vector2i direction)
        {
            if (direction == new Vector2i(-1, 0))
            {
                tiles[0, GridHeight / 2] = TileType.Floor;
                return;
            }

            if (direction == new Vector2i(1, 0))
            {
                tiles[GridWidth - 1, GridHeight / 2] = TileType.Floor;
                return;
            }

            if (direction == new Vector2i(0, -1))
            {
                tiles[GridWidth / 2, 0] = TileType.Floor;
                return;
            }

            if (direction == new Vector2i(0, 1))
                tiles[GridWidth / 2, GridHeight - 1] = TileType.Floor;
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
                    room.Enemies.Add(new EnemyData(_bossProto, new Vector2(GridWidth * 0.5f, 2.5f)));
                    return;
            }

            var count = _random.Next(3, 6);
            for (var i = 0; i < count; i++)
            {
                var pos = new Vector2(_random.NextSingle() * (GridWidth - 4) + 2f, _random.NextSingle() * (GridHeight - 4) + 2f);
                var proto = _random.NextDouble() < 0.25 ? _shooterProto : _chaserProto;
                room.Enemies.Add(new EnemyData(proto, pos));
            }
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            base.Draw(handle);

            var tilePixel = MathF.Min(PixelSize.X / GridWidth, PixelSize.Y / GridHeight);
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
                        new Vector2(tilePixel, tilePixel) - Vector2.One);

                    if (GetSprite(tileProto.SpritePath, tileProto.SpriteState) is { } texture)
                        handle.DrawTextureRect(texture, box);
                    else
                        handle.DrawRect(box, tileProto.Color);
                }
            }

            foreach (var enemy in CurrentRoom.Enemies.Where(enemy => enemy.Hp > 0))
            {
                DrawEntity(handle, enemy.Position, enemy.Prototype.Radius, enemy.Prototype.Color, enemy.Prototype.SpritePath, enemy.Prototype.SpriteState, tilePixel, mapOffset);
            }

            foreach (var projectile in _playerProjectiles)
            {
                DrawCircle(handle, projectile.Position, projectile.Radius, Color.Cyan, tilePixel, mapOffset);
            }

            foreach (var projectile in _enemyProjectiles)
            {
                DrawCircle(handle, projectile.Position, projectile.Radius, Color.Yellow, tilePixel, mapOffset);
            }

            var playerColor = _invulnerabilityTicks > 0 ? Color.LightPink : _playerProto.Color;
            DrawEntity(handle, _playerPos, _playerProto.Radius, playerColor, _playerProto.SpritePath, _playerProto.SpriteState, tilePixel, mapOffset);
        }

        private void DrawEntity(DrawingHandleScreen handle, Vector2 pos, float radius, Color color, string? spritePath, string? spriteState, float tilePixel, Vector2 mapOffset)
        {
            var center = mapOffset + pos * tilePixel;
            var pxRadius = radius * tilePixel;
            var box = UIBox2.FromDimensions(center - new Vector2(pxRadius, pxRadius), new Vector2(pxRadius * 2, pxRadius * 2));

            if (GetSprite(spritePath, spriteState) is { } texture)
            {
                handle.DrawTextureRect(texture, box);
                return;
            }

            handle.DrawCircle(center, pxRadius, color);
        }

        private static void DrawCircle(DrawingHandleScreen handle, Vector2 pos, float radius, Color color, float tilePixel, Vector2 mapOffset)
        {
            var center = mapOffset + pos * tilePixel;
            var pxRadius = radius * tilePixel;
            handle.DrawCircle(center, pxRadius, color);
        }

        private void LoadPrototypes()
        {
            _playerProto = _prototype.Index<DeepMaintenanceEntityPrototype>("DeepMaintenancePlayer");
            _chaserProto = _prototype.Index<DeepMaintenanceEntityPrototype>("DeepMaintenanceChaser");
            _shooterProto = _prototype.Index<DeepMaintenanceEntityPrototype>("DeepMaintenanceShooter");
            _bossProto = _prototype.Index<DeepMaintenanceEntityPrototype>("DeepMaintenanceBoss");

            _floorProto = _prototype.Index<DeepMaintenanceTilePrototype>("DeepMaintenanceFloor");
            _wallProto = _prototype.Index<DeepMaintenanceTilePrototype>("DeepMaintenanceWall");
            _obstacleProto = _prototype.Index<DeepMaintenanceTilePrototype>("DeepMaintenanceObstacle");

            WarmupSprite(_floorProto.SpritePath, _floorProto.SpriteState);
            WarmupSprite(_wallProto.SpritePath, _wallProto.SpriteState);
            WarmupSprite(_obstacleProto.SpritePath, _obstacleProto.SpriteState);
            WarmupSprite(_playerProto.SpritePath, _playerProto.SpriteState);
            WarmupSprite(_chaserProto.SpritePath, _chaserProto.SpriteState);
            WarmupSprite(_shooterProto.SpritePath, _shooterProto.SpriteState);
            WarmupSprite(_bossProto.SpritePath, _bossProto.SpriteState);
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

        private Texture? GetSprite(string? spritePath, string? spriteState)
        {
            if (string.IsNullOrWhiteSpace(spritePath) || string.IsNullOrWhiteSpace(spriteState))
                return null;

            return _spriteCache.GetValueOrDefault((spritePath, spriteState));
        }

        private void TryShoot(Vector2 direction)
        {
            if (_paused || _gameOver || _victory || _playerShootCooldown > 0f)
                return;

            var normalized = NormalizeSafe(direction);
            if (normalized == Vector2.Zero)
                return;

            _playerProjectiles.Add(new ProjectileData(_playerPos, normalized * _playerProto.ProjectileSpeed, ProjectileRadius, 1));
            _playerShootCooldown = PlayerShootCooldownSeconds;
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

        private static Vector2 NormalizeSafe(Vector2 value)
        {
            if (value == Vector2.Zero)
                return Vector2.Zero;

            var len = value.Length();
            return len <= 0f ? Vector2.Zero : value / len;
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

        private static bool IsSolid(Vector2 pos, TileType[,] tiles)
        {
            var tx = (int) MathF.Floor(pos.X);
            var ty = (int) MathF.Floor(pos.Y);
            if (tx < 0 || ty < 0 || tx >= GridWidth || ty >= GridHeight)
                return true;

            return tiles[tx, ty] != TileType.Floor;
        }

        private static Vector2 ResolveCircleTileCollision(Vector2 target, float radius, TileType[,] tiles)
        {
            var resolved = target;

            for (var pass = 0; pass < 2; pass++)
            {
                var probes = new[]
                {
                    resolved with { X = resolved.X + radius },
                    resolved with { X = resolved.X - radius },
                    resolved with { Y = resolved.Y + radius },
                    resolved with { Y = resolved.Y - radius },
                };

                foreach (var check in probes)
                {
                    if (!IsSolid(check, tiles))
                        continue;

                    var tx = Math.Clamp((int)MathF.Floor(check.X), 0, GridWidth - 1);
                    var ty = Math.Clamp((int)MathF.Floor(check.Y), 0, GridHeight - 1);
                    var center = new Vector2(tx + 0.5f, ty + 0.5f);
                    var away = NormalizeSafe(resolved - center);

                    if (away == Vector2.Zero)
                        away = new Vector2(1, 0);

                    resolved = center + away * 0.52f;
                }
            }

            resolved.X = Math.Clamp(resolved.X, radius, GridWidth - radius);
            resolved.Y = Math.Clamp(resolved.Y, radius, GridHeight - radius);
            return resolved;
        }

        private enum TileType : byte
        {
            Floor,
            Wall,
            Obstacle,
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
            public readonly TileType[,] Tiles;
            public readonly List<EnemyData> Enemies = new();
            public readonly Dictionary<Vector2i, int> Neighbors = new();
            public bool Cleared;

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
            public int Hp;
            public int ShootCooldownTicks;

            public EnemyData(DeepMaintenanceEntityPrototype prototype, Vector2 position)
            {
                Prototype = prototype;
                Position = position;
                Hp = prototype.MaxHp;
                ShootCooldownTicks = prototype.ShootCooldownTicks;
            }
        }

        private sealed class ProjectileData
        {
            public Vector2 Position;
            public readonly Vector2 Velocity;
            public readonly float Radius;
            public readonly int Damage;

            public ProjectileData(Vector2 position, Vector2 velocity, float radius, int damage)
            {
                Position = position;
                Velocity = velocity;
                Radius = radius;
                Damage = damage;
            }
        }
    }
}
