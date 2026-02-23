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

        private DeepMaintenanceEntityPrototype _playerProto = default!;
        private DeepMaintenanceEntityPrototype _chaserProto = default!;
        private DeepMaintenanceEntityPrototype _shooterProto = default!;
        private DeepMaintenanceEntityPrototype _bossProto = default!;

        private DeepMaintenanceTilePrototype _floorProto = default!;
        private DeepMaintenanceTilePrototype _wallProto = default!;
        private DeepMaintenanceTilePrototype _obstacleProto = default!;

        private float _playerShootCooldown;
        private int _invulnerabilityTicks;
        private bool _paused;
        private bool _gameOver;
        private bool _victory;
        private float _accumulator;
        private Vector2 _playerPos;
        private Vector2 _moveInput;

        public event Action? StateChanged;

        private const int GridWidth = 12;
        private const int GridHeight = 8;
        private const float TickSeconds = 0.1f;
        private const float ProjectileRadius = 0.09f;
        private const float PlayerShootCooldownSeconds = 0.28f;

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

            _moveInput = Vector2.Zero;

            if (HasKeyboardFocus())
                ReleaseKeyboardFocus();
        }

        public void EnsureInputFocus()
        {
            if (_paused || _gameOver || _victory)
                return;

            if (!IsInsideTree || HasKeyboardFocus() || !CanKeyboardFocus)
                return;

            GrabKeyboardFocus();
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

            var key = (spritePath, spriteState);
            return _spriteCache.GetValueOrDefault(key);
        }

        public void Restart()
        {
            _rooms.Clear();
            _playerProjectiles.Clear();
            _enemyProjectiles.Clear();
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
            _moveInput = Vector2.Zero;

            GenerateMap();
            EnterRoom(0, true);
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

        private void Shoot(Vector2 direction)
        {
            if (_paused || _gameOver || _victory)
                return;

            if (_playerShootCooldown > 0f)
                return;

            var norm = NormalizeSafe(direction);
            if (norm == Vector2.Zero)
                return;

            _playerProjectiles.Add(new ProjectileData(_playerPos, norm * _playerProto.ProjectileSpeed, ProjectileRadius, 1));
            _playerShootCooldown = PlayerShootCooldownSeconds;
        }

        protected override void KeyBindDown(GUIBoundKeyEventArgs args)
        {
            base.KeyBindDown(args);

            EnsureInputFocus();

            if (args.Function == ContentKeyFunctions.DeepMaintenanceMoveUp || args.Function == EngineKeyFunctions.MoveUp)
            {
                _moveInput.Y = -1;
                args.Handle();
                return;
            }

            if (args.Function == ContentKeyFunctions.DeepMaintenanceMoveDown || args.Function == EngineKeyFunctions.MoveDown)
            {
                _moveInput.Y = 1;
                args.Handle();
                return;
            }

            if (args.Function == ContentKeyFunctions.DeepMaintenanceMoveLeft || args.Function == EngineKeyFunctions.MoveLeft)
            {
                _moveInput.X = -1;
                args.Handle();
                return;
            }

            if (args.Function == ContentKeyFunctions.DeepMaintenanceMoveRight || args.Function == EngineKeyFunctions.MoveRight)
            {
                _moveInput.X = 1;
                args.Handle();
                return;
            }

            if (args.Function == ContentKeyFunctions.DeepMaintenanceShootUp || args.Function == ContentKeyFunctions.ArcadeUp)
            {
                Shoot(new Vector2(0, -1));
                args.Handle();
                return;
            }

            if (args.Function == ContentKeyFunctions.DeepMaintenanceShootDown || args.Function == ContentKeyFunctions.ArcadeDown)
            {
                Shoot(new Vector2(0, 1));
                args.Handle();
                return;
            }

            if (args.Function == ContentKeyFunctions.DeepMaintenanceShootLeft || args.Function == ContentKeyFunctions.ArcadeLeft)
            {
                Shoot(new Vector2(-1, 0));
                args.Handle();
                return;
            }

            if (args.Function == ContentKeyFunctions.DeepMaintenanceShootRight || args.Function == ContentKeyFunctions.ArcadeRight)
            {
                Shoot(new Vector2(1, 0));
                args.Handle();
            }
        }

        protected override void KeyBindUp(GUIBoundKeyEventArgs args)
        {
            base.KeyBindUp(args);

            EnsureInputFocus();

            if (args.Function == ContentKeyFunctions.DeepMaintenanceMoveUp || args.Function == ContentKeyFunctions.DeepMaintenanceMoveDown ||
                args.Function == EngineKeyFunctions.MoveUp || args.Function == EngineKeyFunctions.MoveDown)
            {
                _moveInput.Y = 0;
                args.Handle();
                return;
            }

            if (args.Function != ContentKeyFunctions.DeepMaintenanceMoveLeft &&
                args.Function != ContentKeyFunctions.DeepMaintenanceMoveRight &&
                args.Function != EngineKeyFunctions.MoveLeft && args.Function != EngineKeyFunctions.MoveRight)
                return;

            _moveInput.X = 0;
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
            TickEnemies(dt);
            TickProjectiles(_playerProjectiles, true, dt);
            TickProjectiles(_enemyProjectiles, false, dt);
            DamageOnContact();
            CheckRoomClear();

            InvalidateMeasure();
            StateChanged?.Invoke();
        }

        private void MovePlayer(float dt)
        {
            var direction = NormalizeSafe(_moveInput);
            if (direction == Vector2.Zero)
                return;

            var target = _playerPos + direction * _playerProto.MoveSpeed * dt;
            _playerPos = ResolveCircleTileCollision(target, _playerProto.Radius, CurrentRoom.Tiles);
            TryRoomTransition();
        }

        private void TickEnemies(float dt)
        {
            foreach (var enemy in CurrentRoom.Enemies.Where(enemy => enemy.Hp > 0))
            {
                if (enemy.ShootCooldownTicks > 0)
                    enemy.ShootCooldownTicks--;

                var toPlayer = _playerPos - enemy.Position;
                var dir = NormalizeSafe(toPlayer);

                if (enemy.Prototype.Shooter)
                {
                    if (enemy.ShootCooldownTicks <= 0)
                    {
                        _enemyProjectiles.Add(new ProjectileData(enemy.Position, dir * enemy.Prototype.ProjectileSpeed, ProjectileRadius, 1));
                        enemy.ShootCooldownTicks = enemy.Prototype.ShootCooldownTicks;
                    }

                    var strafe = new Vector2(-dir.Y, dir.X) * 0.4f;
                    var target = enemy.Position + strafe * enemy.Prototype.MoveSpeed * dt;
                    enemy.Position = ResolveCircleTileCollision(target, enemy.Prototype.Radius, CurrentRoom.Tiles);
                    continue;
                }

                var chaseTarget = enemy.Position + dir * enemy.Prototype.MoveSpeed * dt;
                enemy.Position = ResolveCircleTileCollision(chaseTarget, enemy.Prototype.Radius, CurrentRoom.Tiles);
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
                    var hit = false;
                    foreach (var enemy in CurrentRoom.Enemies)
                    {
                        if (enemy.Hp <= 0)
                            continue;

                        if (!(Vector2.Distance(projectile.Position, enemy.Position) <=
                              projectile.Radius + enemy.Prototype.Radius))
                            continue;

                        enemy.Hp -= projectile.Damage;
                        hit = true;
                        break;
                    }

                    if (hit)
                    {
                        projectiles.RemoveAt(i);
                    }
                }
                else if (Vector2.Distance(projectile.Position, _playerPos) <= projectile.Radius + _playerProto.Radius)
                {
                    DamagePlayer();
                    projectiles.RemoveAt(i);
                }
            }
        }

        private void DamageOnContact()
        {
            if (_invulnerabilityTicks > 0)
                return;

            foreach (var enemy in CurrentRoom.Enemies)
            {
                if (enemy.Hp <= 0)
                    continue;

                var hitDist = _playerProto.Radius + enemy.Prototype.Radius;

                if (!(Vector2.Distance(_playerPos, enemy.Position) <= hitDist))
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
            _invulnerabilityTicks = 10;
            if (PlayerHp <= 0)
                _gameOver = true;
        }

        private void CheckRoomClear()
        {
            var room = CurrentRoom;
            if (room.Cleared)
                return;

            if (room.Enemies.Any(e => e.Hp > 0))
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

            const float margin = 0.25f;
            switch (_playerPos.X)
            {
                case < margin when room.Neighbors.TryGetValue(new Vector2i(-1, 0), out var left):
                    EnterRoom(left, false);
                    _playerPos = _playerPos with { X = GridWidth - 1.0f };
                    break;
                case > GridWidth - margin when room.Neighbors.TryGetValue(new Vector2i(1, 0), out var right):
                    EnterRoom(right, false);
                    _playerPos = _playerPos with { X = 1.0f };
                    break;
                default:
                {
                    switch (_playerPos.Y)
                    {
                        case < margin when room.Neighbors.TryGetValue(new Vector2i(0, -1), out var up):
                            EnterRoom(up, false);
                            _playerPos = _playerPos with { Y = GridHeight - 1.0f };
                            break;
                        case > GridHeight - margin when room.Neighbors.TryGetValue(new Vector2i(0, 1), out var down):
                            EnterRoom(down, false);
                            _playerPos = _playerPos with { Y = 1.0f };
                            break;
                    }

                    break;
                }
            }
        }

        private void EnterRoom(int roomIndex, bool center)
        {
            RoomIndex = roomIndex;
            _playerProjectiles.Clear();
            _enemyProjectiles.Clear();
            if (center)
                _playerPos = new Vector2(GridWidth * 0.5f, GridHeight * 0.5f);
        }

        private void GenerateMap()
        {
            const int roomCount = 5;
            var positions = new List<Vector2i> { Vector2i.Zero };
            var indicesByPos = new Dictionary<Vector2i, int> { [Vector2i.Zero] = 0 };

            for (var i = 1; i < roomCount; i++)
            {
                var basePos = positions[_random.Next(positions.Count)];
                var newPos = basePos + CardinalDirections()[_random.Next(4)];
                var attempts = 0;
                while (indicesByPos.ContainsKey(newPos))
                {
                    if (++attempts > 100)
                        break;

                    basePos = positions[_random.Next(positions.Count)];
                    newPos = basePos + CardinalDirections()[_random.Next(4)];
                }

                positions.Add(newPos);
                indicesByPos[newPos] = i;
            }

            const int bossIndex = roomCount - 1;
            const int treasureIndex = roomCount - 2;

            for (var i = 0; i < roomCount; i++)
            {
                var type = i switch
                {
                    0 => RoomType.Start,
                    treasureIndex => RoomType.Treasure,
                    bossIndex => RoomType.Boss,
                    _ => RoomType.Normal,
                };

                var room = new RoomData(type, positions[i], BuildTileMap(type));
                SpawnEnemies(room);
                _rooms.Add(room);
            }

            foreach (var t in _rooms)
            {
                foreach (var dir in CardinalDirections())
                {
                    if (indicesByPos.TryGetValue(t.MapPosition + dir, out var nIdx))
                        t.Neighbors[dir] = nIdx;
                }
            }
        }

        private TileType[,] BuildTileMap(RoomType roomType)
        {
            var map = new TileType[GridWidth, GridHeight];

            for (var y = 0; y < GridHeight; y++)
            {
                for (var x = 0; x < GridWidth; x++)
                {
                    map[x, y] = x == 0 || y == 0 || x == GridWidth - 1 || y == GridHeight - 1
                        ? TileType.Wall
                        : TileType.Floor;
                }
            }

            if (roomType == RoomType.Start)
                return map;

            var obstacleCount = _random.Next(2, 5);
            for (var i = 0; i < obstacleCount; i++)
            {
                var ox = _random.Next(2, GridWidth - 2);
                var oy = _random.Next(2, GridHeight - 2);
                map[ox, oy] = TileType.Obstacle;
            }

            return map;
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
                    var tile = CurrentRoom.Tiles[x, y];
                    var tileProto = tile switch
                    {
                        TileType.Floor => _floorProto,
                        TileType.Wall => _wallProto,
                        TileType.Obstacle => _obstacleProto,
                        _ => _floorProto,
                    };

                    var box = UIBox2.FromDimensions(
                        mapOffset + new Vector2(x * tilePixel, y * tilePixel),
                        new Vector2(tilePixel, tilePixel) - Vector2.One);

                    if (GetSprite(tileProto.SpritePath, tileProto.SpriteState) is { } tileTexture)
                        handle.DrawTextureRect(tileTexture, box);
                    else
                        handle.DrawRect(box, tileProto.Color);
                }
            }

            foreach (var enemy in CurrentRoom.Enemies.Where(enemy => enemy.Hp > 0))
            {
                DrawEntity(handle, enemy.Position, enemy.Prototype.Radius, enemy.Prototype.Color, enemy.Prototype.SpritePath, enemy.Prototype.SpriteState, tilePixel, mapOffset);
            }

            foreach (var bullet in _playerProjectiles)
            {
                DrawCircle(handle, bullet.Position, bullet.Radius, Color.Cyan, tilePixel, mapOffset);
            }

            foreach (var bullet in _enemyProjectiles)
            {
                DrawCircle(handle, bullet.Position, bullet.Radius, Color.Yellow, tilePixel, mapOffset);
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

                    var tx = Math.Clamp((int) MathF.Floor(check.X), 0, GridWidth - 1);
                    var ty = Math.Clamp((int) MathF.Floor(check.Y), 0, GridHeight - 1);
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
