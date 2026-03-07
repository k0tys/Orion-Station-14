using System.Linq;
using System.Numerics;
using Content.Shared._Orion.CartridgeLoader.Cartridges.DeepMaintenance;
using Content.Shared.Input;
using Robust.Client.Input;
using Robust.Shared.Input;

namespace Content.Client._Orion.CartridgeLoader.Cartridges.DeepMaintenance;

public sealed partial class DeepMaintenanceUiFragment
{
    private sealed partial class DeepMaintenanceGameControl
    {
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
                ContentKeyFunctions.DeepMaintenanceBomb,
                ContentKeyFunctions.Arcade3,
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

            public bool IsMoveUpHeld()
            {
                return _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceMoveUp) ||
                       _heldKeys.Contains(EngineKeyFunctions.MoveUp);
            }

            public bool IsMoveDownHeld()
            {
                return _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceMoveDown) ||
                       _heldKeys.Contains(EngineKeyFunctions.MoveDown);
            }

            public bool IsMoveLeftHeld()
            {
                return _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceMoveLeft) ||
                       _heldKeys.Contains(EngineKeyFunctions.MoveLeft);
            }

            public bool IsMoveRightHeld()
            {
                return _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceMoveRight) ||
                       _heldKeys.Contains(EngineKeyFunctions.MoveRight);
            }

            public bool IsShootUpHeld()
            {
                return _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceShootUp) ||
                       _heldKeys.Contains(ContentKeyFunctions.ArcadeUp);
            }

            public bool IsShootDownHeld()
            {
                return _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceShootDown) ||
                       _heldKeys.Contains(ContentKeyFunctions.ArcadeDown);
            }

            public bool IsShootLeftHeld()
            {
                return _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceShootLeft) ||
                       _heldKeys.Contains(ContentKeyFunctions.ArcadeLeft);
            }

            public bool IsShootRightHeld()
            {
                return _heldKeys.Contains(ContentKeyFunctions.DeepMaintenanceShootRight) ||
                       _heldKeys.Contains(ContentKeyFunctions.ArcadeRight);
            }

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
            Mushroom,
            Door,
        }

        private enum RoomType : byte
        {
            Start,
            Normal,
            Secret,
            SuperSecret,
            Shop,
            Treasure,
            Boss,
            Devil,
            Angel,
        }

        private enum CurseType : byte
        {
            None,
            Darkness,
            Lost,
            Maze,
            Blind,
            Frail,
            Scarcity,
            Turbulence,
        }

        private enum PickupType : byte
        {
            Coin,
            Bomb,
            Key,
            Heart,
        }

        private enum ResourceType : byte
        {
            Coin,
            Bomb,
            Key,
        }

        private enum EyeSource : byte
        {
            None,
            Left,
            Right,
        }

        private sealed class RoomData
        {
            public readonly RoomType Type;
            public readonly Vector2i MapPosition;
            public TileType[,] Tiles;
            public readonly List<EnemyData> Enemies = new();
            public readonly Dictionary<Vector2i, int> Neighbors = new();
            public readonly List<PickupData> Pickups = new();
            public readonly List<ShopSlotData> ShopSlots = new();
            public bool Cleared;
            public bool ClearRewardsSpawned;
            public bool IsSecret;
            public bool TreasureBoxOpened;
            public bool HasTreasureRelic;
            public bool TreasureRelicTaken;
            public string? TreasureRelicId;
            public bool DoorTargetOpen;
            public bool DoorVisualOpen;
            public int DoorTransitionTicks;
            public int DoorTransitionTotalTicks;

            public RoomData(RoomType type, Vector2i mapPosition, TileType[,] tiles)
            {
                Type = type;
                MapPosition = mapPosition;
                Tiles = tiles;
            }
        }

        private enum ShopItemType : byte
        {
            None,
            Bomb,
            Key,
            Heart,
            Relic,
        }

        private sealed class ShopSlotData
        {
            public readonly ShopItemType Item;
            public readonly int Amount;
            public readonly int Price;
            public readonly Vector2 Position;
            public readonly string? RelicId;
            public readonly string? SpritePath;
            public readonly string? SpriteState;
            public readonly float SpriteScale;
            public bool Sold;

            public ShopSlotData(ShopItemType item, int amount, int price, Vector2 position, string? spritePath, string? spriteState, float spriteScale, string? relicId = null)
            {
                Item = item;
                Amount = amount;
                Price = Math.Max(0, price);
                Position = position;
                RelicId = relicId;
                SpritePath = spritePath;
                SpriteState = spriteState;
                SpriteScale = spriteScale;
            }
        }

        private sealed class PickupData
        {
            public readonly PickupType Type;
            public readonly int Amount;
            public Vector2 Position;
            public float SpawnTimer;
            public readonly float SpawnDuration;

            public PickupData(PickupType type, int amount, Vector2 position, float spawnDelay = 0f)
            {
                Type = type;
                Amount = amount;
                Position = position;
                SpawnTimer = MathF.Max(0f, spawnDelay);
                SpawnDuration = MathF.Max(0.001f, SpawnTimer);
            }
        }

        private sealed class BombData
        {
            public readonly Vector2 Position;
            public float Timer;

            public BombData(Vector2 position, float timer)
            {
                Position = position;
                Timer = timer;
            }
        }

        private sealed class BombExplosionData
        {
            public readonly Vector2 Position;
            public float Timer;
            public readonly float Duration;

            public BombExplosionData(Vector2 position, float duration)
            {
                Position = position;
                Duration = MathF.Max(0.05f, duration);
                Timer = Duration;
            }
        }

        private sealed class FamiliarData
        {
            public readonly string SourceRelicId;
            public readonly DeepMaintenanceFamiliarConfig Config;
            public Vector2 Position;
            public Vector2 Velocity;
            public float ShootTimer;
            public float BurstTimer;
            public float TrailTimer;
            public float OrbitAngle;
            public float OrbitOffset;
            public float BobOffset;
            public int RoomCounter;

            public FamiliarData(string sourceRelicId, DeepMaintenanceFamiliarConfig config, Vector2 position)
            {
                SourceRelicId = sourceRelicId;
                Config = config;
                Position = position;
                ShootTimer = MathF.Max(0.05f, config.ShootInterval);
                BurstTimer = MathF.Max(0.1f, config.BurstInterval);
                TrailTimer = 0.1f;
                OrbitAngle = 0f;
                OrbitOffset = 0f;
                BobOffset = 0f;
            }
        }

        private sealed class BloodTrailData
        {
            public readonly Vector2 Position;
            public readonly float Radius;
            public readonly float Dps;
            public float Lifetime;

            public BloodTrailData(Vector2 position, float radius, float dps, float lifetime)
            {
                Position = position;
                Radius = MathF.Max(0.05f, radius);
                Dps = MathF.Max(0f, dps);
                Lifetime = MathF.Max(0.05f, lifetime);
            }
        }

        private sealed class VisualStatusEffectData
        {
            public readonly string StatusId;
            public Color Tint;
            public float TimeRemaining;

            public VisualStatusEffectData(string statusId, Color tint, float timeRemaining)
            {
                StatusId = statusId;
                Tint = tint;
                TimeRemaining = MathF.Max(0f, timeRemaining);
            }
        }

        private sealed class EnemyData
        {
            public readonly DeepMaintenanceEntityPrototype Prototype;
            public Vector2 Position;
            public Vector2 PreviousPosition;
            public int Hp;
            public float ShootCooldown;
            public float AggroDelay;
            public float WallContactTimer;
            public float EscapeTimer;
            public int StrafeDirection = 1;
            public float StrafeSwapTimer;
            public float SpawnGraceTimer;
            public float AvoidanceTimer;
            public int AvoidanceDirection = 1;
            public FacingDirection BodyFacing = FacingDirection.Down;
            public FacingDirection ShootFacing = FacingDirection.Down;
            public float DamageFlash;
            public float FearTimer;
            public float FearBossCooldown;
            public bool Frozen;
            public bool DeathHandled;
            public bool IsChampion;
            public string ChampionType = string.Empty;
            public float ChampionSpeedMultiplier = 1f;
            public float ChampionDamageMultiplier = 1f;
            public readonly List<VisualStatusEffectData> VisualEffects = new();

            public EnemyData(DeepMaintenanceEntityPrototype prototype, Vector2 position, float aggroDelay, float spawnGrace)
            {
                Prototype = prototype;
                Position = position;
                PreviousPosition = position;
                Hp = prototype.MaxHp;
                ShootCooldown = GetEnemyShootCooldown(prototype);
                AggroDelay = aggroDelay;
                StrafeSwapTimer = prototype.Shooter
                    ? EnemyStrafeSwapMinShooterSeconds
                    : EnemyStrafeSwapMinChaserSeconds;
                SpawnGraceTimer = spawnGrace;
            }
        }

        private sealed class ProjectileData
        {
            public Vector2 GroundPosition;
            public Vector2 PreviousGroundPosition;
            public Vector2 CollisionPosition;
            public Vector2 PreviousCollisionPosition;
            public readonly Vector2 SourcePosition;
            public Vector2 PlanarVelocity;
            public Vector2 Direction;
            public readonly float InitialLifetime;
            public readonly float InitialPlanarSpeed;
            public readonly float Radius;
            public readonly DeepMaintenanceHitboxShape HitboxShape;
            public readonly float HitboxWidth;
            public readonly float HitboxHeight;
            public readonly Vector2 HitboxOffset;
            public readonly float HeightScale;
            public float Height;
            public float PreviousHeight;
            public float VerticalVelocity;
            public readonly float Gravity;
            public readonly float TerminalFallSpeed;
            public readonly float CollisionMaxHeight;
            public readonly float SpriteLiftMultiplier;
            public readonly float ShadowScaleByHeight;
            public readonly bool BallisticEnabled;
            public readonly bool LandOnZeroHeight;
            public readonly bool CollideOnlyWhenLow;
            public readonly bool DestroyOnLanding;
            public readonly bool ImpactOnLanding;
            public bool HasLanded;
            public bool Spectral;
            public bool Piercing;
            public int RemainingPierces;
            public bool SplitOnHit;
            public bool SplitOnDeath;
            public int SplitCount;
            public bool Explosive;
            public bool Sticky;
            public bool Boomerang;
            public float ModifierStrength = 1f;
            public float HomingStrength;
            public float SinWaveAmplitude;
            public float SinWaveFrequency;
            public float SpiralTurnRate;
            public float HeavyMultiplier = 1f;
            public float TimeSinceSpawn;
            public readonly float Damage;
            public bool FreezeOnHit;
            public float FreezeChance = 0.5f;
            public bool FreezeBosses;
            public float Lifetime;
            public readonly string SpritePath;
            public readonly string SpriteState;
            public readonly float SpriteScale;
            public readonly DeepMaintenanceProjectilePrototype Prototype;
            public readonly Color Tint;

            public ProjectileData(Vector2 position, Vector2 sourcePosition, Vector2 velocity, float radius, DeepMaintenanceHitboxShape hitboxShape, float hitboxWidth, float hitboxHeight, Vector2 hitboxOffset, float damage, float lifetime, string spritePath, string spriteState, float spriteScale, DeepMaintenanceProjectilePrototype prototype, Color tint, float heightScale, float initialHeight, float initialVerticalVelocity, float gravity)
            {
                GroundPosition = position;
                PreviousGroundPosition = position;
                CollisionPosition = position;
                PreviousCollisionPosition = position;
                SourcePosition = sourcePosition;
                PlanarVelocity = velocity;
                Direction = NormalizeSafe(velocity);
                Radius = radius;
                HitboxShape = hitboxShape;
                HitboxWidth = hitboxWidth;
                HitboxHeight = hitboxHeight;
                HitboxOffset = hitboxOffset;
                Damage = damage;
                Lifetime = lifetime;
                InitialLifetime = lifetime;
                InitialPlanarSpeed = velocity.Length();
                HeightScale = MathF.Max(0.1f, heightScale);
                Height = MathF.Max(0f, initialHeight);
                PreviousHeight = Height;
                VerticalVelocity = initialVerticalVelocity;
                Gravity = MathF.Max(0f, gravity);
                TerminalFallSpeed = MathF.Max(0f, prototype.TerminalFallSpeed);
                CollisionMaxHeight = MathF.Max(0f, prototype.CollisionMaxHeight) * HeightScale;
                SpriteLiftMultiplier = MathF.Max(0f, prototype.SpriteLiftMultiplier);
                ShadowScaleByHeight = MathF.Max(0f, prototype.ShadowScaleByHeight);
                BallisticEnabled = prototype.BallisticEnabled;
                LandOnZeroHeight = prototype.LandOnZeroHeight;
                CollideOnlyWhenLow = prototype.CollideOnlyWhenLow;
                DestroyOnLanding = prototype.DestroyOnLanding;
                ImpactOnLanding = prototype.ImpactOnLanding;
                Spectral = prototype.Spectral;
                Piercing = prototype.Piercing;
                RemainingPierces = Math.Max(0, prototype.PierceCount);
                SpritePath = spritePath;
                SpriteState = spriteState;
                SpriteScale = spriteScale;
                Prototype = prototype;
                Tint = tint;

                foreach (var modifier in prototype.Modifiers)
                {
                    var modifierType = modifier.Type?.Trim().ToLowerInvariant();
                    var strength = MathF.Max(0f, modifier.Strength);
                    switch (modifierType)
                    {
                        case "spectral":
                            Spectral = true;
                            break;
                        case "piercing":
                            Piercing = true;
                            RemainingPierces = Math.Max(RemainingPierces, Math.Max(1, modifier.Intensity));
                            break;
                        case "splitonhit":
                            SplitOnHit = true;
                            SplitCount = Math.Max(SplitCount, Math.Max(2, modifier.Intensity));
                            break;
                        case "splitondeath":
                            SplitOnDeath = true;
                            SplitCount = Math.Max(SplitCount, Math.Max(2, modifier.Intensity));
                            break;
                        case "explosive":
                            Explosive = true;
                            ModifierStrength = MathF.Max(ModifierStrength, MathF.Max(0.5f, strength));
                            break;
                        case "sticky":
                            Sticky = true;
                            break;
                        case "boomerang":
                            Boomerang = true;
                            ModifierStrength = MathF.Max(ModifierStrength, MathF.Max(0.5f, strength));
                            break;
                        case "homing":
                            HomingStrength = MathF.Max(HomingStrength, strength <= 0f ? 1.8f : strength);
                            break;
                        case "sinwave":
                            SinWaveAmplitude = MathF.Max(SinWaveAmplitude, strength <= 0f ? 0.35f : strength);
                            SinWaveFrequency = MathF.Max(SinWaveFrequency, Math.Max(1f, modifier.Intensity));
                            break;
                        case "spiral":
                            SpiralTurnRate = MathF.Max(SpiralTurnRate, strength <= 0f ? 1.5f : strength);
                            break;
                        case "heavy":
                            HeavyMultiplier = MathF.Min(HeavyMultiplier, strength <= 0f ? 0.72f : MathF.Min(1f, strength));
                            break;
                        case "fast":
                            PlanarVelocity *= strength <= 0f ? 1.28f : strength;
                            break;
                    }
                }
            }
        }
    }
}
