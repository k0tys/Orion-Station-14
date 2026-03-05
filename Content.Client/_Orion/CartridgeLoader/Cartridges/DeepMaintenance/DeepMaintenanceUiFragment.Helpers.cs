using System.Linq;
using System.Numerics;
using Content.Shared._Orion.CartridgeLoader.Cartridges.DeepMaintenance;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Audio;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Player;
using Robust.Shared.Utility;

namespace Content.Client._Orion.CartridgeLoader.Cartridges.DeepMaintenance;

public sealed partial class DeepMaintenanceUiFragment
{
    private sealed partial class DeepMaintenanceGameControl
    {
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
            _mushroomProto = _prototype.Index<DeepMaintenanceTilePrototype>(TileMushroomPrototypeId);

            _floorConfigs.Clear();
            foreach (var floorConfig in _prototype.EnumeratePrototypes<DeepMaintenanceFloorPrototype>())
            {
                if (floorConfig.Abstract)
                    continue;

                _floorConfigs[floorConfig.FloorNumber] = floorConfig;
            }

            _coinPickupProto = _prototype.Index<DeepMaintenancePickupPrototype>(PickupCoinPrototypeId);
            _bombPickupProto = _prototype.Index<DeepMaintenancePickupPrototype>(PickupBombPrototypeId);
            _heartPickupProto = _prototype.Index<DeepMaintenancePickupPrototype>(PickupHeartPrototypeId);
            _hasTreasurePrototype = _prototype.TryIndex<DeepMaintenanceTreasurePrototype>(TreasurePrototypeId, out var treasurePrototype);
            if (_hasTreasurePrototype)
                _treasurePrototype = treasurePrototype!;

            WarmupSprite(_coinPickupProto.SpritePath, _coinPickupProto.SpriteState);
            WarmupSprite(_bombPickupProto.SpritePath, _bombPickupProto.SpriteState);
            WarmupSprite(_bombPickupProto.SpritePath, BombPrimedState);
            WarmupSprite(_heartPickupProto.SpritePath, _heartPickupProto.SpriteState);

            WarmupSprite(_playerProto.SpritePath, _playerProto.SpriteState);
            WarmupSprite(_playerProto.SpritePath, _playerProto.BodySpriteState);
            WarmupSprite(_playerProto.SpritePath, _playerProto.HeadSpriteState);
            WarmupSprite(_playerProto.SpritePath, _playerProto.ShootSpriteState);
            WarmupSprite(_chaserProto.SpritePath, _chaserProto.SpriteState);
            WarmupSprite(_shooterProto.SpritePath, _shooterProto.SpriteState);
            WarmupSprite(_bossProto.SpritePath, _bossProto.SpriteState);

            ApplyFloorTheme(_currentFloor);

            WarmupSprite(HeartSpritePath, HeartFullState);
            WarmupSprite(HeartSpritePath, HeartHalfState);
            WarmupSprite(HeartSpritePath, HeartEmptyState);

            if (_hasTreasurePrototype)
            {
                WarmupSprite(_treasurePrototype.ClosedCrateSpritePath, _treasurePrototype.ClosedCrateSpriteState);
                WarmupSprite(_treasurePrototype.ClosedCrateSpritePath, _treasurePrototype.OpenCrateSpriteState);
                WarmupSprite(_treasurePrototype.ClosedCrateSpritePath, TreasureOpeningState);
            }

            foreach (var relic in _prototype.EnumeratePrototypes<DeepMaintenanceRelicPrototype>())
            {
                WarmupSprite(relic.HudIconSpritePath, relic.HudIconSpriteState);
                WarmupSprite(relic.VisualEffectSpritePath, relic.VisualEffectSpriteState);
                WarmupSprite(relic.BodyAttachedSpritePath, relic.BodyAttachedSpriteState);
                WarmupSprite(relic.HeadAttachedSpritePath, relic.HeadAttachedSpriteState);
                WarmupSprite(relic.MeleeArcSpritePath, relic.MeleeArcSpriteState);
            }

            NormalizeConfiguredHitboxes();
        }

        private void ApplyFloorTheme(int floor)
        {
            if (!_floorConfigs.TryGetValue(floor, out var floorConfig))
            {
                floorConfig = _floorConfigs
                    .OrderBy(pair => Math.Abs(pair.Key - floor))
                    .Select(pair => pair.Value)
                    .FirstOrDefault();
            }

            if (floorConfig == null)
            {
                _doorPrototypes[RoomType.Normal] = _prototype.Index<DeepMaintenanceDoorPrototype>("DoorNormal");
                _doorPrototypes[RoomType.Secret] = _doorPrototypes[RoomType.Normal];
                _doorPrototypes[RoomType.Treasure] = _prototype.Index<DeepMaintenanceDoorPrototype>("DoorTreasure");
                _doorPrototypes[RoomType.Boss] = _prototype.Index<DeepMaintenanceDoorPrototype>("DoorBoss");
                _doorPrototypes[RoomType.Start] = _doorPrototypes[RoomType.Normal];
                return;
            }

            _activeFloorConfig = floorConfig;
            _floorProto = _prototype.Index(floorConfig.FloorTile);
            _wallProto = _prototype.Index(floorConfig.WallTile);
            _obstacleProto = _prototype.Index(floorConfig.ObstacleTile);
            _mushroomProto = _prototype.Index(floorConfig.MushroomTile);

            _doorPrototypes[RoomType.Normal] = _prototype.Index(floorConfig.DoorNormal);
            _doorPrototypes[RoomType.Secret] = _doorPrototypes[RoomType.Normal];
            _doorPrototypes[RoomType.Treasure] = _prototype.Index(floorConfig.DoorTreasure);
            _doorPrototypes[RoomType.Boss] = _prototype.Index(floorConfig.DoorBoss);
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
            WarmupSprite(_mushroomProto.SpritePath, _mushroomProto.SpriteState);

            foreach (var door in _doorPrototypes.Values.Distinct())
            {
                WarmupSprite(door.SpritePath, door.ClosedState);
                WarmupSprite(door.SpritePath, door.OpenState);
                WarmupSprite(door.SpritePath, door.OpeningState);
                WarmupSprite(door.SpritePath, door.ClosingState);
            }
        }

        private DeepMaintenanceEntityPrototype ChooseEntityFromPool(List<DeepMaintenanceWeightedEntityEntry> pool, string fallbackPrototype)
        {
            if (pool.Count == 0)
                return _prototype.Index<DeepMaintenanceEntityPrototype>(fallbackPrototype);

            var totalWeight = 0f;
            foreach (var entry in pool)
            {
                if (entry.Weight <= 0f)
                    continue;

                totalWeight += entry.Weight;
            }

            if (totalWeight <= 0.001f)
                return _prototype.Index<DeepMaintenanceEntityPrototype>(fallbackPrototype);

            var roll = _random.NextSingle() * totalWeight;
            foreach (var entry in pool)
            {
                if (entry.Weight <= 0f)
                    continue;

                roll -= entry.Weight;
                if (roll > 0f)
                    continue;

                if (_prototype.TryIndex(entry.Entity, out var result))
                    return result;
            }

            return _prototype.Index<DeepMaintenanceEntityPrototype>(fallbackPrototype);
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

        private void NormalizeConfiguredHitboxes()
        {
            ClampEntityHitbox(_playerProto);
            ClampEntityHitbox(_chaserProto);
            ClampEntityHitbox(_shooterProto);
            ClampEntityHitbox(_bossProto);

            ClampProjectileHitbox(_playerProto.ProjectilePrototype);
            ClampProjectileHitbox(_chaserProto.ProjectilePrototype);
            ClampProjectileHitbox(_shooterProto.ProjectilePrototype);
            ClampProjectileHitbox(_bossProto.ProjectilePrototype);
        }

        private static void ClampEntityHitbox(DeepMaintenanceEntityPrototype prototype)
        {
            prototype.HitboxWidth = Math.Clamp(prototype.HitboxWidth, 0.02f, 3.2f);
            prototype.HitboxHeight = Math.Clamp(prototype.HitboxHeight, 0.02f, 3.2f);
        }

        private void ClampProjectileHitbox(string projectilePrototypeId)
        {
            var projectilePrototype = _prototype.Index<DeepMaintenanceProjectilePrototype>(projectilePrototypeId);
            projectilePrototype.HitboxWidth = Math.Clamp(projectilePrototype.HitboxWidth, 0.01f, 2.4f);
            projectilePrototype.HitboxHeight = Math.Clamp(projectilePrototype.HitboxHeight, 0.01f, 2.4f);
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

            _lastPlayerShotDirection = normalized;

            if (HasClaymoreRelic())
            {
                _claymoreCharging = true;
                _claymoreChargeDirection = normalized;
                return;
            }

            _playerShootFacing = FacingFromVector(normalized, _playerShootFacing);
            _playerShootFacingResetTimer = FacingResetDelaySeconds;
            _playerShootAnimationTimer = ShootAnimationDuration;

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
            var shotEye = _nextShotLeftEye ? EyeSource.Left : EyeSource.Right;
            _nextShotLeftEye = !_nextShotLeftEye;
            var speed = projectilePrototype.Speed * GetProjectileSpeedMultiplier(shotEye);
            var damage = (projectilePrototype.Damage + GetDamageFlatBonus()) * GetEyeDamageMultiplier(shotEye, true);
            var lifetime = GetProjectileLifetime(projectilePrototype, shotEye);
            var heightScale = GetProjectileHeightScale();
            var tintPalette = GetProjectileTintPalette();
            var created = new List<ProjectileData>();

            if (_activeRelics.Any(relic => relic.TripleShotAlternating))
            {
                foreach (var spreadDegrees in new[] { -12f, 0f, 12f })
                {
                    var shotDirection = NormalizeSafe(Rotate(direction, MathF.PI * spreadDegrees / 180f));
                    created.Add(SpawnProjectile(_playerProjectiles, _playerPos, shotDirection * speed, projectilePrototype, 1f, damage, PickTintFromPalette(tintPalette), lifetime, heightScale));
                }
            }
            else
            {
                created.Add(SpawnProjectile(_playerProjectiles, _playerPos, direction * speed, projectilePrototype, 1f, damage, PickTintFromPalette(tintPalette), lifetime, heightScale));
            }

            TrySpawnExtraRandomProjectiles(created, projectilePrototype, lifetime, heightScale);
        }

        private void TrySpawnExtraRandomProjectiles(List<ProjectileData> created, DeepMaintenanceProjectilePrototype projectilePrototype, float lifetime, float heightScale)
        {
            if (created.Count == 0)
                return;

            var chance = _activeRelics.Select(relic => relic.ExtraShotChance).DefaultIfEmpty(0f).Max();
            var maxCount = _activeRelics.Select(relic => relic.ExtraShotMaxCount).DefaultIfEmpty(0).Max();
            if (chance <= 0f || maxCount <= 0)
                return;

            var template = created[0];
            for (var i = 0; i < maxCount; i++)
            {
                if (_random.NextDouble() > chance)
                    continue;

                var angle = _random.NextSingle() * MathF.PI * 2f;
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var velocity = direction * template.Velocity.Length();
                SpawnProjectile(_playerProjectiles, _playerPos, velocity, projectilePrototype, 1f, template.Damage, template.Tint, lifetime, heightScale);
            }
        }

        private bool HasClaymoreRelic()
        {
            return _activeRelics.Any(relic => relic.ClaymoreEnabled);
        }

        private void ReleaseClaymoreCharge()
        {
            if (!_claymoreCharging)
                return;

            _claymoreCharging = false;
            var chargeRelic = _activeRelics.FirstOrDefault(relic => relic.ClaymoreEnabled);
            if (chargeRelic == null || _playerShootCooldown > 0f)
            {
                _claymoreChargeTimer = 0f;
                return;
            }

            var chargeDuration = MathF.Max(0.05f, chargeRelic.ClaymoreChargeDuration);
            var charged = _claymoreChargeTimer >= chargeDuration;
            _claymoreChargeTimer = 0f;
            _meleeSwingFacing = FacingFromVector(_claymoreChargeDirection, _meleeSwingFacing);
            _meleeSwingTimer = MeleeSwingDuration;

            var baseDamage = _prototype.Index<DeepMaintenanceProjectilePrototype>(_playerProto.ProjectilePrototype).Damage + GetDamageFlatBonus();
            var damage = charged
                ? baseDamage * chargeRelic.ClaymoreChargedDamageMultiplier
                : baseDamage * chargeRelic.ClaymoreMeleeDamageMultiplier;

            if (charged)
            {
                foreach (var enemy in CurrentRoom.Enemies)
                {
                    if (enemy.Hp <= 0 || Vector2.Distance(enemy.Position, _playerPos) > chargeRelic.ClaymoreSwingRadius + enemy.Prototype.Radius)
                        continue;

                    enemy.Hp -= (int) MathF.Ceiling(ApplyNonTearDamageModifiers(damage));
                    enemy.DamageFlash = EntityDamageFlashDuration;
                }

                ReflectEnemyProjectiles(_playerPos, chargeRelic.ClaymoreReflectRadius);
            }
            else
            {
                var strikeCenter = _playerPos + _claymoreChargeDirection * chargeRelic.ClaymoreSwingRadius;
                foreach (var enemy in CurrentRoom.Enemies)
                {
                    if (enemy.Hp <= 0 || Vector2.Distance(enemy.Position, strikeCenter) > enemy.Prototype.Radius + 0.65f)
                        continue;

                    enemy.Hp -= (int) MathF.Ceiling(ApplyNonTearDamageModifiers(damage));
                    enemy.DamageFlash = EntityDamageFlashDuration;
                }
            }

            if (chargeRelic.ClaymoreProjectileOnFullHealth && PlayerHp >= MaxPlayerHp)
            {
                var projectilePrototype = _prototype.Index<DeepMaintenanceProjectilePrototype>(_playerProto.ProjectilePrototype);
                var speed = projectilePrototype.Speed * GetProjectileSpeedMultiplier(EyeSource.None);
                SpawnProjectile(_playerProjectiles, _playerPos, _claymoreChargeDirection * speed, projectilePrototype, 1f, baseDamage + chargeRelic.ClaymoreProjectileBonusDamage, Color.White, GetProjectileLifetime(projectilePrototype, EyeSource.None), GetProjectileHeightScale());
            }

            _playerShootCooldown = GetShootCooldown(_playerProto) * GetShootCooldownMultiplier();
            PlaySfx(SfxPlayerShoot, -5f);
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

                enemy.Hp -= (int) MathF.Ceiling(ApplyNonTearDamageModifiers(meleeDamage));
                enemy.DamageFlash = EntityDamageFlashDuration;
                if (enemy.Hp <= 0)
                    PlaySfx(SfxEnemyDeath, -7f);
            }
        }

        private static ProjectileData SpawnProjectile(List<ProjectileData> container, Vector2 position, Vector2 velocity, DeepMaintenanceProjectilePrototype projectilePrototype, float radiusScale, float damage, Color tint, float? lifetimeOverride = null, float heightScale = 1f)
        {
            var projectileRadius = MathF.Max(0.01f, projectilePrototype.HitboxWidth * 0.5f * radiusScale);
            var projectile = new ProjectileData(
                position,
                position,
                velocity,
                projectileRadius,
                projectilePrototype.HitboxShape,
                MathF.Max(0.01f, projectilePrototype.HitboxWidth * radiusScale),
                MathF.Max(0.01f, projectilePrototype.HitboxHeight * radiusScale),
                new Vector2(projectilePrototype.HitboxOffsetX, projectilePrototype.HitboxOffsetY),
                damage,
                lifetimeOverride ?? projectilePrototype.Lifetime,
                projectilePrototype.SpritePath,
                projectilePrototype.SpriteState,
                projectilePrototype.SpriteScale,
                projectilePrototype,
                tint,
                heightScale);
            container.Add(projectile);
            return projectile;
        }

        private float GetProjectileSpeedMultiplier(EyeSource eye)
        {
            var multiplier = 1f;
            var additive = 0f;
            foreach (var relic in _activeRelics)
            {
                multiplier *= relic.ProjectileSpeedMultiplier;
                additive += relic.PassiveProjectileSpeedBonus;

                if (eye == EyeSource.Left)
                    multiplier *= MathF.Max(0.1f, relic.LeftEyeProjectileSpeedMultiplier);

                if (eye == EyeSource.Right)
                    additive += relic.RightEyeProjectileSpeedBonus;
            }

            return MathF.Max(0.05f, multiplier + additive);
        }

        private float GetDamageFlatBonus()
        {
            return _activeRelics.Sum(relic => relic.DamageFlatBonus + relic.PassiveDamageBonus) + _roomKillDamageBonus + _floorDamageBonus;
        }

        private float GetEyeDamageMultiplier(EyeSource eye, bool projectileAttack)
        {
            var multiplier = 1f;
            foreach (var relic in _activeRelics)
            {
                if (eye == EyeSource.Right)
                    multiplier *= MathF.Max(0.01f, relic.RightEyeDamageMultiplier);
                else if (eye == EyeSource.None && _random.NextDouble() < relic.RightEyeFallbackChance)
                    multiplier *= MathF.Max(0.01f, relic.RightEyeDamageMultiplier);

                if (!projectileAttack && _random.NextDouble() < relic.NonTearDamageProcChance)
                    multiplier *= MathF.Max(0.01f, relic.RightEyeDamageMultiplier);
            }

            return multiplier;
        }

        private float ApplyNonTearDamageModifiers(float baseDamage)
        {
            return baseDamage * GetEyeDamageMultiplier(EyeSource.None, false);
        }

        private float GetProjectileLifetime(DeepMaintenanceProjectilePrototype projectile, EyeSource eye)
        {
            var lifetime = projectile.Lifetime;
            var multiplier = 1f;
            foreach (var relic in _activeRelics)
            {
                lifetime += relic.RangeFlatBonus;
                multiplier *= MathF.Max(0.1f, relic.RangeMultiplier);
                if (eye == EyeSource.Right)
                    lifetime += relic.RightEyeRangeFlatBonus;
            }

            return MathF.Max(0.1f, lifetime * multiplier);
        }

        private float GetProjectileHeightScale()
        {
            var scale = 1f;
            foreach (var relic in _activeRelics)
            {
                scale += relic.TearHeightBonus;
            }

            return MathF.Max(0.1f, scale);
        }

        private float GetShootCooldownMultiplier()
        {
            var multiplier = 1f;
            var fireRateBonus = _electroRakRoomFireRateBonus + _rhythmicKnifeBonus;
            foreach (var relic in _activeRelics)
            {
                multiplier *= relic.ShootCooldownMultiplier;
                fireRateBonus += relic.PassiveFireRateBonus;
                if (HasClaymoreRelic())
                    fireRateBonus += relic.LeftEyeFallbackFireRateBonus;
            }

            return MathF.Max(0.05f, multiplier / MathF.Max(0.1f, 1f + fireRateBonus));
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

        private void RevealConnectedRooms(int roomIndex)
        {
            _knownRooms.Add(roomIndex);
            foreach (var neighbor in _rooms[roomIndex].Neighbors.Values)
            {
                _knownRooms.Add(neighbor);
            }
        }

        private int CalculatePickupPrice(DeepMaintenancePickupPrototype pickup)
        {
            return Math.Max(0, pickup.BasePrice);
        }

        private int CalculateRelicPrice(DeepMaintenanceRelicPrototype relic)
        {
            return Math.Max(0, relic.BasePrice);
        }

        private int ApplyPriceModifiers(int basePrice)
        {
            var price = MathF.Max(0f, basePrice) * GetShopPriceMultiplier();
            return Math.Max(0, (int) MathF.Round(price, MidpointRounding.AwayFromZero));
        }

        private float GetShopPriceMultiplier()
        {
            var multiplier = 1f;
            foreach (var relic in _activeRelics)
            {
                multiplier *= MathF.Max(0.01f, relic.ShopPriceMultiplier);
            }

            return Math.Clamp(multiplier, 0.1f, 10f);
        }

        private void TriggerEmote()
        {
            _emoteTimer = EmoteAnimationDuration;
            PlaySfx(SfxPlayerEmote, -6f);
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
            _knownRooms.Clear();
            _roomKillDamageBonus = 0f;
            _playerProjectiles.Clear();
            _enemyProjectiles.Clear();
            RoomIndex = 0;
            ApplyFloorTheme(_currentFloor);
            GenerateMap();
            EnterRoom(0, true);
            RebuildFamiliarsFromRelics();
            StateChanged?.Invoke();
        }

        private FacingDirection FacingFromDoorTile(int tileX, int tileY)
        {
            switch (tileX)
            {
                case 0:
                    return FacingDirection.Left;
                case GridWidth - 1:
                    return FacingDirection.Right;
            }

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
                !resource.RSI.TryGetState(new RSI.StateId(spriteState), out var state) || !TryGetDirectionalFrame(state, direction, out texture) &&
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
            return TryGetDirectionalFrame(state, direction, 0f, out texture);
        }

        private static bool TryGetDirectionalFrame(RSI.State state, RsiDirection direction, float progress, out Texture texture)
        {
            try
            {
                var frames = state.GetFrames(direction);
                if (frames.Length > 0)
                {
                    var index = ResolveAnimationFrameIndex(frames.Length, state.GetDelays(), progress);
                    texture = frames[index];
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

        private static int ResolveAnimationFrameIndex(int frameCount, float[] delays, float progress)
        {
            if (frameCount <= 1)
                return 0;

            if (delays.Length == 0)
                return Math.Clamp((int) MathF.Floor(progress * frameCount), 0, frameCount - 1);

            var total = 0f;
            for (var i = 0; i < Math.Min(frameCount, delays.Length); i++)
            {
                total += MathF.Max(0.001f, delays[i]);
            }

            if (total <= 0.001f)
                return 0;

            var t = Math.Clamp(progress, 0f, 0.9999f) * total;
            var cumulative = 0f;
            for (var i = 0; i < Math.Min(frameCount, delays.Length); i++)
            {
                cumulative += MathF.Max(0.001f, delays[i]);
                if (t <= cumulative)
                    return i;
            }

            return frameCount - 1;
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
                return !room.DoorVisualOpen;

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

        private readonly struct HitboxData
        {
            public readonly DeepMaintenanceHitboxShape Shape;
            public readonly Vector2 Center;
            public readonly float Radius;
            public readonly Vector2 HalfExtents;

            public HitboxData(DeepMaintenanceHitboxShape shape, Vector2 center, float radius, Vector2 halfExtents)
            {
                Shape = shape;
                Center = center;
                Radius = radius;
                HalfExtents = halfExtents;
            }
        }

        private static HitboxData GetEntityHitbox(DeepMaintenanceEntityPrototype prototype, Vector2 position)
        {
            var center = position + new Vector2(prototype.HitboxOffsetX, prototype.HitboxOffsetY);
            if (prototype.HitboxShape == DeepMaintenanceHitboxShape.Rectangle)
            {
                var halfExtents = new Vector2(MathF.Max(0.01f, prototype.HitboxWidth * 0.5f), MathF.Max(0.01f, prototype.HitboxHeight * 0.5f));
                return new HitboxData(DeepMaintenanceHitboxShape.Rectangle, center, 0f, halfExtents);
            }

            var radius = MathF.Max(0.01f, prototype.HitboxWidth * 0.5f);
            return new HitboxData(DeepMaintenanceHitboxShape.Circle, center, radius, Vector2.Zero);
        }

        private static HitboxData GetProjectileHitbox(ProjectileData projectile)
        {
            return GetProjectileHitbox(projectile, projectile.Position);
        }

        private static HitboxData GetProjectileHitbox(ProjectileData projectile, Vector2 position)
        {
            var center = position + GetProjectileVisualDrop(projectile, position) + projectile.HitboxOffset;
            if (projectile.HitboxShape != DeepMaintenanceHitboxShape.Rectangle)
            {
                return new HitboxData(DeepMaintenanceHitboxShape.Circle,
                    center,
                    MathF.Max(0.01f, projectile.Radius),
                    Vector2.Zero);
            }

            var half = new Vector2(MathF.Max(0.01f, projectile.HitboxWidth * 0.5f), MathF.Max(0.01f, projectile.HitboxHeight * 0.5f));
            return new HitboxData(DeepMaintenanceHitboxShape.Rectangle, center, 0f, half);

        }

        private static Vector2 ResolveEntityTileCollision(Vector2 target, DeepMaintenanceEntityPrototype prototype, RoomData room)
        {
            var offset = new Vector2(prototype.HitboxOffsetX, prototype.HitboxOffsetY);
            var resolvedCenter = ResolveHitboxTileCollision(GetEntityHitbox(prototype, target), room).Center;
            return resolvedCenter - offset;
        }

        private static HitboxData ResolveHitboxTileCollision(HitboxData hitbox, RoomData room)
        {
            var center = hitbox.Center;
            var boundsHalf = hitbox.Shape == DeepMaintenanceHitboxShape.Circle ? new Vector2(hitbox.Radius, hitbox.Radius) : hitbox.HalfExtents;
            var minX = Math.Max(0, (int)MathF.Floor(center.X - boundsHalf.X) - 1);
            var maxX = Math.Min(GridWidth - 1, (int)MathF.Floor(center.X + boundsHalf.X) + 1);
            var minY = Math.Max(0, (int)MathF.Floor(center.Y - boundsHalf.Y) - 1);
            var maxY = Math.Min(GridHeight - 1, (int)MathF.Floor(center.Y + boundsHalf.Y) + 1);

            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    if (!IsSolidTile(room, x, y))
                        continue;

                    if (hitbox.Shape == DeepMaintenanceHitboxShape.Circle)
                    {
                        var nearestX = Math.Clamp(center.X, x, x + 1f);
                        var nearestY = Math.Clamp(center.Y, y, y + 1f);
                        var delta = center - new Vector2(nearestX, nearestY);
                        var distance = delta.Length();
                        if (distance >= hitbox.Radius)
                            continue;

                        if (distance <= 0.0001f)
                        {
                            delta = new Vector2(1f, 0f);
                            distance = 1f;
                        }

                        center += delta / distance * (hitbox.Radius - distance);
                        continue;
                    }

                    var tileCenter = new Vector2(x + 0.5f, y + 0.5f);
                    var deltaRect = center - tileCenter;
                    var overlapX = hitbox.HalfExtents.X + 0.5f - MathF.Abs(deltaRect.X);
                    var overlapY = hitbox.HalfExtents.Y + 0.5f - MathF.Abs(deltaRect.Y);
                    if (overlapX <= 0f || overlapY <= 0f)
                        continue;

                    if (overlapX < overlapY)
                        center.X += deltaRect.X >= 0f ? overlapX : -overlapX;
                    else
                        center.Y += deltaRect.Y >= 0f ? overlapY : -overlapY;
                }
            }

            center.X = Math.Clamp(center.X, boundsHalf.X, GridWidth - boundsHalf.X);
            center.Y = Math.Clamp(center.Y, boundsHalf.Y, GridHeight - boundsHalf.Y);
            return new HitboxData(hitbox.Shape, center, hitbox.Radius, hitbox.HalfExtents);
        }

        private static bool HitboxesOverlap(HitboxData left, HitboxData right)
        {
            switch (left.Shape)
            {
                case DeepMaintenanceHitboxShape.Circle when right.Shape == DeepMaintenanceHitboxShape.Circle:
                    return Vector2.Distance(left.Center, right.Center) <= left.Radius + right.Radius;
                case DeepMaintenanceHitboxShape.Rectangle when right.Shape == DeepMaintenanceHitboxShape.Rectangle:
                    return MathF.Abs(left.Center.X - right.Center.X) <= left.HalfExtents.X + right.HalfExtents.X &&
                           MathF.Abs(left.Center.Y - right.Center.Y) <= left.HalfExtents.Y + right.HalfExtents.Y;
            }

            var circle = left.Shape == DeepMaintenanceHitboxShape.Circle ? left : right;
            var rect = left.Shape == DeepMaintenanceHitboxShape.Rectangle ? left : right;
            var nearestX = Math.Clamp(circle.Center.X, rect.Center.X - rect.HalfExtents.X, rect.Center.X + rect.HalfExtents.X);
            var nearestY = Math.Clamp(circle.Center.Y, rect.Center.Y - rect.HalfExtents.Y, rect.Center.Y + rect.HalfExtents.Y);
            return Vector2.Distance(circle.Center, new Vector2(nearestX, nearestY)) <= circle.Radius;
        }

        private static Vector2 ResolveCircleTileCollision(Vector2 target, float radius, RoomData room)
        {
            var hitbox = new HitboxData(DeepMaintenanceHitboxShape.Circle, target, MathF.Max(0.01f, radius), Vector2.Zero);
            return ResolveHitboxTileCollision(hitbox, room).Center;
        }

        #endregion
    }
}
