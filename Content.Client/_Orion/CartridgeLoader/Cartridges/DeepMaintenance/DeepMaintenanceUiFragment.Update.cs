using System.Linq;
using System.Numerics;
using Content.Shared._Orion.CartridgeLoader.Cartridges.DeepMaintenance;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Orion.CartridgeLoader.Cartridges.DeepMaintenance;

public sealed partial class DeepMaintenanceUiFragment
{
    private sealed partial class DeepMaintenanceGameControl
    {
        #region Update

        protected override void FrameUpdate(FrameEventArgs args)
        {
            base.FrameUpdate(args);

            _debugHitboxes = _cfg.GetCVar(CCVars.DeepMaintenanceDebugHitboxes);

            if (_paused || _gameOver || _victory)
                return;

            _animationClock += args.DeltaSeconds;
            MovePlayer(args.DeltaSeconds);

            if (_heartDamageFlash > 0f)
                _heartDamageFlash = MathF.Max(0f, _heartDamageFlash - args.DeltaSeconds);

            if (_playerDamageFlash > 0f)
                _playerDamageFlash = MathF.Max(0f, _playerDamageFlash - args.DeltaSeconds);

            if (_emoteTimer > 0f)
                _emoteTimer = MathF.Max(0f, _emoteTimer - args.DeltaSeconds);

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

            if (_playerShootAnimationTimer > 0f)
                _playerShootAnimationTimer = MathF.Max(0f, _playerShootAnimationTimer - dt);

            if (_invulnerabilityTicks > 0)
                _invulnerabilityTicks--;

            HandleHeldShootKeys();
            UpdateFacingResetTimers(dt);
            TickDamageFlashes(dt);
            TickMeleeSwing(dt);
            TickTreasureAnimations(dt);
            TickEnemies(dt);
            TickChainLightning(dt);
            TickFamiliars(dt);
            TickBloodTrails(dt);
            TickProjectiles(_playerProjectiles, true, dt);
            TickProjectiles(_enemyProjectiles, false, dt);
            TickBombs(dt);
            TickBombExplosionEffects(dt);
            TickPickupAnimations(dt);
            ResolvePickupCollisions(dt);
            HandleContactDamage();
            HandleEnemyDeaths();
            HandlePickups();
            HandleShopPurchases();
            HandleTreasureInteractions();
            HandleRoomState();
            ResolveDynamicEntitySeparation(dt);
            TickDoorAnimations();

            InvalidateMeasure();
            StateChanged?.Invoke();
        }

        private void TickBombs(float dt)
        {
            for (var i = _activeBombs.Count - 1; i >= 0; i--)
            {
                var bomb = _activeBombs[i];
                bomb.Timer -= dt;
                if (bomb.Timer > 0f)
                    continue;

                ExplodeBomb(bomb.Position);
                _activeBombs.RemoveAt(i);
            }
        }

        private void TickPickupAnimations(float dt)
        {
            foreach (var pickup in CurrentRoom.Pickups)
            {
                if (pickup.SpawnTimer <= 0f)
                    continue;

                pickup.SpawnTimer = MathF.Max(0f, pickup.SpawnTimer - dt);
            }
        }

        private void TryPlaceBomb()
        {
            if (_bombs <= 0)
                return;

            if (_activeBombs.Any(b => Vector2.Distance(b.Position, _playerPos) <= 0.45f))
                return;

            if (!TryConsumeResource(ResourceType.Bomb, 1))
                return;

            _activeBombs.Add(new BombData(_playerPos, BombTimerSeconds));
            StateChanged?.Invoke();
        }

        private void ExplodeBomb(Vector2 center)
        {
            _bombExplosions.Add(new BombExplosionData(center, BombExplosionVisualDuration));

            foreach (var enemy in CurrentRoom.Enemies)
            {
                if (enemy.Hp <= 0)
                    continue;

                if (Vector2.Distance(enemy.Position, center) > BombExplosionRadius + enemy.Prototype.Radius)
                    continue;

                enemy.Hp -= BombEnemyDamage;
                enemy.DamageFlash = EntityDamageFlashDuration;
            }

            RevealSecretDoorways(center);

            for (var y = 1; y < GridHeight - 1; y++)
            {
                for (var x = 1; x < GridWidth - 1; x++)
                {
                    var tileType = CurrentRoom.Tiles[x, y];
                    if (tileType is not TileType.Obstacle and not TileType.Mushroom)
                        continue;

                    var tileCenter = new Vector2(x + 0.5f, y + 0.5f);
                    if (Vector2.Distance(tileCenter, center) > BombObjectDamageRadius)
                        continue;

                    CurrentRoom.Tiles[x, y] = TileType.Floor;
                }
            }
        }

        private void HandlePickups()
        {
            for (var i = CurrentRoom.Pickups.Count - 1; i >= 0; i--)
            {
                var pickup = CurrentRoom.Pickups[i];
                if (pickup.SpawnTimer > 0f || Vector2.Distance(_playerPos, pickup.Position) > PickupRadius)
                    continue;

                var picked = false;
                switch (pickup.Type)
                {
                    case PickupType.Coin:
                        picked = TryAddResource(ResourceType.Coin, pickup.Amount);
                        break;
                    case PickupType.Bomb:
                        picked = TryAddResource(ResourceType.Bomb, pickup.Amount);
                        break;
                    case PickupType.Key:
                        picked = TryAddResource(ResourceType.Key, pickup.Amount);
                        break;
                    case PickupType.Heart:
                        picked = TryAddHealth(pickup.Amount);
                        break;
                }

                if (!picked)
                    continue;

                CurrentRoom.Pickups.RemoveAt(i);
                StateChanged?.Invoke();
            }
        }

        private void HandleShopPurchases()
        {
            if (CurrentRoom.Type != RoomType.Shop)
                return;

            for (var i = CurrentRoom.ShopSlots.Count - 1; i >= 0; i--)
            {
                var slot = CurrentRoom.ShopSlots[i];
                if (slot.Sold || slot.Item == ShopItemType.None)
                    continue;

                if (Vector2.Distance(_playerPos, slot.Position) > ShopPurchaseRadius + _playerProto.Radius)
                    continue;

                if (!CanReceiveShopItem(slot))
                    continue;

                var effectivePrice = ApplyPriceModifiers(slot.Price);
                if (!TryConsumeResource(ResourceType.Coin, effectivePrice))
                    continue;

                ApplyShopPurchase(slot);
                slot.Sold = true;
                StateChanged?.Invoke();
            }
        }

        private void ApplyShopPurchase(ShopSlotData slot)
        {
            switch (slot.Item)
            {
                case ShopItemType.Coin:
                    TryAddResource(ResourceType.Coin, slot.Amount);
                    break;
                case ShopItemType.Bomb:
                    TryAddResource(ResourceType.Bomb, slot.Amount);
                    break;
                case ShopItemType.Heart:
                    TryAddHealth(slot.Amount);
                    break;
                case ShopItemType.Key:
                    TryAddResource(ResourceType.Key, slot.Amount);
                    break;
                case ShopItemType.Relic:
                    if (!string.IsNullOrWhiteSpace(slot.RelicId) && _prototype.TryIndex<DeepMaintenanceRelicPrototype>(slot.RelicId, out var relic))
                        PickupRelic(relic);
                    break;
            }
        }

        private bool CanReceiveShopItem(ShopSlotData slot)
        {
            return slot.Item switch
            {
                ShopItemType.Coin => GetResource(ResourceType.Coin) + slot.Amount <= GetResourceMax(ResourceType.Coin),
                ShopItemType.Bomb => GetResource(ResourceType.Bomb) + slot.Amount <= GetResourceMax(ResourceType.Bomb),
                ShopItemType.Heart => PlayerHp + slot.Amount <= MaxPlayerHp,
                ShopItemType.Key => GetResource(ResourceType.Key) + slot.Amount <= GetResourceMax(ResourceType.Key),
                _ => true,
            };
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

        private void TickBombExplosionEffects(float dt)
        {
            for (var i = _bombExplosions.Count - 1; i >= 0; i--)
            {
                var explosion = _bombExplosions[i];
                explosion.Timer = MathF.Max(0f, explosion.Timer - dt);
                if (explosion.Timer <= 0f)
                    _bombExplosions.RemoveAt(i);
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
            _playerPos = ResolveEntityTileCollision(target, _playerProto, CurrentRoom);
            TryRoomTransition();
        }

        private void HandleHeldShootKeys()
        {
            var shootDirection = GetHeldShootDirection();
            if (HasClaymoreRelic())
            {
                if (shootDirection != Vector2.Zero)
                {
                    _claymoreCharging = true;
                    _claymoreChargeDirection = NormalizeSafe(shootDirection);
                    _claymoreChargeTimer += TickSeconds;
                }
                else if (_claymoreCharging)
                {
                    ReleaseClaymoreCharge();
                }

                return;
            }

            if (_playerShootCooldown > 0f)
                return;

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

                if (enemy.Frozen)
                    continue;

                if (enemy.SpawnGraceTicks > 0)
                    enemy.SpawnGraceTicks--;

                if (enemy.FearTimer > 0f)
                    enemy.FearTimer = MathF.Max(0f, enemy.FearTimer - dt);

                if (enemy.FearBossCooldown > 0f)
                    enemy.FearBossCooldown = MathF.Max(0f, enemy.FearBossCooldown - dt);

                for (var effectIndex = enemy.VisualEffects.Count - 1; effectIndex >= 0; effectIndex--)
                {
                    var effect = enemy.VisualEffects[effectIndex];
                    effect.TimeRemaining = MathF.Max(0f, effect.TimeRemaining - dt);
                    if (effect.TimeRemaining <= 0f)
                        enemy.VisualEffects.RemoveAt(effectIndex);
                }

                var toPlayer = _playerPos - enemy.Position;
                var predictedPlayerPos = _playerPos + _playerVelocity * 0.4f;
                var toPredictedPlayer = predictedPlayerPos - enemy.Position;
                var directionToPlayer = NormalizeSafe(toPlayer);
                var directionToPredictedPlayer = NormalizeSafe(toPredictedPlayer);
                var hasDirectVision = HasLineOfSight(enemy.Position, _playerPos, enemy.Prototype.Radius);
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

                ApplyFearAura(enemy);
                if (enemy.FearTimer > 0f)
                {
                    var fleeDirection = NormalizeSafe(enemy.Position - predictedPlayerPos);
                    if (fleeDirection == Vector2.Zero)
                        fleeDirection = NormalizeSafe(enemy.Position - _playerPos);
                    MoveEnemyWithAvoidance(enemy, fleeDirection, fleeDirection, dt, hasDirectVision, enemy.Prototype.Shooter ? 0.8f : 1f);
                    continue;
                }

                if (enemy.Prototype.Shooter)
                {
                    if (enemy.ShootCooldownTicks <= 0 && hasDirectVision)
                    {
                        FireEnemyProjectiles(enemy, directionToPlayer);
                        PlaySfx(SfxEnemyShoot, -10f);

                        enemy.ShootCooldownTicks = enemy.Prototype.ShootCooldownTicks;
                    }

                    var movement = Vector2.Zero;
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

                        movement = NormalizeSafe(strafe + kiting + directionToPredictedPlayer * 0.15f);
                    }

                    if (movement == Vector2.Zero)
                        movement = directionToPredictedPlayer;

                    MoveEnemyWithAvoidance(enemy, movement, directionToPredictedPlayer, dt, hasDirectVision, 0.5f);
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
                var chaseDirection = NormalizeSafe(directionToPredictedPlayer + flank * (hasDirectVision ? 1f : 0.45f));
                MoveEnemyWithAvoidance(enemy, chaseDirection, directionToPredictedPlayer, dt, hasDirectVision, 1f);
            }

            ResolveEntityCollisions(CurrentRoom.Enemies);
        }

        private EnemyData? GetCurrentBoss()
        {
            if (_rooms.Count == 0 || RoomIndex < 0 || RoomIndex >= _rooms.Count)
                return null;

            return _rooms[RoomIndex].Enemies.FirstOrDefault(enemy => enemy.Hp > 0 && enemy.Prototype.IsBoss);
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
            enemy.Position = ResolveEntityTileCollision(target, enemy.Prototype, CurrentRoom);
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

        private void MoveEnemyWithAvoidance(EnemyData enemy, Vector2 preferredDirection, Vector2 fallbackDirection, float dt, bool hasDirectVision, float speedScale)
        {
            if (preferredDirection == Vector2.Zero)
                preferredDirection = fallbackDirection;

            var chosenDirection = ResolveEnemyMovementDirection(enemy, preferredDirection, fallbackDirection, hasDirectVision);
            if (chosenDirection == Vector2.Zero)
                return;

            enemy.BodyFacing = FacingFromVector(chosenDirection, enemy.BodyFacing);
            var target = enemy.Position + chosenDirection * enemy.Prototype.MoveSpeed * speedScale * dt;
            enemy.Position = ResolveEntityTileCollision(target, enemy.Prototype, CurrentRoom);
        }

        private Vector2 ResolveEnemyMovementDirection(EnemyData enemy, Vector2 preferredDirection, Vector2 fallbackDirection, bool hasDirectVision)
        {
            if (preferredDirection == Vector2.Zero)
                return Vector2.Zero;

            if (hasDirectVision && IsDirectionWalkable(enemy, preferredDirection))
            {
                enemy.AvoidanceTicks = 0;
                return preferredDirection;
            }

            if (enemy.AvoidanceTicks <= 0)
            {
                enemy.AvoidanceTicks = EnemyAvoidanceLockTicks;
                enemy.AvoidanceDirection = _random.NextDouble() < 0.5 ? -1 : 1;
            }
            else
            {
                enemy.AvoidanceTicks--;
            }

            var anchor = fallbackDirection == Vector2.Zero ? preferredDirection : fallbackDirection;
            var orderedAngles = enemy.AvoidanceDirection > 0
                ? new[] { 28f, 56f, 84f, 112f, 140f, -28f, -56f, -84f, -112f, -140f }
                : new[] { -28f, -56f, -84f, -112f, -140f, 28f, 56f, 84f, 112f, 140f };

            foreach (var angle in orderedAngles)
            {
                var candidate = NormalizeSafe(Rotate(anchor, MathF.PI * angle / 180f));
                if (candidate != Vector2.Zero && IsDirectionWalkable(enemy, candidate))
                    return candidate;
            }

            return IsDirectionWalkable(enemy, -preferredDirection) ? -preferredDirection : Vector2.Zero;
        }

        private bool IsDirectionWalkable(EnemyData enemy, Vector2 direction)
        {
            if (direction == Vector2.Zero)
                return false;

            var target = enemy.Position + NormalizeSafe(direction) * EnemyAvoidanceCheckDistance;
            var resolved = ResolveEntityTileCollision(target, enemy.Prototype, CurrentRoom);
            return Vector2.DistanceSquared(resolved, target) <= 0.0015f &&
                   HasLineOfSight(enemy.Position, resolved, enemy.Prototype.Radius * 0.55f);
        }

        private bool HasLineOfSight(Vector2 start, Vector2 end, float sampleRadius)
        {
            var delta = end - start;
            var distance = delta.Length();
            if (distance <= 0.001f)
                return true;

            var dir = delta / distance;
            var steps = Math.Max(1, (int) MathF.Ceiling(distance * EnemyVisionSamplesPerTile));
            for (var i = 1; i <= steps; i++)
            {
                var point = start + dir * (distance * i / steps);
                if (TouchesBlockedTile(point + new Vector2(sampleRadius, 0f), CurrentRoom) ||
                    TouchesBlockedTile(point + new Vector2(-sampleRadius, 0f), CurrentRoom) ||
                    TouchesBlockedTile(point + new Vector2(0f, sampleRadius), CurrentRoom) ||
                    TouchesBlockedTile(point + new Vector2(0f, -sampleRadius), CurrentRoom))
                    return false;
            }

            return true;
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

                    var leftHitbox = GetEntityHitbox(left.Prototype, left.Position);
                    var rightHitbox = GetEntityHitbox(right.Prototype, right.Position);
                    if (!HitboxesOverlap(leftHitbox, rightHitbox))
                        continue;

                    var delta = rightHitbox.Center - leftHitbox.Center;
                    if (delta.LengthSquared() <= 0.0001f)
                        delta = new Vector2(1f, 0f);

                    var normal = Vector2.Normalize(delta);
                    const float correction = 0.06f;
                    left.Position = ResolveEntityTileCollision(left.Position - normal * correction, left.Prototype, CurrentRoom);
                    right.Position = ResolveEntityTileCollision(right.Position + normal * correction, right.Prototype, CurrentRoom);
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

                var projectileHitbox = GetProjectileHitbox(projectile);
                if (!InsideMap(projectileHitbox.Center) || IsSolid(projectileHitbox.Center, CurrentRoom))
                {
                    PlaySfx(SfxProjectileHit, -12f);
                    projectiles.RemoveAt(i);
                    continue;
                }

                if (playerProjectile)
                {
                    if (!TryHitEnemy(projectile, projectileHitbox))
                        continue;

                    projectiles.RemoveAt(i);
                    continue;
                }

                var playerHitbox = GetEntityHitbox(_playerProto, _playerPos);
                if (!HitboxesOverlap(projectileHitbox, playerHitbox))
                    continue;

                if (TryBluespaceProjectileBlock(projectile, projectiles, i))
                    continue;

                DamagePlayer();
                PlaySfx(SfxProjectileHit, -10f);
                projectiles.RemoveAt(i);
            }
        }

        private bool TryHitEnemy(ProjectileData projectile, HitboxData projectileHitbox)
        {
            foreach (var enemy in CurrentRoom.Enemies)
            {
                if (enemy.Hp <= 0)
                    continue;

                if (!HitboxesOverlap(projectileHitbox, GetEntityHitbox(enemy.Prototype, enemy.Position)))
                    continue;

                enemy.Hp -= (int) MathF.Ceiling(projectile.Damage);
                enemy.DamageFlash = EntityDamageFlashDuration;

                if (projectile.FreezeOnHit && _random.NextDouble() < projectile.FreezeChance && (projectile.FreezeBosses || !enemy.Prototype.IsBoss))
                {
                    enemy.Frozen = true;
                    ApplyVisualStatusEffect(enemy, "frozen", new Color(140, 220, 255), 9999f);
                }

                var knockbackDir = NormalizeSafe(enemy.Position - projectile.SourcePosition);
                if (knockbackDir != Vector2.Zero)
                {
                    var knockbackTarget = enemy.Position + knockbackDir * EnemyHitKnockback;
                    enemy.Position = ResolveEntityTileCollision(knockbackTarget, enemy.Prototype, CurrentRoom);
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

                if (!HitboxesOverlap(GetEntityHitbox(_playerProto, _playerPos), GetEntityHitbox(enemy.Prototype, enemy.Position)))
                    continue;

                if (enemy.Frozen)
                {
                    ShatterFrozenEnemy(enemy);
                    return;
                }

                if (TryBluespaceContactBlock(enemy))
                    return;

                DamagePlayer();
                return;
            }
        }

        private void HandleEnemyDeaths()
        {
            foreach (var enemy in CurrentRoom.Enemies)
            {
                if (enemy.Hp > 0 || enemy.DeathHandled)
                    continue;

                enemy.DeathHandled = true;
                ApplyOnKillDamageBonuses();
                SpawnEnemyDeathBurstProjectiles(enemy);
            }
        }

        private void ApplyOnKillDamageBonuses()
        {
            foreach (var relic in _activeRelics)
            {
                if (relic.OnEnemyKillRoomDamageBonus <= 0f)
                    continue;

                _roomKillDamageBonus = MathF.Min(relic.OnEnemyKillRoomDamageBonusMax, _roomKillDamageBonus + relic.OnEnemyKillRoomDamageBonus);
            }
        }

        private void ApplyOnDamagedFloorBonuses()
        {
            foreach (var relic in _activeRelics)
            {
                if (relic.OnDamagedFloorDamageBonusSequence.Count == 0)
                    continue;

                var index = Math.Clamp(_floorDamageBonusStacks, 0, relic.OnDamagedFloorDamageBonusSequence.Count - 1);
                if (_floorDamageBonusStacks >= relic.OnDamagedFloorDamageBonusSequence.Count)
                    continue;

                _floorDamageBonus += relic.OnDamagedFloorDamageBonusSequence[index];
                _floorDamageBonusStacks++;
            }
        }

        private void SpawnEnemyDeathBurstProjectiles(EnemyData enemy)
        {
            foreach (var relic in _activeRelics)
            {
                if (!relic.EnemyDeathBurstEnabled)
                    continue;

                var scaled = (int) MathF.Ceiling(enemy.Prototype.MaxHp * MathF.Max(0.1f, relic.EnemyDeathBurstProjectilesPerMaxHp));
                var count = Math.Clamp(scaled, relic.EnemyDeathBurstMinProjectiles, relic.EnemyDeathBurstMaxProjectiles);
                var projectilePrototype = _prototype.Index<DeepMaintenanceProjectilePrototype>(_playerProto.ProjectilePrototype);
                var damage = relic.EnemyDeathBurstDamageBase + relic.EnemyDeathBurstDamagePerFloor * Math.Max(0, _currentFloor - 1);
                for (var i = 0; i < count; i++)
                {
                    var angle = _random.NextSingle() * MathF.PI * 2f;
                    var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                    SpawnProjectile(_playerProjectiles, enemy.Position, direction * projectilePrototype.Speed, projectilePrototype, 1f, damage, Color.Red, projectilePrototype.Lifetime);
                }
            }
        }

        private void ShatterFrozenEnemy(EnemyData enemy)
        {
            enemy.Hp = 0;
            enemy.DeathHandled = false;
            enemy.Frozen = false;

            var projectilePrototype = _prototype.Index<DeepMaintenanceProjectilePrototype>(_playerProto.ProjectilePrototype);
            const int shards = 8;
            for (var i = 0; i < shards; i++)
            {
                var angle = i / (float) shards * MathF.PI * 2f;
                var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                var projectile = SpawnProjectile(_playerProjectiles, enemy.Position, dir * projectilePrototype.Speed, projectilePrototype, 1f, 2f, new Color(180, 240, 255), projectilePrototype.Lifetime);
                projectile.FreezeOnHit = true;
                projectile.FreezeChance = 0.5f;
                projectile.FreezeBosses = false;
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
                var fallback = _random.NextDouble() < TreasureShooterSpawnChance ? _chaserProto.ID : _shooterProto.ID;
                var enemyPool = _activeFloorConfig?.EnemyPool ?? new List<DeepMaintenanceWeightedEntityEntry>();
                var proto = ChooseEntityFromPool(enemyPool, fallback);
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
            var rolls = _entityTable.GetSpawns(lootTable, _random)
                .Select(id => id.ToString())
                .Where(id => !_runSeenRelicIds.Contains(id))
                .ToList();

            if (rolls.Count == 0)
                return null;

            var selected = rolls[_random.Next(rolls.Count)];
            _runSeenRelicIds.Add(selected);
            return selected;
        }

        private void PickupRelic(DeepMaintenanceRelicPrototype relic)
        {
            if (_activeRelics.Any(active => active.ID == relic.ID))
                return;

            _activeRelics.Add(relic);
            _runSeenRelicIds.Add(relic.ID);

            if (relic.NoDamageRoomStartBonus > 0f)
                _rhythmicKnifeBonus = MathF.Min(relic.NoDamageRoomFireRateMax, _rhythmicKnifeBonus + relic.NoDamageRoomStartBonus);

            foreach (var grant in relic.ResourceGrants)
            {
                if (!Enum.TryParse<ResourceType>(grant.ResourceType, true, out var resourceType))
                    continue;

                AddResource(resourceType, grant.Amount);
            }

            if (relic.MaxHealthBonusOnPickup > 0)
                SetPlayerHealth(PlayerHp, MaxPlayerHp + relic.MaxHealthBonusOnPickup);

            if (relic.FullHealOnPickup)
                SetPlayerHealth(MaxPlayerHp, MaxPlayerHp);

            RebuildFamiliarsFromRelics();

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

            _tookDamageInRoom = true;
            ResetRhythmicKnifeOnDamage();
            TriggerElectroRakEffects();
            ApplyOnDamagedFloorBonuses();

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

        private void ApplyVisualStatusEffect(EnemyData enemy, string statusId, Color tint, float duration)
        {
            var existing = enemy.VisualEffects.FirstOrDefault(effect => effect.StatusId == statusId);
            if (existing != null)
            {
                existing.TimeRemaining = MathF.Max(existing.TimeRemaining, duration);
                existing.Tint = tint;
                return;
            }

            enemy.VisualEffects.Add(new VisualStatusEffectData(statusId, tint, duration));
        }

        private void ApplyFearAura(EnemyData enemy)
        {
            var fearRadius = _activeRelics.Select(relic => relic.FearAuraRadius).DefaultIfEmpty(0f).Max();
            if (fearRadius <= 0f)
                return;

            if (Vector2.Distance(enemy.Position, _playerPos) > fearRadius)
                return;

            var linger = _activeRelics.Select(relic => relic.FearLingerSeconds).DefaultIfEmpty(0f).Max();
            if (enemy.Prototype.IsBoss)
            {
                var cooldown = _activeRelics.Select(relic => relic.FearBossCooldownSeconds).DefaultIfEmpty(0f).Max();
                if (enemy.FearBossCooldown > 0f)
                    return;

                enemy.FearBossCooldown = MathF.Max(0f, cooldown);
            }

            enemy.FearTimer = MathF.Max(enemy.FearTimer, MathF.Max(0f, linger));
            ApplyVisualStatusEffect(enemy, "fear", new Color(190, 120, 255), enemy.FearTimer);
        }

        private void RebuildFamiliarsFromRelics()
        {
            _familiars.Clear();
            foreach (var relic in _activeRelics)
            {
                foreach (var config in relic.FamiliarConfigs)
                {
                    for (var i = 0; i < Math.Max(1, config.Count); i++)
                    {
                        _familiars.Add(new FamiliarData(relic.ID, config, _playerPos));
                    }
                }
            }
        }

        private void HandleFamiliarRoomRewards()
        {
            foreach (var familiar in _familiars)
            {
                if (familiar.Config.RoomRewardEvery <= 0)
                    continue;

                familiar.RoomCounter++;
                if (familiar.RoomCounter % familiar.Config.RoomRewardEvery != 0)
                    continue;

                if (!Enum.TryParse<PickupType>(familiar.Config.RoomRewardPickupType, true, out var pickupType))
                    pickupType = PickupType.Heart;

                SpawnPickup(CurrentRoom, pickupType, Math.Max(1, familiar.Config.RoomRewardAmount), familiar.Position + new Vector2(0.4f, 0f), PickupSpawnAnimationDuration);
            }
        }

        private void TickFamiliars(float dt)
        {
            if (_familiars.Count == 0)
                return;

            var aliveEnemies = CurrentRoom.Enemies.Where(e => e.Hp > 0).ToList();
            for (var i = 0; i < _familiars.Count; i++)
            {
                var familiar = _familiars[i];
                var side = i % 2 == 0 ? -1f : 1f;
                var desiredOffset = new Vector2(side * familiar.Config.FollowDistance, -0.85f + (i % 3) * 0.25f);
                var desiredPosition = Vector2.Lerp(familiar.Position, _playerPos + desiredOffset, Math.Clamp(familiar.Config.MoveSpeed * dt, 0f, 1f));
                familiar.Position = MoveFamiliarWithCollision(familiar.Position, desiredPosition, FamiliarCollisionRadius, CurrentRoom);

                var tetherMaxDistance = MathF.Max(0.9f, familiar.Config.FollowDistance + 0.9f);
                var toPlayer = familiar.Position - _playerPos;
                if (toPlayer.LengthSquared() > tetherMaxDistance * tetherMaxDistance)
                {
                    var pullTarget = _playerPos + NormalizeSafe(toPlayer) * tetherMaxDistance;
                    familiar.Position = ResolveCircleTileCollision(pullTarget, FamiliarCollisionRadius, CurrentRoom);
                }

                if (familiar.Config.SpawnBloodTrail)
                {
                    familiar.TrailTimer -= dt;
                    if (familiar.TrailTimer <= 0f)
                    {
                        familiar.TrailTimer = 0.1f;
                        _bloodTrails.Add(new BloodTrailData(familiar.Position, familiar.Config.BloodTrailRadius, familiar.Config.BloodTrailDps, familiar.Config.BloodTrailLifetime));
                    }
                }

                if (string.Equals(familiar.Config.Behavior, "Interceptor", StringComparison.OrdinalIgnoreCase))
                {
                    if (familiar.RestTimer > 0f)
                    {
                        familiar.RestTimer = MathF.Max(0f, familiar.RestTimer - dt);
                    }
                    else
                    {
                        if (familiar.InterceptCooldown > 0f)
                            familiar.InterceptCooldown = MathF.Max(0f, familiar.InterceptCooldown - dt);

                        if (familiar.InterceptCooldown <= 0f)
                        {
                            var idx = _enemyProjectiles.FindIndex(p => Vector2.Distance(p.Position, familiar.Position) <= familiar.Config.InterceptRadius);
                            if (idx >= 0)
                            {
                                var projectile = _enemyProjectiles[idx];
                                var direction = NormalizeSafe(projectile.Position - _playerPos);
                                projectile.Velocity = direction * MathF.Max(projectile.Velocity.Length(), 0.5f);
                                _enemyProjectiles.RemoveAt(idx);
                                _playerProjectiles.Add(projectile);
                                familiar.InterceptCooldown = MathF.Max(0.05f, familiar.Config.InterceptCooldown);
                                if (_random.NextDouble() < familiar.Config.RestChance)
                                    familiar.RestTimer = MathF.Max(0f, familiar.Config.RestDuration);
                            }
                        }
                    }

                    var contactDamage = familiar.Config.ContactDps * dt;
                    foreach (var enemy in CurrentRoom.Enemies)
                    {
                        if (enemy.Hp <= 0 || Vector2.Distance(enemy.Position, familiar.Position) > enemy.Prototype.Radius + 0.22f)
                            continue;

                        enemy.Hp -= (int) MathF.Ceiling(ApplyNonTearDamageModifiers(contactDamage));
                        enemy.DamageFlash = EntityDamageFlashDuration;
                    }
                }

                familiar.ShootTimer -= dt;
                if (familiar.ShootTimer > 0f)
                    continue;

                familiar.ShootTimer = MathF.Max(0.05f, familiar.Config.ShootInterval);
                var projectilePrototype = _prototype.Index<DeepMaintenanceProjectilePrototype>(_playerProto.ProjectilePrototype);
                var directions = new List<Vector2>();
                var hasAnyEnemy = aliveEnemies.Count > 0;
                if (!hasAnyEnemy)
                    continue;

                if (familiar.Config.ShootFourDirections)
                {
                    var candidates = new[] { new Vector2(1,0), new Vector2(-1,0), new Vector2(0,1), new Vector2(0,-1) };
                    foreach (var candidate in candidates)
                    {
                        if (aliveEnemies.Any(enemy => HasLineOfSight(familiar.Position, enemy.Position, FamiliarCollisionRadius) &&
                                                      Vector2.Dot(NormalizeSafe(enemy.Position - familiar.Position), candidate) > 0.5f))
                            directions.Add(candidate);
                    }
                }
                else if (familiar.Config.ShootAlongPlayerAim)
                {
                    var aimDirection = _lastPlayerShotDirection == Vector2.Zero ? Vector2.UnitX : NormalizeSafe(_lastPlayerShotDirection);
                    if (aliveEnemies.Any(enemy => HasLineOfSight(familiar.Position, enemy.Position, FamiliarCollisionRadius) &&
                                                  Vector2.Dot(NormalizeSafe(enemy.Position - familiar.Position), aimDirection) > 0.4f))
                        directions.Add(aimDirection);
                }
                else if (familiar.Config.ShootNearestEnemy && aliveEnemies.Count > 0)
                {
                    var target = aliveEnemies
                        .Where(enemy => HasLineOfSight(familiar.Position, enemy.Position, FamiliarCollisionRadius))
                        .OrderBy(e => Vector2.Distance(e.Position, familiar.Position))
                        .FirstOrDefault();

                    if (target != null)
                        directions.Add(NormalizeSafe(target.Position - familiar.Position));
                }
                else
                {
                    var target = aliveEnemies
                        .Where(enemy => HasLineOfSight(familiar.Position, enemy.Position, FamiliarCollisionRadius))
                        .OrderBy(e => Vector2.Distance(e.Position, familiar.Position))
                        .FirstOrDefault();

                    if (target != null)
                        directions.Add(NormalizeSafe(target.Position - familiar.Position));
                }

                if (directions.Count == 0)
                    continue;

                foreach (var direction in directions)
                {
                    if (direction == Vector2.Zero)
                        continue;

                    var tint = familiar.Config.FixedRedTint ? Color.Red : Color.White;
                    var damage = familiar.Config.ProjectileDamage;
                    if (familiar.Config.UsePlayerCurrentDamage)
                    {
                        var playerDamage = _prototype.Index<DeepMaintenanceProjectilePrototype>(_playerProto.ProjectilePrototype).Damage + GetDamageFlatBonus();
                        damage = playerDamage * familiar.Config.PlayerDamageScale;
                    }

                    var projectile = SpawnProjectile(_playerProjectiles, familiar.Position, NormalizeSafe(direction) * familiar.Config.ProjectileSpeed, projectilePrototype, 1f, damage, tint, projectilePrototype.Lifetime);
                    projectile.FreezeOnHit = familiar.Config.CanFreezeOnHit;
                    projectile.FreezeChance = familiar.Config.FreezeChance;
                    projectile.FreezeBosses = familiar.Config.FreezeBosses;
                }

                switch (familiar.Config)
                {
                    case { BurstCount: > 0, BurstDamageOptions.Count: > 0 }:
                    {
                        familiar.BurstTimer -= dt;
                        if (familiar.BurstTimer <= 0f)
                        {
                            familiar.BurstTimer = MathF.Max(0.1f, familiar.Config.BurstInterval);
                            for (var n = 0; n < familiar.Config.BurstCount; n++)
                            {
                                var angle = n / (float) familiar.Config.BurstCount * MathF.PI * 2f;
                                var dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                                if (!aliveEnemies.Any(enemy => HasLineOfSight(familiar.Position, enemy.Position, FamiliarCollisionRadius) && Vector2.Dot(NormalizeSafe(enemy.Position - familiar.Position), dir) > 0.35f))
                                    continue;

                                var dmg = familiar.Config.BurstDamageOptions[_random.Next(familiar.Config.BurstDamageOptions.Count)];
                                SpawnProjectile(_playerProjectiles, familiar.Position, dir * familiar.Config.ProjectileSpeed, projectilePrototype, 1f, dmg, Color.White, projectilePrototype.Lifetime);
                            }
                        }

                        break;
                    }
                }
            }
        }

        private static Vector2 MoveFamiliarWithCollision(Vector2 current, Vector2 desired, float radius, RoomData room)
        {
            var distance = Vector2.Distance(current, desired);
            var steps = Math.Max(1, (int) MathF.Ceiling(distance / 0.2f));
            var position = current;

            for (var i = 1; i <= steps; i++)
            {
                var nextTarget = Vector2.Lerp(current, desired, i / (float) steps);
                var resolved = ResolveCircleTileCollision(nextTarget, radius, room);

                // Stop if we strongly collide into geometry to avoid tunneling through walls
                if (Vector2.DistanceSquared(resolved, nextTarget) > 0.0225f)
                    break;

                position = resolved;
            }

            return ResolveCircleTileCollision(position, radius, room);
        }

        private void TickBloodTrails(float dt)
        {
            for (var i = _bloodTrails.Count - 1; i >= 0; i--)
            {
                var trail = _bloodTrails[i];
                trail.Lifetime -= dt;
                if (trail.Lifetime <= 0f)
                {
                    _bloodTrails.RemoveAt(i);
                    continue;
                }

                var damage = trail.Dps * dt;
                foreach (var enemy in CurrentRoom.Enemies)
                {
                    if (enemy.Hp <= 0 || Vector2.Distance(enemy.Position, trail.Position) > enemy.Prototype.Radius + trail.Radius)
                        continue;

                    enemy.Hp -= (int) MathF.Ceiling(damage);
                    enemy.DamageFlash = EntityDamageFlashDuration;
                }
            }
        }

        private void TickChainLightning(float dt)
        {
            var source = _activeRelics.FirstOrDefault(relic => relic.ChainLightningEnabled);
            if (source == null)
                return;

            _chainLightningAccumulator += dt;
            var interval = 1f / MathF.Max(0.05f, source.ChainLightningRate);
            while (_chainLightningAccumulator >= interval)
            {
                _chainLightningAccumulator -= interval;
                FireChainLightning(source);
            }
        }

        private void FireChainLightning(DeepMaintenanceRelicPrototype source)
        {
            var remaining = CurrentRoom.Enemies.Where(e => e.Hp > 0).ToList();
            if (remaining.Count == 0)
                return;

            var currentPos = _playerPos;
            var maxTargets = Math.Max(1, source.ChainLightningMaxTargets);
            for (var i = 0; i < maxTargets; i++)
            {
                EnemyData? target;
                if (i == 0)
                    target = remaining.OrderBy(e => Vector2.Distance(e.Position, currentPos)).FirstOrDefault(e => Vector2.Distance(e.Position, currentPos) <= source.ChainLightningRadius);
                else
                    target = remaining.OrderBy(e => Vector2.Distance(e.Position, currentPos)).FirstOrDefault(e => Vector2.Distance(e.Position, currentPos) <= source.ChainLightningJumpRadius);

                if (target == null)
                    break;

                var baseDamage = (_prototype.Index<DeepMaintenanceProjectilePrototype>(_playerProto.ProjectilePrototype).Damage + GetDamageFlatBonus()) * source.ChainLightningDamageMultiplier;
                target.Hp -= (int) MathF.Ceiling(ApplyNonTearDamageModifiers(baseDamage));
                target.DamageFlash = EntityDamageFlashDuration;
                currentPos = target.Position;
                remaining.Remove(target);
            }
        }

        private bool TryBluespaceContactBlock(EnemyData enemy)
        {
            var chance = _activeRelics.Select(relic => relic.ContactOrProjectileBlockChance).DefaultIfEmpty(0f).Max();
            if (chance <= 0f || _random.NextDouble() > chance)
                return false;

            var knockback = _activeRelics.Select(relic => relic.ContactBlockKnockback).DefaultIfEmpty(0f).Max();
            var direction = NormalizeSafe(enemy.Position - _playerPos);
            var target = enemy.Position + direction * knockback;
            var resolved = ResolveEntityTileCollision(target, enemy.Prototype, CurrentRoom);
            enemy.Position = resolved;
            ApplyCollisionDamageAfterKnockback(enemy, target, resolved);
            return true;
        }

        private bool TryBluespaceProjectileBlock(ProjectileData projectile, List<ProjectileData> source, int index)
        {
            var chance = _activeRelics.Select(relic => relic.ContactOrProjectileBlockChance).DefaultIfEmpty(0f).Max();
            if (chance <= 0f || _random.NextDouble() > chance)
                return false;

            source.RemoveAt(index);
            var dir = NormalizeSafe(projectile.Position - _playerPos);
            projectile.Velocity = dir * MathF.Max(0.5f, projectile.Velocity.Length());
            _playerProjectiles.Add(projectile);
            return true;
        }

        private void ApplyCollisionDamageAfterKnockback(EnemyData enemy, Vector2 target, Vector2 resolved)
        {
            var hitWall = Vector2.DistanceSquared(target, resolved) > 0.01f;
            var hitEnemy = CurrentRoom.Enemies.Any(other => other != enemy && other.Hp > 0 && Vector2.Distance(other.Position, resolved) < other.Prototype.Radius + enemy.Prototype.Radius + 0.08f);
            if (!hitWall && !hitEnemy)
                return;

            var relic = _activeRelics.FirstOrDefault(r => r.ContactOrProjectileBlockChance > 0f);
            if (relic == null)
                return;

            var damage = relic.CollisionDamageBase + relic.CollisionDamagePerFloor * _currentFloor;
            enemy.Hp -= (int) MathF.Ceiling(ApplyNonTearDamageModifiers(damage));
            enemy.DamageFlash = EntityDamageFlashDuration;
        }

        private void TriggerElectroRakEffects()
        {
            var relic = _activeRelics.FirstOrDefault(r => r.OnDamageRadialProjectileCount > 0);
            if (relic == null)
                return;

            var projectilePrototype = _prototype.Index<DeepMaintenanceProjectilePrototype>(_playerProto.ProjectilePrototype);
            var count = Math.Max(1, relic.OnDamageRadialProjectileCount);
            var speed = projectilePrototype.Speed * GetProjectileSpeedMultiplier(EyeSource.None);
            var lifetime = GetProjectileLifetime(projectilePrototype, EyeSource.None);
            var heightScale = GetProjectileHeightScale();
            for (var i = 0; i < count; i++)
            {
                var angle = i / (float) count * MathF.PI * 2f;
                var direction = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
                SpawnProjectile(_playerProjectiles, _playerPos, direction * speed, projectilePrototype, 1f, relic.OnDamageRadialProjectileDamage, Color.White, lifetime, heightScale);
            }

            _electroRakDamageTriggersInRoom++;
            _electroRakRoomFireRateBonus += _electroRakDamageTriggersInRoom == 1
                ? relic.OnDamageFireRateFirstBonus
                : relic.OnDamageFireRateStackBonus;
        }

        private void HandleRoomCompletionTransition(RoomData room)
        {
            if (!room.Cleared)
                return;

            HandleFamiliarRoomRewards();

            if (_tookDamageInRoom)
                return;

            var relic = _activeRelics.FirstOrDefault(r => r.NoDamageRoomFireRatePerRoom > 0f);
            if (relic == null)
                return;

            _rhythmicKnifeBonus = MathF.Min(relic.NoDamageRoomFireRateMax, _rhythmicKnifeBonus + relic.NoDamageRoomFireRatePerRoom);
        }

        private void ResetRhythmicKnifeOnDamage()
        {
            if (_activeRelics.Any(relic => relic.ResetNoDamageBonusOnHit))
                _rhythmicKnifeBonus = 0f;
        }

        private void ReflectEnemyProjectiles(Vector2 center, float radius)
        {
            for (var i = _enemyProjectiles.Count - 1; i >= 0; i--)
            {
                var projectile = _enemyProjectiles[i];
                if (Vector2.Distance(projectile.Position, center) > radius)
                    continue;

                _enemyProjectiles.RemoveAt(i);
                var direction = NormalizeSafe(projectile.Position - _playerPos);
                projectile.Velocity = direction * MathF.Max(0.5f, projectile.Velocity.Length());
                _playerProjectiles.Add(projectile);
            }
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

        private int GetResource(ResourceType type)
        {
            return type switch
            {
                ResourceType.Coin => _coins,
                ResourceType.Bomb => _bombs,
                ResourceType.Key => _keys,
                _ => 0,
            };
        }

        private int GetResourceMax(ResourceType type)
        {
            return type switch
            {
                ResourceType.Coin => 999,
                ResourceType.Bomb => MaxBombs,
                ResourceType.Key => MaxKeys,
                _ => 999,
            };
        }

        private void AddResource(ResourceType type, int amount)
        {
            var clamped = Math.Clamp(GetResource(type) + amount, 0, GetResourceMax(type));
            switch (type)
            {
                case ResourceType.Coin:
                    _coins = clamped;
                    break;
                case ResourceType.Bomb:
                    _bombs = clamped;
                    break;
                case ResourceType.Key:
                    _keys = clamped;
                    break;
            }
        }

        private bool TryAddResource(ResourceType type, int amount)
        {
            amount = Math.Max(0, amount);
            if (amount == 0)
                return false;

            if (GetResource(type) + amount > GetResourceMax(type))
                return false;

            AddResource(type, amount);
            return true;
        }

        private bool TryAddHealth(int amount)
        {
            amount = Math.Max(0, amount);
            if (amount == 0)
                return false;

            if (PlayerHp + amount > MaxPlayerHp)
                return false;

            SetPlayerHealth(PlayerHp + amount, MaxPlayerHp);
            return true;
        }

        private bool TryConsumeResource(ResourceType type, int amount)
        {
            amount = Math.Max(0, amount);
            if (GetResource(type) < amount)
                return false;

            AddResource(type, -amount);
            return true;
        }

        private void TickDoorAnimations()
        {
            if (CurrentRoom.DoorTransitionTicks <= 0)
            {
                CurrentRoom.DoorVisualOpen = CurrentRoom.DoorTargetOpen;
                return;
            }

            CurrentRoom.DoorTransitionTicks--;
            if (CurrentRoom.DoorTransitionTicks <= 0)
                CurrentRoom.DoorVisualOpen = CurrentRoom.DoorTargetOpen;
        }

        private void HandleRoomState()
        {
            var room = CurrentRoom;
            _roomsEnteredCounter++;
            RevealConnectedRooms(RoomIndex);

            if (room.Type != RoomType.Boss)
            {
                _floorExitSpawned = false;
                _floorExitPosition = null;
            }

            var hasEnemies = room.Enemies.Any(enemy => enemy.Hp > 0);
            if (!(room.IsSecret && !room.Cleared))
                SetDoorState(room, !hasEnemies);

            if (room.Cleared)
                return;

            if (hasEnemies)
                return;

            room.Cleared = true;

            if (!room.ClearRewardsSpawned)
            {
                room.ClearRewardsSpawned = true;
                SpawnRoomClearRewards(room);
            }

            if (!room.DoorTargetOpen)
                SetDoorState(room, true);

            if (room.Type == RoomType.Boss)
            {
                _floorExitSpawned = true;
                _floorExitPosition = GetRoomCenter();
            }
        }

        private void SpawnRoomClearRewards(RoomData room)
        {
            if (room.Type is RoomType.Treasure or RoomType.Shop)
                return;

            var coins = _random.Next(RoomClearCoinMin, RoomClearCoinMax + 1);
            for (var i = 0; i < coins; i++)
            {
                SpawnPickup(room, PickupType.Coin, 1, GetRoomCenter() + new Vector2((_random.NextSingle() - 0.5f) * 1.2f, (_random.NextSingle() - 0.5f) * 1.2f), PickupSpawnAnimationDuration);
            }

            if (_random.NextDouble() > 0.35)
                return;

            var rewardType = _random.Next(3) switch
            {
                0 => PickupType.Coin,
                1 => PickupType.Heart,
                _ => PickupType.Bomb,
            };

            SpawnPickup(room, rewardType, 1, GetRoomCenter() + new Vector2(0f, -1f), PickupSpawnAnimationDuration);
        }

        private static void SpawnPickup(RoomData room, PickupType type, int amount, Vector2 position, float spawnDelay)
        {
            room.Pickups.Add(new PickupData(type, amount, position, spawnDelay));
        }

        private void ResolvePickupCollisions(float dt)
        {
            if (CurrentRoom.Pickups.Count == 0)
                return;

            var pushStep = MathF.Max(0f, dt) * PickupPushStrength;
            for (var i = 0; i < CurrentRoom.Pickups.Count; i++)
            {
                var pickup = CurrentRoom.Pickups[i];
                var offset = Vector2.Zero;

                offset += ComputePushOffset(pickup.Position, PickupCollisionRadius, _playerPos, _playerProto.Radius);

                foreach (var enemy in CurrentRoom.Enemies)
                {
                    if (enemy.Hp <= 0)
                        continue;

                    offset += ComputePushOffset(pickup.Position, PickupCollisionRadius, enemy.Position, enemy.Prototype.Radius);
                }

                foreach (var familiar in _familiars)
                {
                    offset += ComputePushOffset(pickup.Position, PickupCollisionRadius, familiar.Position, FamiliarCollisionRadius);
                }

                if (offset == Vector2.Zero)
                    continue;

                pickup.Position = ResolveCircleTileCollision(pickup.Position + offset * pushStep, PickupCollisionRadius, CurrentRoom);
            }
        }

        private void ResolveDynamicEntitySeparation(float dt)
        {
            var step = Math.Clamp(dt * 10f, 0f, 1f);

            foreach (var enemy in CurrentRoom.Enemies)
            {
                if (enemy.Hp <= 0)
                    continue;

                var push = ComputePushOffset(_playerPos, _playerProto.Radius + EntitySeparationBias, enemy.Position, enemy.Prototype.Radius + EntitySeparationBias);
                if (push != Vector2.Zero)
                    _playerPos = ResolveCircleTileCollision(_playerPos + push * step, _playerProto.Radius, CurrentRoom);
            }

            foreach (var familiar in _familiars)
            {
                var push = ComputePushOffset(_playerPos, _playerProto.Radius + EntitySeparationBias, familiar.Position, FamiliarCollisionRadius + EntitySeparationBias);
                if (push != Vector2.Zero)
                    _playerPos = ResolveCircleTileCollision(_playerPos + push * step, _playerProto.Radius, CurrentRoom);
            }
        }

        private static Vector2 ComputePushOffset(Vector2 leftPos, float leftRadius, Vector2 rightPos, float rightRadius)
        {
            var delta = leftPos - rightPos;
            var distance = delta.Length();
            var minDistance = leftRadius + rightRadius;

            if (distance <= 0.0001f)
            {
                delta = new Vector2(1f, 0f);
                distance = 1f;
            }

            if (distance >= minDistance)
                return Vector2.Zero;

            return delta / distance * (minDistance - distance);
        }

        private void SetDoorState(RoomData room, bool open)
        {
            if (room.DoorTargetOpen == open)
                return;

            room.DoorTargetOpen = open;
            var doorPrototype = GetDoorPrototype(room.Type);
            var animationState = open
                ? doorPrototype.OpeningState ?? doorPrototype.OpenState
                : doorPrototype.ClosingState ?? doorPrototype.ClosedState;
            var animationDuration = GetRsiAnimationDuration(doorPrototype.SpritePath, animationState);
            var transitionDuration = MathF.Max(MathF.Max(0.01f, doorPrototype.TransitionDuration), animationDuration);
            room.DoorTransitionTotalTicks = Math.Max(1, (int) MathF.Ceiling(transitionDuration / TickSeconds));
            room.DoorTransitionTicks = room.DoorTransitionTotalTicks;
        }

        private float GetRsiAnimationDuration(string? spritePath, string? stateName)
        {
            if (string.IsNullOrWhiteSpace(spritePath) || string.IsNullOrWhiteSpace(stateName))
                return 0f;

            if (!_resourceCache.TryGetResource<RSIResource>(new ResPath(spritePath), out var resource) ||
                !resource.RSI.TryGetState(new RSI.StateId(stateName), out var state))
                return 0f;

            var delays = state.GetDelays();
            if (delays.Length == 0)
                return 0f;

            var duration = 0f;
            foreach (var t in delays)
            {
                duration += MathF.Max(0.001f, t);
            }

            return duration;
        }

        private void TryRoomTransition()
        {
            var room = CurrentRoom;
            _roomsEnteredCounter++;
            RevealConnectedRooms(RoomIndex);

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

            var playerHitbox = GetEntityHitbox(_playerProto, _playerPos);
            var hitboxHalfExtents = playerHitbox.Shape == DeepMaintenanceHitboxShape.Rectangle
                ? playerHitbox.HalfExtents
                : new Vector2(playerHitbox.Radius, playerHitbox.Radius);

            var leftEdge = playerHitbox.Center.X - hitboxHalfExtents.X;
            var rightEdge = playerHitbox.Center.X + hitboxHalfExtents.X;
            var topEdge = playerHitbox.Center.Y - hitboxHalfExtents.Y;
            var bottomEdge = playerHitbox.Center.Y + hitboxHalfExtents.Y;

            if (leftEdge < DoorTransitionMargin && room.Neighbors.TryGetValue(new Vector2i(-1, 0), out var left) && (!CurrentRoom.Neighbors.TryGetValue(new Vector2i(-1, 0), out var ln) || !_rooms[ln].IsSecret || _rooms[ln].Cleared))
            {
                HandleRoomCompletionTransition(room);
                EnterRoom(left, false);
                _playerPos = _playerPos with { X = GridWidth - 1f - hitboxHalfExtents.X - _playerProto.HitboxOffsetX };
                return;
            }

            if (rightEdge > GridWidth - DoorTransitionMargin && room.Neighbors.TryGetValue(new Vector2i(1, 0), out var right) && (!CurrentRoom.Neighbors.TryGetValue(new Vector2i(1, 0), out var rn) || !_rooms[rn].IsSecret || _rooms[rn].Cleared))
            {
                HandleRoomCompletionTransition(room);
                EnterRoom(right, false);
                _playerPos = _playerPos with { X = 1f + hitboxHalfExtents.X - _playerProto.HitboxOffsetX };
                return;
            }

            if (topEdge < DoorTransitionMargin && room.Neighbors.TryGetValue(new Vector2i(0, -1), out var up) && (!CurrentRoom.Neighbors.TryGetValue(new Vector2i(0, -1), out var un) || !_rooms[un].IsSecret || _rooms[un].Cleared))
            {
                HandleRoomCompletionTransition(room);
                EnterRoom(up, false);
                _playerPos = _playerPos with { Y = GridHeight - 1f - hitboxHalfExtents.Y - _playerProto.HitboxOffsetY };
                return;
            }

            if (bottomEdge > GridHeight - DoorTransitionMargin && room.Neighbors.TryGetValue(new Vector2i(0, 1), out var down) && (!CurrentRoom.Neighbors.TryGetValue(new Vector2i(0, 1), out var dn) || !_rooms[dn].IsSecret || _rooms[dn].Cleared))
            {
                HandleRoomCompletionTransition(room);
                EnterRoom(down, false);
                _playerPos = _playerPos with { Y = 1f + hitboxHalfExtents.Y - _playerProto.HitboxOffsetY };
            }
        }

        private void EnterRoom(int roomIndex, bool centerPlayer)
        {
            RoomIndex = roomIndex;
            _visitedRooms.Add(roomIndex);
            _knownRooms.Add(roomIndex);
            _playerProjectiles.Clear();
            _enemyProjectiles.Clear();
            _playerVelocity = Vector2.Zero;
            _tookDamageInRoom = false;
            _electroRakRoomFireRateBonus = 0f;
            _electroRakDamageTriggersInRoom = 0;
            _roomKillDamageBonus = 0f;
            _claymoreCharging = false;
            _claymoreChargeTimer = 0f;

            if (centerPlayer)
                _playerPos = new Vector2(GridWidth * 0.5f, GridHeight * 0.5f);

            var room = CurrentRoom;
            _roomsEnteredCounter++;
            RevealConnectedRooms(RoomIndex);

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

            UpdateRoomLighting(room);
        }

        private void UpdateRoomLighting(RoomData room)
        {
            var baseLight = _activeFloorConfig?.BaseLight ?? 0.72f;
            baseLight = room.Type switch
            {
                RoomType.Boss => _activeFloorConfig?.BossRoomBaseLight ?? MathF.Min(baseLight, 0.45f),
                RoomType.Shop => _activeFloorConfig?.ShopRoomBaseLight ?? MathF.Max(baseLight, 0.82f),
                _ => baseLight,
            };

            _roomBaseLight = Math.Clamp(baseLight, 0.05f, 1f);
            _roomVignetteStrength = Math.Clamp(_activeFloorConfig?.VignetteStrength ?? 0.2f, 0f, 0.8f);
            _playerLightRadius = Math.Clamp(_activeFloorConfig?.PlayerLightRadius ?? 3.8f, 1.2f, 8f);
            _playerLightStrength = Math.Clamp(_activeFloorConfig?.PlayerLightStrength ?? 0.5f, 0f, 1f);
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

            var bossIndex = SelectBossRoomIndex(positions);
            var specialTaken = new HashSet<int> { 0, bossIndex };
            var treasureIndex = SelectSpecialRoomIndex(positions, specialTaken, _random);
            if (treasureIndex >= 0)
                specialTaken.Add(treasureIndex);
            var shopIndex = roomCount > 4 ? SelectSpecialRoomIndex(positions, specialTaken, _random) : -1;

            for (var i = 0; i < roomCount; i++)
            {
                var type = i switch
                {
                    0 => RoomType.Start,
                    _ when i == treasureIndex => RoomType.Treasure,
                    _ when i == shopIndex => RoomType.Shop,
                    _ when i == bossIndex => RoomType.Boss,
                    _ => RoomType.Normal,
                };

                var room = new RoomData(type, positions[i], BuildTileMap(type));
                SpawnEnemies(room);

                if (room.Type == RoomType.Shop)
                    PopulateShop(room);

                room.DoorTargetOpen = room.Enemies.All(enemy => enemy.Hp <= 0);
                room.DoorVisualOpen = room.DoorTargetOpen;
                _rooms.Add(room);
            }

            foreach (var room in _rooms)
            {
                foreach (var direction in CardinalDirections())
                {
                    if (!indexByPos.TryGetValue(room.MapPosition + direction, out var neighborIndex))
                        continue;

                    switch (room.Type)
                    {
                        case RoomType.Start when _rooms[neighborIndex].Type == RoomType.Boss:
                        case RoomType.Boss when _rooms[neighborIndex].Type == RoomType.Start:
                            continue;
                        default:
                            room.Neighbors[direction] = neighborIndex;
                            AddDoorway(room, direction);
                            break;
                    }
                }
            }

            TryAddSecretRoom(indexByPos, positions);
            EnsureBossRoomSingleConnection();
            EnsureBossReachabilityFromStart();

            foreach (var room in _rooms)
            {
                if (room.Type == RoomType.Start)
                {
                    room.Cleared = true;
                    room.DoorTargetOpen = true;
                    room.DoorVisualOpen = true;
                }
            }
        }

        private static int SelectBossRoomIndex(List<Vector2i> positions)
        {
            var bestIndex = 1;
            var bestDistance = 0;
            for (var i = 1; i < positions.Count; i++)
            {
                var pos = positions[i];
                var distance = Math.Abs(pos.X) + Math.Abs(pos.Y);
                if (distance <= bestDistance)
                    continue;

                bestDistance = distance;
                bestIndex = i;
            }

            return bestIndex;
        }

        private static int SelectSpecialRoomIndex(List<Vector2i> positions, HashSet<int> excluded, Random random)
        {
            var candidates = new List<(int Index, float Weight)>();
            var totalWeight = 0f;

            for (var i = 1; i < positions.Count; i++)
            {
                if (excluded.Contains(i))
                    continue;

                var pos = positions[i];
                var distance = Math.Abs(pos.X) + Math.Abs(pos.Y);
                var weight = 1f + distance * 0.35f;
                candidates.Add((i, weight));
                totalWeight += weight;
            }

            if (candidates.Count == 0)
                return -1;

            var roll = random.NextSingle() * MathF.Max(0.001f, totalWeight);
            foreach (var candidate in candidates)
            {
                roll -= candidate.Weight;
                if (roll > 0f)
                    continue;

                return candidate.Index;
            }

            return candidates[^1].Index;
        }

        private void EnsureBossReachabilityFromStart()
        {
            var bossIndex = _rooms.FindIndex(room => room.Type == RoomType.Boss);
            if (bossIndex <= 0)
                return;

            foreach (var direction in CardinalDirections().ToArray())
            {
                if (!_rooms[0].Neighbors.TryGetValue(direction, out var neighborIndex) || neighborIndex != bossIndex)
                    continue;

                _rooms[0].Neighbors.Remove(direction);
                _rooms[0].Tiles = SetDoorTile(_rooms[0].Tiles, direction, TileType.Wall);
                var reverse = new Vector2i(-direction.X, -direction.Y);
                _rooms[bossIndex].Neighbors.Remove(reverse);
                _rooms[bossIndex].Tiles = SetDoorTile(_rooms[bossIndex].Tiles, reverse, TileType.Wall);
            }

            if (_rooms[bossIndex].Neighbors.Values.Any(index => index != 0))
                return;

            var neighborEntry = _rooms
                .Select((room, index) => (room, index))
                .FirstOrDefault(entry => entry.index != 0 && entry.index != bossIndex && entry.room.Type != RoomType.Secret &&
                                         (Math.Abs(entry.room.MapPosition.X - _rooms[bossIndex].MapPosition.X) +
                                          Math.Abs(entry.room.MapPosition.Y - _rooms[bossIndex].MapPosition.Y) == 1));

            if (neighborEntry.room == null)
                return;

            var delta = _rooms[bossIndex].MapPosition - neighborEntry.room.MapPosition;
            var directionToBoss = new Vector2i(Math.Sign(delta.X), Math.Sign(delta.Y));
            var reverseDir = new Vector2i(-directionToBoss.X, -directionToBoss.Y);
            _rooms[neighborEntry.index].Neighbors[directionToBoss] = bossIndex;
            _rooms[bossIndex].Neighbors[reverseDir] = neighborEntry.index;
            AddDoorway(_rooms[neighborEntry.index], directionToBoss);
            AddDoorway(_rooms[bossIndex], reverseDir);
        }

        #endregion
    }
}
