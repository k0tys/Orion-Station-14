using System.Linq;
using System.Numerics;
using Content.Shared._Orion.CartridgeLoader.Cartridges.DeepMaintenance;

namespace Content.Client._Orion.CartridgeLoader.Cartridges.DeepMaintenance;

public sealed partial class DeepMaintenanceUiFragment
{
    private sealed partial class DeepMaintenanceGameControl
    {
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

            if (roomType is RoomType.Start or RoomType.Boss or RoomType.Treasure or RoomType.Shop)
                return tiles;

            var clusterCount = _random.Next(1, 4);
            for (var clusterIndex = 0; clusterIndex < clusterCount; clusterIndex++)
            {
                var x = _random.Next(2, GridWidth - 2);
                var y = _random.Next(2, GridHeight - 2);
                var clusterSize = _random.Next(3, 7);

                for (var step = 0; step < clusterSize; step++)
                {
                    if (x > 1 && x < GridWidth - 1 && y > 1 && y < GridHeight - 1 &&
                        !IsInsideDoorSpawnExclusion(new Vector2(x + 0.5f, y + 0.5f)) &&
                        (Math.Abs(x - GridWidth / 2) > 1 || Math.Abs(y - GridHeight / 2) > 1))
                    {
                        tiles[x, y] = TileType.Obstacle;
                    }

                    var direction = CardinalDirections()[_random.Next(4)];
                    x = Math.Clamp(x + direction.X, 1, GridWidth - 2);
                    y = Math.Clamp(y + direction.Y, 1, GridHeight - 2);
                }
            }

            var mushroomCount = _random.Next(1, 4);
            for (var i = 0; i < mushroomCount; i++)
            {
                var x = _random.Next(2, GridWidth - 2);
                var y = _random.Next(2, GridHeight - 2);

                if (tiles[x, y] != TileType.Floor)
                    continue;

                if (Math.Abs(x - GridWidth / 2) <= 1 && Math.Abs(y - GridHeight / 2) <= 1)
                    continue;

                tiles[x, y] = TileType.Mushroom;
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

        private void TryAddSecretRoom(Dictionary<Vector2i, int> indexByPos, List<Vector2i> occupiedPositions)
        {
            var candidates = new List<(Vector2i Position, List<Vector2i> Connections)>();
            foreach (var anchor in occupiedPositions)
            {
                foreach (var direction in CardinalDirections())
                {
                    var candidate = anchor + direction;
                    if (indexByPos.ContainsKey(candidate))
                        continue;

                    var connections = new List<Vector2i>();
                    foreach (var check in CardinalDirections())
                    {
                        var neighborPos = candidate + check;
                        if (!indexByPos.TryGetValue(neighborPos, out var neighborIndex))
                            continue;

                        var neighbor = _rooms[neighborIndex];
                        if (neighbor.Type is RoomType.Boss or RoomType.Treasure or RoomType.Start)
                            continue;

                        connections.Add(check);
                    }

                    if (connections.Count == 0)
                        continue;

                    candidates.Add((candidate, connections));
                }
            }

            if (candidates.Count == 0)
                return;

            var selected = candidates[_random.Next(candidates.Count)];
            var room = new RoomData(RoomType.Secret, selected.Position, BuildTileMap(RoomType.Secret))
            {
                IsSecret = true,
                Cleared = false,
                DoorTargetOpen = false,
                DoorVisualOpen = false,
            };

            var roomIndex = _rooms.Count;
            _rooms.Add(room);
            indexByPos[selected.Position] = roomIndex;
            occupiedPositions.Add(selected.Position);

            foreach (var direction in selected.Connections)
            {
                var neighborPos = selected.Position + direction;
                if (!indexByPos.TryGetValue(neighborPos, out var neighborIndex))
                    continue;

                var reverse = new Vector2i(-direction.X, -direction.Y);
                room.Neighbors[direction] = neighborIndex;
                _rooms[neighborIndex].Neighbors[reverse] = roomIndex;
            }
        }

        private void RevealSecretDoorways(Vector2 center)
        {
            foreach (var direction in CardinalDirections())
            {
                if (!CurrentRoom.Neighbors.TryGetValue(direction, out var neighborIndex))
                    continue;

                var neighbor = _rooms[neighborIndex];
                if (!neighbor.IsSecret || neighbor.Cleared)
                    continue;

                if (!TryGetSecretDoorwayTile(direction, out var doorwayTile))
                    continue;

                var doorwayCenter = new Vector2(doorwayTile.X + 0.5f, doorwayTile.Y + 0.5f);
                if (Vector2.Distance(doorwayCenter, center) > _bombPickupProto.SecretRevealBombRadius)
                    continue;

                CurrentRoom.Tiles[doorwayTile.X, doorwayTile.Y] = TileType.Door;
                var reverse = new Vector2i(-direction.X, -direction.Y);
                if (TryGetSecretDoorwayTile(reverse, out var reverseDoorway))
                    neighbor.Tiles[reverseDoorway.X, reverseDoorway.Y] = TileType.Door;

                neighbor.Cleared = true;
                neighbor.DoorTargetOpen = true;
                neighbor.DoorVisualOpen = true;
                neighbor.IsSecret = false;
                SetDoorState(CurrentRoom, true);
            }
        }

        private static bool TryGetSecretDoorwayTile(Vector2i direction, out Vector2i tile)
        {
            if (direction == new Vector2i(-1, 0))
            {
                tile = new Vector2i(0, GridHeight / 2);
                return true;
            }

            if (direction == new Vector2i(1, 0))
            {
                tile = new Vector2i(GridWidth - 1, GridHeight / 2);
                return true;
            }

            if (direction == new Vector2i(0, -1))
            {
                tile = new Vector2i(GridWidth / 2, 0);
                return true;
            }

            if (direction == new Vector2i(0, 1))
            {
                tile = new Vector2i(GridWidth / 2, GridHeight - 1);
                return true;
            }

            tile = default;
            return false;
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
                case RoomType.Shop:
                    return;
                case RoomType.Boss:
                {
                    var bossPool = _activeFloorConfig?.BossPool ?? new List<DeepMaintenanceWeightedEntityEntry>();
                    var bossPrototype = ChooseEntityFromPool(bossPool, _bossProto.ID);
                    room.Enemies.Add(CreateEnemyData(bossPrototype, GetRoomCenter(), 0));
                    return;
                }
            }

            var count = _random.Next(2 + _currentFloor, Math.Min(7, 5 + _currentFloor));
            for (var i = 0; i < count; i++)
            {
                if (!TryFindEnemySpawnPosition(room, out var pos))
                    continue;

                var enemyPool = _activeFloorConfig?.EnemyPool ?? new List<DeepMaintenanceWeightedEntityEntry>();
                var proto = ChooseEntityFromPool(enemyPool, _random.NextDouble() < 0.5 ? _chaserProto.ID : _shooterProto.ID);
                room.Enemies.Add(CreateEnemyData(proto, pos, 0));
            }
        }

        private void PopulateShop(RoomData room)
        {
            room.ShopSlots.Clear();
            var slots = new[]
            {
                new Vector2(GridWidth * 0.5f - 2.1f, GridHeight * 0.5f),
                new Vector2(GridWidth * 0.5f, GridHeight * 0.5f),
                new Vector2(GridWidth * 0.5f + 2.1f, GridHeight * 0.5f),
            };

            var availableConsumables = new List<ShopItemType>
            {
                ShopItemType.Bomb,
                ShopItemType.Heart,
            };

            for (var i = 0; i < Math.Min(ShopSlotCount, slots.Length); i++)
            {
                room.ShopSlots.Add(RollShopSlot(slots[i], availableConsumables));
            }
        }

        private ShopSlotData RollShopSlot(Vector2 position, List<ShopItemType> availableConsumables)
        {
            if (availableConsumables.Count == 0)
            {
                var exhaustedRelicId = RollTreasureRelicId();
                if (!string.IsNullOrWhiteSpace(exhaustedRelicId) && _prototype.TryIndex<DeepMaintenanceRelicPrototype>(exhaustedRelicId, out var exhaustedRelicPrototype))
                    return new ShopSlotData(ShopItemType.Relic, 1, CalculateRelicPrice(exhaustedRelicPrototype), position, exhaustedRelicPrototype.HudIconSpritePath, exhaustedRelicPrototype.HudIconSpriteState, 1f, exhaustedRelicId);

                return new ShopSlotData(ShopItemType.None, 0, 0, position, null, null, 1f);
            }

            var roll = _random.Next(100);

            if (roll >= 80)
            {
                var relicId = RollTreasureRelicId();
                if (!string.IsNullOrWhiteSpace(relicId) && _prototype.TryIndex<DeepMaintenanceRelicPrototype>(relicId, out var relicPrototype))
                {
                    return new ShopSlotData(ShopItemType.Relic, 1, CalculateRelicPrice(relicPrototype), position, relicPrototype.HudIconSpritePath, relicPrototype.HudIconSpriteState, 1f, relicId);
                }
            }

            ShopItemType chosen = roll switch
            {
                < 45 when availableConsumables.Contains(ShopItemType.Bomb) => ShopItemType.Bomb,
                < 80 when availableConsumables.Contains(ShopItemType.Heart) => ShopItemType.Heart,
                _ => availableConsumables[_random.Next(availableConsumables.Count)],
            };

            availableConsumables.Remove(chosen);
            switch (chosen)
            {
                case ShopItemType.Bomb:
                    return new ShopSlotData(ShopItemType.Bomb, 1, CalculatePickupPrice(_bombPickupProto), position, _bombPickupProto.SpritePath, _bombPickupProto.SpriteState, _bombPickupProto.SpriteScale);
                case ShopItemType.Heart:
                    return new ShopSlotData(ShopItemType.Heart, 1, CalculatePickupPrice(_heartPickupProto), position, _heartPickupProto.SpritePath, _heartPickupProto.SpriteState, _heartPickupProto.SpriteScale);
                default:
                    return new ShopSlotData(ShopItemType.Bomb, 1, CalculatePickupPrice(_bombPickupProto), position, _bombPickupProto.SpritePath, _bombPickupProto.SpriteState, _bombPickupProto.SpriteScale);
            }
        }

        private static bool IsInsideDoorSpawnExclusion(Vector2 position)
        {
            const float centerX = GridWidth * 0.5f;
            const float centerY = GridHeight * 0.5f;
            const float halfDoorWidth = 1f;

            var leftSafe = position.X is >= 1f and <= 1f + DoorInnerSafeZoneDepth &&
                           MathF.Abs(position.Y - centerY) <= halfDoorWidth;
            var rightSafe = position.X is <= GridWidth - 1f and >= GridWidth - 1f - DoorInnerSafeZoneDepth &&
                            MathF.Abs(position.Y - centerY) <= halfDoorWidth;
            var topSafe = position.Y is >= 1f and <= 1f + DoorInnerSafeZoneDepth &&
                          MathF.Abs(position.X - centerX) <= halfDoorWidth;
            var bottomSafe = position.Y is <= GridHeight - 1f and >= GridHeight - 1f - DoorInnerSafeZoneDepth &&
                             MathF.Abs(position.X - centerX) <= halfDoorWidth;

            return leftSafe || rightSafe || topSafe || bottomSafe;
        }

        private bool TryFindEnemySpawnPosition(RoomData room, out Vector2 position)
        {
            for (var attempt = 0; attempt < 30; attempt++)
            {
                position = new Vector2(_random.NextSingle() * (GridWidth - 4) + 2f, _random.NextSingle() * (GridHeight - 4) + 2f);
                if (TouchesBlockedTile(position, room) || IsInsideDoorSpawnExclusion(position))
                    continue;

                return true;
            }

            position = default;
            return false;
        }

        #endregion
    }
}
