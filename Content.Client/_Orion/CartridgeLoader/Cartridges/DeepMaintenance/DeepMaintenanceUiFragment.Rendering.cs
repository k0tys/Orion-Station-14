using System.Linq;
using System.Numerics;
using Content.Shared._Orion.CartridgeLoader.Cartridges.DeepMaintenance;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Shared.Graphics.RSI;
using Robust.Shared.Utility;

namespace Content.Client._Orion.CartridgeLoader.Cartridges.DeepMaintenance;

public sealed partial class DeepMaintenanceUiFragment
{
    private sealed partial class DeepMaintenanceGameControl
    {
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
                        TileType.Mushroom => _mushroomProto,
                        _ => _floorProto,
                    };

                    var box = UIBox2.FromDimensions(
                        mapOffset + new Vector2(x * tilePixel, y * tilePixel),
                        new Vector2(tilePixel, tilePixel));

                    var tileType = CurrentRoom.Tiles[x, y];
                    switch (tileType)
                    {
                        case TileType.Mushroom:
                        {
                            if (GetSprite(_floorProto.SpritePath, _floorProto.SpriteState) is { } floorTexture)
                                handle.DrawTextureRect(floorTexture, box);
                            else
                                handle.DrawRect(box, Color.DimGray);

                            if (GetSprite(_mushroomProto.SpritePath, _mushroomProto.SpriteState) is { } mushroomTexture)
                                handle.DrawTextureRect(mushroomTexture, box);
                            break;
                        }
                        case TileType.Wall or TileType.Obstacle:
                            DrawConnectedWallTile(handle, box, x, y, tileProto);
                            break;
                        default:
                        {
                            if (GetSprite(tileProto.SpritePath, tileProto.SpriteState) is { } texture)
                            {
                                handle.DrawTextureRect(texture, box);
                            }
                            else
                            {
                                handle.DrawRect(box, Color.DimGray);
                            }

                            break;
                        }
                    }

                    if (CurrentRoom.Tiles[x, y] == TileType.Door)
                    {
                        DrawDoor(handle, box, CurrentRoom.DoorVisualOpen, x, y);
                    }
                }
            }

            var tickAlpha = Math.Clamp(_accumulator / TickSeconds, 0f, 1f);

            DrawTreasureObjects(handle, tilePixel, mapOffset);

            foreach (var trail in _bloodTrails)
            {
                var center = mapOffset + trail.Position * tilePixel;
                var size = trail.Radius * tilePixel * 2f;
                var boxTrail = UIBox2.FromDimensions(center - new Vector2(size * 0.5f, size * 0.5f), new Vector2(size, size));
                handle.DrawRect(boxTrail, new Color(140, 30, 30, 120));
            }

            foreach (var enemy in CurrentRoom.Enemies.Where(enemy => enemy.Hp > 0))
            {
                var drawPos = Vector2.Lerp(enemy.PreviousPosition, enemy.Position, tickAlpha);
                DrawShadow(handle, drawPos + new Vector2(0f, 0.12f), tilePixel, mapOffset, MathF.Max(0.24f, enemy.Prototype.Radius * 1.08f), 0.42f);
                var color = GetEnemyVisualTint(enemy);
                DrawCharacter(handle, drawPos, enemy.Prototype, enemy.BodyFacing, enemy.ShootFacing, tilePixel, mapOffset, color, color, false);
            }

            DrawShadow(handle, _playerPos + new Vector2(0f, 0.12f), tilePixel, mapOffset, MathF.Max(0.22f, _playerProto.Radius * 3.2f), 0.45f);
            DrawPlayer(handle, tilePixel * GetPlayerVisualScale(), mapOffset);
            DrawBombs(handle, tilePixel, mapOffset);

            foreach (var familiar in _familiars)
            {
                var bob = MathF.Sin(_animationClock * 6f + familiar.BobOffset) * 0.05f;
                var drawPos = familiar.Position + new Vector2(0f, bob);
                DrawShadow(handle, familiar.Position + new Vector2(0f, 0.1f), tilePixel, mapOffset, 0.24f, 0.34f);
                var centerF = mapOffset + drawPos * tilePixel;
                var sizeF = tilePixel * 0.36f;
                var boxF = UIBox2.FromDimensions(centerF - new Vector2(sizeF * 0.5f, sizeF * 0.5f), new Vector2(sizeF, sizeF));
                handle.DrawRect(boxF, familiar.Config.FixedRedTint ? new Color(220, 80, 80) : new Color(190, 190, 255));
            }

            foreach (var projectile in _playerProjectiles)
            {
                var groundPos = Vector2.Lerp(projectile.PreviousGroundPosition, projectile.GroundPosition, tickAlpha);
                var height = projectile.PreviousHeight + (projectile.Height - projectile.PreviousHeight) * tickAlpha;
                var shadowScale = MathF.Max(0.12f, projectile.Radius * 0.7f * (1f - height * projectile.ShadowScaleByHeight));
                DrawShadow(handle, groundPos + new Vector2(0f, 0.08f), tilePixel, mapOffset, shadowScale, 0.12f);
                DrawProjectile(handle, groundPos, height, projectile, tilePixel, mapOffset);
            }

            foreach (var projectile in _enemyProjectiles)
            {
                var groundPos = Vector2.Lerp(projectile.PreviousGroundPosition, projectile.GroundPosition, tickAlpha);
                var height = projectile.PreviousHeight + (projectile.Height - projectile.PreviousHeight) * tickAlpha;
                var shadowScale = MathF.Max(0.12f, projectile.Radius * 0.7f * (1f - height * projectile.ShadowScaleByHeight));
                DrawShadow(handle, groundPos + new Vector2(0f, 0.08f), tilePixel, mapOffset, shadowScale, 0.12f);
                DrawProjectile(handle, groundPos, height, projectile, tilePixel, mapOffset);
            }

            DrawPickups(handle, tilePixel, mapOffset);
            DrawBombExplosions(handle, tilePixel, mapOffset);
            DrawFloorExit(handle, tilePixel, mapOffset);
            DrawShopSlots(handle, tilePixel, mapOffset);
            DrawLightingOverlay(handle, tilePixel, mapOffset);
            DrawRoomPanelShading(handle, tilePixel, mapOffset);

            if (_debugHitboxes)
                DrawDebugHitboxes(handle, tilePixel, mapOffset, tickAlpha);

            DrawHealthHearts(handle);
            DrawBuffIcons(handle);
            DrawHoveredItemTooltip(handle, tilePixel, mapOffset);
            DrawBossHealthBar(handle);
            DrawMinimap(handle);
        }

        private static Color GetEnemyVisualTint(EnemyData enemy)
        {
            var tint = Color.White;
            foreach (var effect in enemy.VisualEffects)
            {
                tint = effect.Tint;
            }

            if (enemy.DamageFlash > 0f)
                tint = Color.IndianRed;

            return tint;
        }

        private void DrawPlayer(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset)
        {
            var color = _playerDamageFlash > 0f ? Color.IndianRed : Color.White;

            if (_emoteTimer > 0f)
            {
                var emoteState = _playerProto.EmoteSpriteState ?? EmotePlaceholderState;
                DrawDirectionalEntityLayer(handle, _playerPos, _playerProto.SpritePath, emoteState, _playerShootFacing, tilePixel, _playerProto.SpriteScale, mapOffset, color);
                return;
            }

            var bodyTint = color;
            var headTint = color;
            foreach (var relic in _activeRelics)
            {
                if (relic.BodyTintColor is { } bodyColor)
                    bodyTint = bodyColor;

                if (relic.HeadTintColor is { } headColor)
                    headTint = headColor;
            }

            DrawCharacter(handle, _playerPos, _playerProto, _playerBodyFacing, _playerShootFacing, tilePixel, mapOffset, bodyTint, headTint, _playerShootAnimationTimer > 0f);

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
            DrawClaymoreAnimation(handle, tilePixel, mapOffset);
        }

        private void DrawClaymoreAnimation(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset)
        {
            if (!HasClaymoreRelic())
                return;

            const string path = "/Textures/Effects/arcs.rsi";
            var state = _claymoreReflectTimer > 0f
                ? "disarm"
                : _claymoreReleaseTimer > 0f
                    ? "disarm"
                    : _claymoreCharging
                        ? "disarm"
                        : string.Empty;

            if (string.IsNullOrEmpty(state))
                return;

            DrawDirectionalEntityLayer(handle, _playerPos + new Vector2(0f, -0.1f), path, state, _playerShootFacing, tilePixel, 1.1f, mapOffset, Color.White.WithAlpha(0.85f));
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
            if (!TryResolveMeleeArcState(meleeRelic, out var state))
                return;

            DrawDirectionalEntityLayer(handle, pos, meleeRelic.MeleeArcSpritePath!, state, _meleeSwingFacing, tilePixel, 1f, mapOffset, Color.White);
        }

        private bool TryResolveMeleeArcState(DeepMaintenanceRelicPrototype meleeRelic, out string state)
        {
            state = meleeRelic.MeleeArcSpriteState!;
            if (!meleeRelic.MeleeArcAnimated)
                return true;

            var phase = 1f - (_meleeSwingTimer / MathF.Max(0.001f, MeleeSwingDuration));
            var frame = (int) (phase * MathF.Max(1f, meleeRelic.MeleeArcAnimationFps));
            var animatedState = $"{state}{frame}";
            if (GetSprite(meleeRelic.MeleeArcSpritePath, animatedState) != null)
            {
                state = animatedState;
                return true;
            }

            return GetSprite(meleeRelic.MeleeArcSpritePath, state) != null;
        }

        private void DrawCharacter(DrawingHandleScreen handle, Vector2 pos, DeepMaintenanceEntityPrototype prototype, FacingDirection bodyFacing, FacingDirection shootFacing, float tilePixel, Vector2 mapOffset, Color bodyColor, Color headColor, bool shootAnimation)
        {
            var bodyState = prototype.BodySpriteState ?? prototype.SpriteState;
            var shootState = prototype.ShootSpriteState;
            var hasHeadLayer = !string.IsNullOrWhiteSpace(prototype.HeadSpriteState);

            if (shootAnimation && !hasHeadLayer && !string.IsNullOrWhiteSpace(shootState))
                bodyState = shootState;

            DrawDirectionalEntityLayer(handle, pos, prototype.SpritePath, bodyState, bodyFacing, tilePixel, prototype.SpriteScale, mapOffset, bodyColor);

            var headState = prototype.HeadSpriteState;
            if (!string.IsNullOrWhiteSpace(shootState) && shootAnimation)
                headState = shootState;

            if (string.IsNullOrWhiteSpace(headState) || headState == bodyState)
                return;

            DrawDirectionalEntityLayer(handle, pos, prototype.SpritePath, headState, shootFacing, tilePixel, prototype.SpriteScale, mapOffset, headColor);
        }

        private void DrawDirectionalEntityLayer(DrawingHandleScreen handle, Vector2 pos, string spritePath, string spriteState, FacingDirection facing, float tilePixel, float spriteScale, Vector2 mapOffset, Color color)
        {
            var center = mapOffset + pos * tilePixel;
            var size = tilePixel * EntitySpriteTileSize * MathF.Max(0.05f, spriteScale);
            var box = UIBox2.FromDimensions(center - new Vector2(size * 0.5f, size * 0.5f), new Vector2(size, size));
            var animationProgress = _animationClock - MathF.Floor(_animationClock);

            if (TryGetDirectionalAnimatedSprite(spritePath, spriteState, facing, animationProgress, out var animatedTexture))
            {
                handle.DrawTextureRect(animatedTexture, box, color);
                return;
            }

            if (TryGetAnimatedSprite(spritePath, spriteState, animationProgress, out var nonDirectionalAnimatedTexture))
            {
                handle.DrawTextureRect(nonDirectionalAnimatedTexture, box, color);
                return;
            }

            if (GetDirectionalSprite(spritePath, spriteState, facing) is not { } texture)
            {
                if (GetSprite(spritePath, spriteState) is { } staticTexture)
                {
                    handle.DrawTextureRect(staticTexture, box, color);
                    return;
                }

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

        private void DrawAnimatedEntity(DrawingHandleScreen handle, Vector2 pos, string spritePath, string spriteState, float progress, float tilePixel, Vector2 mapOffset)
        {
            var center = mapOffset + pos * tilePixel;
            var size = tilePixel * EntitySpriteTileSize;
            var box = UIBox2.FromDimensions(center - new Vector2(size * 0.5f, size * 0.5f), new Vector2(size, size));

            if (TryGetAnimatedSprite(spritePath, spriteState, progress, out var texture))
            {
                handle.DrawTextureRect(texture, box);
                return;
            }

            DrawEntity(handle, pos, spritePath, spriteState, tilePixel, mapOffset);
        }

        private bool TryGetAnimatedSprite(string? spritePath, string? spriteState, float progress, out Texture texture)
        {
            texture = default!;
            if (string.IsNullOrWhiteSpace(spritePath) || string.IsNullOrWhiteSpace(spriteState))
                return false;

            if (!_resourceCache.TryGetResource<RSIResource>(new ResPath(spritePath), out var resource) ||
                !resource.RSI.TryGetState(new RSI.StateId(spriteState), out var state))
                return false;

            return TryGetDirectionalFrame(state, RsiDirection.South, progress, out texture) ||
                   TryGetDirectionalFrame(state, RsiDirection.North, progress, out texture) ||
                   TryGetDirectionalFrame(state, RsiDirection.East, progress, out texture) ||
                   TryGetDirectionalFrame(state, RsiDirection.West, progress, out texture);
        }

        private bool TryGetDirectionalAnimatedSprite(string? spritePath, string? spriteState, FacingDirection facing, float progress, out Texture texture)
        {
            texture = default!;
            if (string.IsNullOrWhiteSpace(spritePath) || string.IsNullOrWhiteSpace(spriteState))
                return false;

            var direction = FacingToRsiDirection(facing);
            if (!_resourceCache.TryGetResource<RSIResource>(new ResPath(spritePath), out var resource) ||
                !resource.RSI.TryGetState(new RSI.StateId(spriteState), out var state))
                return false;

            return TryGetDirectionalFrame(state, direction, progress, out texture) ||
                   TryGetDirectionalFrame(state, RsiDirection.South, progress, out texture) ||
                   TryGetDirectionalFrame(state, RsiDirection.North, progress, out texture) ||
                   TryGetDirectionalFrame(state, RsiDirection.East, progress, out texture) ||
                   TryGetDirectionalFrame(state, RsiDirection.West, progress, out texture);
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
            var progress = GetDoorAnimationProgress();
            if (TryGetDirectionalAnimatedSprite(doorPrototype.SpritePath, state, direction, progress, out var texture))
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

        private float GetDoorAnimationProgress()
        {
            if (CurrentRoom.DoorTransitionTicks <= 0 || CurrentRoom.DoorTransitionTotalTicks <= 0)
                return 1f;

            var elapsed = CurrentRoom.DoorTransitionTotalTicks - CurrentRoom.DoorTransitionTicks;
            return Math.Clamp((elapsed + 1f) / CurrentRoom.DoorTransitionTotalTicks, 0f, 1f);
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
                RoomType.Shop => 2,
                RoomType.Devil => 2,
                RoomType.Angel => 2,
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
            return _doorPrototypes.TryGetValue(roomType, out var doorPrototype)
                ? doorPrototype
                : _doorPrototypes[RoomType.Normal];
        }

        private void DrawProjectile(DrawingHandleScreen handle, Vector2 groundPos, float height, ProjectileData projectile, float tilePixel, Vector2 mapOffset)
        {
            var center = mapOffset + (groundPos + GetProjectileVisualOffset(projectile, height)) * tilePixel;
            var facing = FacingFromVector(projectile.Direction == Vector2.Zero ? projectile.PlanarVelocity : projectile.Direction, FacingDirection.Down);

            var texture = GetDirectionalSprite(projectile.SpritePath, projectile.SpriteState, facing) ?? GetSprite(projectile.SpritePath, projectile.SpriteState);
            if (texture == null)
                return;

            var size = new Vector2(
                tilePixel * texture.Width / DefaultSpritePixelsPerTile,
                tilePixel * texture.Height / DefaultSpritePixelsPerTile) * MathF.Max(0.05f, projectile.SpriteScale);
            var box = UIBox2.FromDimensions(center - size * 0.5f, size);

            handle.DrawTextureRect(texture, box, projectile.Tint);
        }

        private static Vector2 GetProjectileVisualOffset(ProjectileData projectile, float height)
        {
            return new Vector2(0f, -MathF.Max(0f, height) * projectile.SpriteLiftMultiplier);
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
            if (!_hasTreasurePrototype)
                return;

            if (_treasureBoxPosition is { } boxPos)
            {
                if (_treasureOpeningAnimation || _treasureOpenAnimationTimer > 0f)
                {
                    var progress = _treasurePrototype.OpenAnimationDuration <= 0f
                        ? 1f
                        : 1f - Math.Clamp(_treasureOpenAnimationTimer / _treasurePrototype.OpenAnimationDuration, 0f, 1f);
                    DrawAnimatedEntity(handle, boxPos, _treasurePrototype.ClosedCrateSpritePath, TreasureOpeningState, progress, tilePixel, mapOffset);
                }
                else
                {
                    var state = _treasureBoxOpened ? _treasurePrototype.OpenCrateSpriteState : _treasurePrototype.ClosedCrateSpriteState;
                    DrawEntity(handle, boxPos, _treasurePrototype.ClosedCrateSpritePath, state, tilePixel, mapOffset);
                }
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

        private void DrawHoveredItemTooltip(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset)
        {
            if (!TryBuildTooltipText(tilePixel, mapOffset, out var text))
                return;

            var dimensions = handle.GetDimensions(_tooltipFont, text, 1f);
            var padding = new Vector2(8f, 6f);
            var pos = _lastMousePosition + new Vector2(14f, 14f);
            var size = dimensions + padding * 2f;

            if (pos.X + size.X > PixelSize.X - 4f)
                pos.X = MathF.Max(4f, PixelSize.X - size.X - 4f);
            if (pos.Y + size.Y > PixelSize.Y - 4f)
                pos.Y = MathF.Max(4f, PixelSize.Y - size.Y - 4f);

            var box = UIBox2.FromDimensions(pos, size);
            handle.DrawRect(box, new Color(10, 10, 14, 215));
            var border = new Color(244, 225, 171, 220);
            handle.DrawLine(box.TopLeft, new Vector2(box.Right, box.Top), border);
            handle.DrawLine(new Vector2(box.Right, box.Top), box.BottomRight, border);
            handle.DrawLine(box.BottomRight, new Vector2(box.Left, box.Bottom), border);
            handle.DrawLine(new Vector2(box.Left, box.Bottom), box.TopLeft, border);
            handle.DrawString(_tooltipFont, pos + padding, text, Color.White);
        }

        private bool TryBuildTooltipText(float tilePixel, Vector2 mapOffset, out string text)
        {
            text = string.Empty;

            if (_activeCurse == CurseType.Blind)
            {
                text = "???";
                return true;
            }

            var worldPos = (_lastMousePosition - mapOffset) / MathF.Max(0.001f, tilePixel);

            if (CurrentRoom.Type == RoomType.Shop)
            {
                foreach (var slot in CurrentRoom.ShopSlots)
                {
                    if (slot.Sold || Vector2.Distance(worldPos, slot.Position) > 0.5f)
                        continue;

                    text = BuildShopTooltip(slot);
                    return !string.IsNullOrWhiteSpace(text);
                }
            }

            foreach (var pickup in CurrentRoom.Pickups)
            {
                if (Vector2.Distance(worldPos, pickup.Position) > 0.4f)
                    continue;

                text = BuildPickupTooltip(pickup);
                return !string.IsNullOrWhiteSpace(text);
            }

            if (_treasureRelicPosition is { } relicPos && Vector2.Distance(worldPos, relicPos) <= 0.45f && !string.IsNullOrWhiteSpace(_treasureRelicId) &&
                _prototype.TryIndex<DeepMaintenanceRelicPrototype>(_treasureRelicId, out var relic))
            {
                text = BuildRelicTooltip(relic, null);
                return true;
            }

            var hudIndex = (int) ((_lastMousePosition.X - 6f) / 28f);
            if (_lastMousePosition.Y >= 38f && _lastMousePosition.Y <= 62f && hudIndex >= 0 && hudIndex < _activeRelics.Count)
            {
                text = BuildRelicTooltip(_activeRelics[hudIndex], null);
                return true;
            }

            return false;
        }

        private static string BuildPickupTooltip(PickupData pickup)
        {
            return pickup.Type switch
            {
                PickupType.Coin => $"{Loc.GetString("deep-maintenance-tooltip-pickup-coin-name")}\n{Loc.GetString("deep-maintenance-tooltip-pickup-coin-desc")}",
                PickupType.Bomb => $"{Loc.GetString("deep-maintenance-tooltip-pickup-bomb-name")}\n{Loc.GetString("deep-maintenance-tooltip-pickup-bomb-desc")}",
                PickupType.Key => $"{Loc.GetString("deep-maintenance-tooltip-pickup-key-name")}\n{Loc.GetString("deep-maintenance-tooltip-pickup-key-desc")}",
                PickupType.Heart => $"{Loc.GetString("deep-maintenance-tooltip-pickup-heart-name")}\n{Loc.GetString("deep-maintenance-tooltip-pickup-heart-desc")}",
                _ => string.Empty,
            };
        }

        private string BuildShopTooltip(ShopSlotData slot)
        {
            var price = ApplyPriceModifiers(slot.Price);
            return slot.Item switch
            {
                ShopItemType.Relic when !string.IsNullOrWhiteSpace(slot.RelicId) && _prototype.TryIndex<DeepMaintenanceRelicPrototype>(slot.RelicId, out var relic)
                    => BuildRelicTooltip(relic, price),
                ShopItemType.Bomb => $"{Loc.GetString("deep-maintenance-tooltip-pickup-bomb-name")}\n{Loc.GetString("deep-maintenance-tooltip-pickup-bomb-desc")}\n{Loc.GetString("deep-maintenance-tooltip-price", ("price", price))}",
                ShopItemType.Heart => $"{Loc.GetString("deep-maintenance-tooltip-pickup-heart-name")}\n{Loc.GetString("deep-maintenance-tooltip-pickup-heart-desc")}\n{Loc.GetString("deep-maintenance-tooltip-price", ("price", price))}",
                ShopItemType.Key => $"{Loc.GetString("deep-maintenance-tooltip-pickup-key-name")}\n{Loc.GetString("deep-maintenance-tooltip-pickup-key-desc")}\n{Loc.GetString("deep-maintenance-tooltip-price", ("price", price))}",
                _ => string.Empty,
            };
        }

        private static string BuildRelicEffectsSummary(DeepMaintenanceRelicPrototype relic)
        {
            if (relic.Effects.Count == 0)
                return Loc.GetString("deep-maintenance-tooltip-relic-default-desc");

            var labels = relic.Effects
                .Select(effect => effect.Type)
                .Distinct()
                .Take(4)
                .Select(type => $"• {type}")
                .ToList();

            if (relic.Effects.Count > 4)
                labels.Add("• ...");

            return string.Join("\n", labels);
        }

        private static string BuildRelicTooltip(DeepMaintenanceRelicPrototype relic, int? price)
        {
            var nameKey = $"deep-maintenance-relic-{relic.ID.ToLowerInvariant()}-name";
            var descKey = $"deep-maintenance-relic-{relic.ID.ToLowerInvariant()}-desc";
            var name = Loc.TryGetString(nameKey, out var localizedName) ? localizedName : relic.ID;
            var desc = Loc.TryGetString(descKey, out var localizedDesc)
                ? localizedDesc
                : BuildRelicEffectsSummary(relic);
            var rarity = relic.BasePrice switch
            {
                <= 10 => Loc.GetString("deep-maintenance-tooltip-rarity-common"),
                <= 17 => Loc.GetString("deep-maintenance-tooltip-rarity-rare"),
                _ => Loc.GetString("deep-maintenance-tooltip-rarity-legendary"),
            };

            var lines = new List<string>
            {
                name,
                desc,
                Loc.GetString("deep-maintenance-tooltip-rarity", ("rarity", rarity)),
            };

            if (price.HasValue)
                lines.Add(Loc.GetString("deep-maintenance-tooltip-price", ("price", price.Value)));

            return string.Join("\n", lines);
        }

        private void DrawResourceCounters(DrawingHandleScreen handle)
        {
            var entries = new (DeepMaintenancePickupPrototype Proto, int Amount)[]
            {
                (_coinPickupProto, _coins),
                (_bombPickupProto, _bombs),
                (_keyPickupProto, _keys),
            };

            var iconSize = Math.Clamp(PixelSize.X / 30f, 14f, 22f);
            var x = 6f;
            var y = 6f;
            foreach (var (proto, amount) in entries)
            {
                var box = UIBox2.FromDimensions(new Vector2(x, y), new Vector2(iconSize, iconSize));
                if (GetSprite(proto.SpritePath, proto.SpriteState) is { } texture)
                    handle.DrawTextureRect(texture, box);
                else
                    handle.DrawRect(box, Color.White);

                var text = $"x{Math.Max(0, amount):00}";
                var textPos = new Vector2(x + iconSize + 3f, y + 1f);
                handle.DrawString(_shopPriceFont, textPos + new Vector2(1f, 1f), text, Color.Black.WithAlpha(0.8f));
                handle.DrawString(_shopPriceFont, textPos, text, Color.WhiteSmoke);
                y += iconSize + 2f;
            }
        }

        private void DrawHealthHearts(DrawingHandleScreen handle)
        {
            DrawResourceCounters(handle);

            var size = Math.Clamp(PixelSize.X / 18f, 14f, 24f);
            const float gap = 3f;
            const float rowGap = 4f;

            var maxHearts = (int) MathF.Ceiling(MaxPlayerHp / 2f);
            var heartsPerRow = Math.Max(1, Math.Min(maxHearts, (int) ((Math.Max(48f, PixelSize.X * 0.45f) + gap) / (size + gap))));
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
                var totalRowWidth = heartsPerRow * size + Math.Max(0, heartsPerRow - 1) * gap;
                var heartsStartX = Math.Max(6f, PixelSize.X - totalRowWidth - 8f);
                var basePos = new Vector2(heartsStartX + column * (size + gap), HeartRowsStartY + row * (size + rowGap));
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

        private DeepMaintenancePickupPrototype GetPickupPrototype(PickupData pickup)
        {
            return GetPickupPrototypeData(pickup);
        }

        private void DrawPickups(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset)
        {
            foreach (var pickup in CurrentRoom.Pickups)
            {
                var prototype = GetPickupPrototype(pickup);
                var center = mapOffset + pickup.Position * tilePixel;
                var appearProgress = pickup.SpawnDuration <= 0f
                    ? 1f
                    : 1f - Math.Clamp(pickup.SpawnTimer / pickup.SpawnDuration, 0f, 1f);
                var scale = 0.55f + (1f - 0.55f) * appearProgress;
                var pulse = 0.92f + MathF.Sin(_animationClock * 9f + pickup.Position.X + pickup.Position.Y) * 0.08f;
                var alpha = (0.35f + (1f - 0.35f) * appearProgress) * pulse;
                var size = tilePixel * 0.5f * MathF.Max(0.1f, prototype.SpriteScale) * scale;
                var box = UIBox2.FromDimensions(center - new Vector2(size * 0.5f, size * 0.5f), new Vector2(size, size));

                DrawShadow(handle, pickup.Position + new Vector2(0f, 0.08f), tilePixel, mapOffset, 0.22f, 0.14f);

                if (GetSprite(prototype.SpritePath, prototype.SpriteState) is { } texture)
                {
                    handle.DrawTextureRect(texture, box, Color.White.WithAlpha(Math.Clamp(alpha, 0.1f, 1f)));
                    continue;
                }

                handle.DrawRect(box, Color.White.WithAlpha(Math.Clamp(alpha, 0.1f, 1f)));
            }
        }

        private void DrawBombs(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset)
        {
            foreach (var bomb in _activeBombs)
            {
                var center = mapOffset + bomb.Position * tilePixel;
                var size = tilePixel * 0.58f * MathF.Max(0.1f, _bombPickupProto.SpriteScale);
                var box = UIBox2.FromDimensions(center - new Vector2(size * 0.5f, size * 0.5f), new Vector2(size, size));
                var progress = 1f - Math.Clamp(bomb.Timer / MathF.Max(0.05f, _bombPickupProto.BombFuseSeconds), 0f, 1f);
                DrawShadow(handle, bomb.Position + new Vector2(0f, 0.1f), tilePixel, mapOffset, 0.24f, 0.16f);

                if (TryGetAnimatedSprite(_bombPickupProto.SpritePath, BombPrimedState, progress, out var primed))
                {
                    handle.DrawTextureRect(primed, box);
                    continue;
                }

                if (GetSprite(_bombPickupProto.SpritePath, _bombPickupProto.SpriteState) is { } idle)
                {
                    handle.DrawTextureRect(idle, box);
                    continue;
                }

                handle.DrawRect(box, Color.DarkSlateGray);
            }
        }

        private static void DrawShadow(DrawingHandleScreen handle, Vector2 tilePosition, float tilePixel, Vector2 mapOffset, float radius, float alpha)
        {
            var center = mapOffset + tilePosition * tilePixel;
            var baseSize = MathF.Max(3f, radius * tilePixel * 2.15f);
            for (var layer = 0; layer < 4; layer++)
            {
                var scale = 1f - layer * 0.16f;
                var size = new Vector2(baseSize * scale, baseSize * (0.6f - layer * 0.05f) * scale);
                var box = UIBox2.FromDimensions(center - size * 0.5f, size);
                var layerAlpha = Math.Clamp(alpha * (0.68f - layer * 0.14f), 0f, 0.5f);
                handle.DrawRect(box, Color.Black.WithAlpha(layerAlpha));
            }
        }

        private void DrawLightingOverlay(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset)
        {
            var mapBox = UIBox2.FromDimensions(mapOffset, new Vector2(tilePixel * GridWidth, tilePixel * GridHeight));
            for (var y = 0; y < GridHeight; y++)
            {
                for (var x = 0; x < GridWidth; x++)
                {
                    var tileCenter = new Vector2(x + 0.5f, y + 0.5f);
                    var distanceToPlayer = Vector2.Distance(tileCenter, _playerPos);
                    var playerLight = Math.Clamp(1f - distanceToPlayer / MathF.Max(0.001f, _playerLightRadius), 0f, 1f) * _playerLightStrength;

                    var edgeX = MathF.Abs(tileCenter.X - GridWidth * 0.5f) / (GridWidth * 0.5f);
                    var edgeY = MathF.Abs(tileCenter.Y - GridHeight * 0.5f) / (GridHeight * 0.5f);
                    var edgeFactor = Math.Clamp(MathF.Max(edgeX, edgeY), 0f, 1f);

                    var darkness = (1f - _roomBaseLight) + _roomVignetteStrength * edgeFactor * edgeFactor - playerLight;
                    darkness = Math.Clamp(darkness, 0f, 0.94f);
                    if (darkness <= 0.001f)
                        continue;

                    var tileBox = UIBox2.FromDimensions(
                        mapOffset + new Vector2(x * tilePixel, y * tilePixel),
                        new Vector2(tilePixel, tilePixel));
                    handle.DrawRect(tileBox, Color.Black.WithAlpha(darkness));
                }
            }

            var vignetteInset = tilePixel * 0.2f;
            var border = UIBox2.FromDimensions(mapBox.TopLeft - new Vector2(vignetteInset, vignetteInset), mapBox.Size + new Vector2(vignetteInset * 2f, vignetteInset * 2f));
            var borderAlpha = Math.Clamp(_roomVignetteStrength * 0.4f, 0f, 0.3f);
            handle.DrawRect(UIBox2.FromDimensions(border.TopLeft, new Vector2(border.Width, vignetteInset)), Color.Black.WithAlpha(borderAlpha));
            handle.DrawRect(UIBox2.FromDimensions(new Vector2(border.Left, border.Bottom - vignetteInset), new Vector2(border.Width, vignetteInset)), Color.Black.WithAlpha(borderAlpha));
            handle.DrawRect(UIBox2.FromDimensions(new Vector2(border.Left, border.Top), new Vector2(vignetteInset, border.Height)), Color.Black.WithAlpha(borderAlpha));
            handle.DrawRect(UIBox2.FromDimensions(new Vector2(border.Right - vignetteInset, border.Top), new Vector2(vignetteInset, border.Height)), Color.Black.WithAlpha(borderAlpha));
        }

        private void DrawRoomPanelShading(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset)
        {
            var mapSize = new Vector2(tilePixel * GridWidth, tilePixel * GridHeight);
            var mapBox = UIBox2.FromDimensions(mapOffset, mapSize);
            var bandHeight = mapSize.Y / 5f;
            for (var i = 0; i < 5; i++)
            {
                var y = mapOffset.Y + i * bandHeight;
                var box = UIBox2.FromDimensions(new Vector2(mapOffset.X, y), new Vector2(mapSize.X, bandHeight));
                var alpha = (i % 2 == 0 ? 0.03f : 0.015f) + _roomVignetteStrength * 0.03f;
                handle.DrawRect(box, new Color(0, 0, 0, (byte) (255f * Math.Clamp(alpha, 0f, 0.14f))));
            }

            var lineColor = new Color(0, 0, 0, 80);
            for (var i = 1; i < 5; i++)
            {
                var y = mapOffset.Y + i * bandHeight;
                handle.DrawLine(new Vector2(mapBox.Left, y), new Vector2(mapBox.Right, y), lineColor.WithAlpha(0.24f));
            }
        }

        private void DrawShopSlots(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset)
        {
            if (CurrentRoom.Type != RoomType.Shop)
                return;

            foreach (var slot in CurrentRoom.ShopSlots)
            {
                var center = mapOffset + slot.Position * tilePixel;
                var frameSize = tilePixel * 0.9f;
                var box = UIBox2.FromDimensions(center - new Vector2(frameSize * 0.5f, frameSize * 0.5f), new Vector2(frameSize, frameSize));
                handle.DrawRect(box, new Color(35, 28, 18, 120));
                var border = new Color(235, 201, 90, 160);
                handle.DrawLine(box.TopLeft, new Vector2(box.Right, box.Top), border);
                handle.DrawLine(new Vector2(box.Right, box.Top), box.BottomRight, border);
                handle.DrawLine(box.BottomRight, new Vector2(box.Left, box.Bottom), border);
                handle.DrawLine(new Vector2(box.Left, box.Bottom), box.TopLeft, border);

                if (!slot.Sold)
                {
                    DrawShopSlotIcon(handle, slot, tilePixel, mapOffset);
                    DrawPriceTag(handle, ApplyPriceModifiers(slot.Price), center + new Vector2(0f, frameSize * 0.56f));
                }
            }
        }

        private void DrawShopSlotIcon(DrawingHandleScreen handle, ShopSlotData slot, float tilePixel, Vector2 mapOffset)
        {
            if (string.IsNullOrWhiteSpace(slot.SpritePath) || string.IsNullOrWhiteSpace(slot.SpriteState))
                return;

            if (GetSprite(slot.SpritePath, slot.SpriteState) is not { } texture)
                return;

            var center = mapOffset + slot.Position * tilePixel;
            var size = tilePixel * 0.5f * MathF.Max(0.2f, slot.SpriteScale);
            var box = UIBox2.FromDimensions(center - new Vector2(size * 0.5f, size * 0.5f), new Vector2(size, size));
            handle.DrawTextureRect(texture, box);
        }

        private void DrawPriceTag(DrawingHandleScreen handle, int price, Vector2 center)
        {
            var text = $"{Math.Max(0, price)} §";
            var dimensions = handle.GetDimensions(_shopPriceFont, text, 1f);
            var centered = center - dimensions / 2f;
            var shadowOffset = new Vector2(1f, 1f);
            handle.DrawString(_shopPriceFont, centered + shadowOffset, text, Color.Black.WithAlpha(0.8f));
            handle.DrawString(_shopPriceFont, centered, text, new Color(255, 216, 89));
        }

        private void DrawBombExplosions(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset)
        {
            foreach (var explosion in _bombExplosions)
            {
                var progress = 1f - Math.Clamp(explosion.Timer / explosion.Duration, 0f, 1f);
                var center = mapOffset + explosion.Position * tilePixel;
                var radius = tilePixel * 0.2f + (_bombPickupProto.BombExplosionRadius * tilePixel - tilePixel * 0.2f) * progress;
                var alpha = Math.Clamp(1f - progress, 0f, 1f);

                var outer = UIBox2.FromDimensions(center - new Vector2(radius, radius), new Vector2(radius * 2f, radius * 2f));
                var innerRadius = radius * 0.58f;
                var inner = UIBox2.FromDimensions(center - new Vector2(innerRadius, innerRadius), new Vector2(innerRadius * 2f, innerRadius * 2f));

                handle.DrawRect(outer, new Color(255, 196, 95, (byte) (120 * alpha)));
                handle.DrawRect(inner, new Color(255, 235, 160, (byte) (190 * alpha)));
            }
        }

        private void DrawEmote(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset)
        {
            if (_emoteTimer <= 0f)
                return;

            var center = mapOffset + (_playerPos + new Vector2(0f, -0.9f)) * tilePixel;
            var progress = 1f - Math.Clamp(_emoteTimer / MathF.Max(0.05f, _playerProto.EmoteDuration), 0f, 1f);
            var bob = MathF.Sin(progress * MathF.PI) * 0.12f;

            if (GetDirectionalSprite(_playerProto.SpritePath, EmotePlaceholderState, _playerShootFacing) is { } emoteTexture)
            {
                var size = new Vector2(tilePixel * emoteTexture.Width / DefaultSpritePixelsPerTile, tilePixel * emoteTexture.Height / DefaultSpritePixelsPerTile);
                var box = UIBox2.FromDimensions(center - new Vector2(size.X * 0.5f, size.Y) + new Vector2(0f, -bob * tilePixel), size);
                handle.DrawTextureRect(emoteTexture, box);
                return;
            }

            var radius = tilePixel * (0.11f + 0.07f * (1f - progress));
            var boxFallback = UIBox2.FromDimensions(center - new Vector2(radius, radius) + new Vector2(0f, -bob * tilePixel), new Vector2(radius * 2f, radius * 2f));
            handle.DrawRect(boxFallback, new Color(255, 244, 120, 220));
        }

        private void DrawBossHealthBar(DrawingHandleScreen handle)
        {
            if (CurrentRoom.Type != RoomType.Boss)
                return;

            var boss = CurrentRoom.Enemies.FirstOrDefault(enemy => enemy.Hp > 0 && enemy.Prototype.IsBoss);
            if (boss == null)
                return;

            var barWidth = Math.Clamp(PixelSize.X * 0.55f, 140f, 320f);
            const float barHeight = 11f;
            var topLeft = new Vector2((PixelSize.X - barWidth) * 0.5f, 7f);
            var bg = UIBox2.FromDimensions(topLeft, new Vector2(barWidth, barHeight));
            handle.DrawRect(bg, new Color(20, 8, 8, 220));

            var ratio = Math.Clamp((float) boss.Hp / Math.Max(1, boss.Prototype.MaxHp), 0f, 1f);
            var fill = UIBox2.FromDimensions(topLeft + new Vector2(1f, 1f), new Vector2((barWidth - 2f) * ratio, barHeight - 2f));
            handle.DrawRect(fill, new Color(210, 35, 45, 235));

            var border = new Color(245, 235, 195, 230);
            handle.DrawLine(bg.TopLeft, new Vector2(bg.Right, bg.Top), border);
            handle.DrawLine(new Vector2(bg.Right, bg.Top), bg.BottomRight, border);
            handle.DrawLine(bg.BottomRight, new Vector2(bg.Left, bg.Bottom), border);
            handle.DrawLine(new Vector2(bg.Left, bg.Bottom), bg.TopLeft, border);

            var nameBox = UIBox2.FromDimensions(new Vector2(topLeft.X, topLeft.Y + barHeight + 1f), new Vector2(barWidth, 8f));
            handle.DrawRect(nameBox, new Color(12, 12, 12, 120));

            var glyphCount = Math.Clamp(GetBossDisplayName(boss.Prototype.ID).Length, 4, 24);
            var pipWidth = barWidth / glyphCount;
            for (var i = 0; i < glyphCount; i++)
            {
                var pip = UIBox2.FromDimensions(new Vector2(topLeft.X + i * pipWidth, topLeft.Y + barHeight + 3f), new Vector2(MathF.Max(1f, pipWidth - 1f), 3f));
                handle.DrawRect(pip, new Color(245, 235, 195, 90));
            }
        }

        private static string GetBossDisplayName(string prototypeId)
        {
            var key = $"deep-maintenance-boss-name-{prototypeId.ToLowerInvariant()}";
            return Loc.TryGetString(key, out var localized) ? localized : prototypeId;
        }

        private void DrawDebugHitboxes(DrawingHandleScreen handle, float tilePixel, Vector2 mapOffset, float tickAlpha)
        {
            DrawDebugHitbox(handle, GetEntityHitbox(_playerProto, _playerPos), Color.LimeGreen, tilePixel, mapOffset);

            foreach (var enemy in CurrentRoom.Enemies.Where(enemy => enemy.Hp > 0))
            {
                var drawPos = Vector2.Lerp(enemy.PreviousPosition, enemy.Position, tickAlpha);
                DrawDebugHitbox(handle, GetEntityHitbox(enemy.Prototype, drawPos), Color.Red, tilePixel, mapOffset);
            }

            foreach (var projectile in _playerProjectiles)
            {
                var drawPos = Vector2.Lerp(projectile.PreviousCollisionPosition, projectile.CollisionPosition, tickAlpha);
                DrawDebugHitbox(handle, GetProjectileHitbox(projectile, drawPos), Color.Yellow, tilePixel, mapOffset);
            }

            foreach (var projectile in _enemyProjectiles)
            {
                var drawPos = Vector2.Lerp(projectile.PreviousCollisionPosition, projectile.CollisionPosition, tickAlpha);
                DrawDebugHitbox(handle, GetProjectileHitbox(projectile, drawPos), Color.Yellow, tilePixel, mapOffset);
            }

            if (_treasureBoxPosition is { } chestPos)
                DrawDebugAabb(handle, chestPos, TreasureObjectRadius, Color.CornflowerBlue, tilePixel, mapOffset);

            if (_treasureRelicPosition is { } relicPos)
                DrawDebugAabb(handle, relicPos, TreasureObjectRadius, Color.CornflowerBlue, tilePixel, mapOffset);
        }

        private static void DrawDebugHitbox(DrawingHandleScreen handle, HitboxData hitbox, Color color, float tilePixel, Vector2 mapOffset)
        {
            if (hitbox.Shape == DeepMaintenanceHitboxShape.Rectangle)
            {
                var center = mapOffset + hitbox.Center * tilePixel;
                var half = hitbox.HalfExtents * tilePixel;
                var box = UIBox2.FromDimensions(center - half, half * 2f);
                handle.DrawRect(box, color.WithAlpha(0.15f));
                handle.DrawLine(new Vector2(box.Left, box.Top), new Vector2(box.Right, box.Top), color);
                handle.DrawLine(new Vector2(box.Right, box.Top), new Vector2(box.Right, box.Bottom), color);
                handle.DrawLine(new Vector2(box.Right, box.Bottom), new Vector2(box.Left, box.Bottom), color);
                handle.DrawLine(new Vector2(box.Left, box.Bottom), new Vector2(box.Left, box.Top), color);
                return;
            }

            DrawDebugCircle(handle, hitbox.Center, hitbox.Radius, color, tilePixel, mapOffset);
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
                if (!IsRoomVisibleOnMinimap(i))
                    continue;

                var rel = room.MapPosition - current;
                var start = origin + new Vector2(rel.X * (roomSize + spacing), rel.Y * (roomSize + spacing)) + new Vector2(roomSize * 0.5f, roomSize * 0.5f);

                foreach (var (direction, neighborIndex) in room.Neighbors)
                {
                    if (neighborIndex <= i)
                        continue;

                    if (!IsRoomVisibleOnMinimap(neighborIndex))
                        continue;

                    var end = start + new Vector2(direction.X * (roomSize + spacing), direction.Y * (roomSize + spacing));
                    handle.DrawLine(start, end, Color.DarkSlateGray);
                }
            }

            for (var i = 0; i < _rooms.Count; i++)
            {
                var room = _rooms[i];

                if (!IsRoomVisibleOnMinimap(i))
                    continue;

                var rel = room.MapPosition - current;
                var center = origin + new Vector2(rel.X * (roomSize + spacing), rel.Y * (roomSize + spacing));
                var box = UIBox2.FromDimensions(center, new Vector2(roomSize, roomSize));

                var discovered = _visitedRooms.Contains(i);
                var color = room.Type switch
                {
                    RoomType.Boss => discovered ? Color.Red : new Color(110, 35, 35),
                    RoomType.Treasure => discovered ? Color.Gold : new Color(110, 95, 32),
                    RoomType.Secret => discovered ? new Color(180, 145, 255) : new Color(75, 60, 95),
                    RoomType.SuperSecret => discovered ? new Color(210, 170, 255) : new Color(88, 70, 112),
                    RoomType.Shop => discovered ? new Color(132, 224, 126) : new Color(55, 97, 51),
                    RoomType.Devil => discovered ? new Color(220, 95, 95) : new Color(90, 40, 40),
                    RoomType.Angel => discovered ? new Color(150, 195, 255) : new Color(60, 85, 120),
                    _ => discovered ? Color.LightGray : new Color(55, 55, 55),
                };

                if (i == RoomIndex)
                    color = Color.White;

                handle.DrawRect(box, color);
            }
        }

        private bool IsRoomVisibleOnMinimap(int roomIndex)
        {
            var room = _rooms[roomIndex];
            if (room is { IsSecret: true, Cleared: false } && roomIndex != RoomIndex)
                return false;

            if (_visitedRooms.Contains(roomIndex) || roomIndex == RoomIndex)
                return true;

            return _knownRooms.Contains(roomIndex);
        }

        #endregion
    }
}
