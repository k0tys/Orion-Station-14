using Content.Server.Popups;
using Content.Shared._Orion.Silicons.StationAi;
using Content.Shared.ActionBlocker;
using Content.Shared.Interaction.Events;
using Content.Shared.Mind.Components;
using Content.Shared.Popups;
using Content.Shared.Silicons.Laws.Components;
using Content.Shared.Silicons.StationAi;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Player;

namespace Content.Server._Orion.Silicons.StationAi;

public sealed class AiBootSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    private static readonly SoundSpecifier InitCompleteSound = new SoundPathSpecifier("/Audio/Machines/high_tech_confirm.ogg");

    public override void Initialize()
    {
        SubscribeLocalEvent<AiBootComponent, ComponentInit>(OnBootInit);
        SubscribeLocalEvent<AiBootComponent, MindAddedMessage>(OnMindAdded);
        SubscribeLocalEvent<AiBootComponent, PlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<AiBootComponent, BoundUIOpenedEvent>(OnBootUiOpened);
        SubscribeLocalEvent<AiBootComponent, BoundUIClosedEvent>(OnBootUiClosed);
        SubscribeLocalEvent<AiBootComponent, StationAiBootCompleteMessage>(OnBootComplete);

        SubscribeLocalEvent<AiBootComponent, InteractionAttemptEvent>(OnInteractionAttempt);
        SubscribeLocalEvent<AiBootComponent, UseAttemptEvent>(OnUseAttempt);
        SubscribeLocalEvent<AiBootComponent, ToggleLawsScreenEvent>(OnToggleLawsAttempt);
    }

    private void OnBootInit(Entity<AiBootComponent> ent, ref ComponentInit args)
    {
        UpdateBootContext(ent);
    }

    private void OnMindAdded(Entity<AiBootComponent> ent, ref MindAddedMessage args)
    {
        UpdateBootContext(ent);
    }

    private void OnPlayerAttached(Entity<AiBootComponent> ent, ref PlayerAttachedEvent args)
    {
        if (!HasComp<StationAiHeldComponent>(ent))
            return;

        UpdateBootContext(ent);
        TryStartBootFlow(ent, ent.Owner);
    }

    private void OnBootUiOpened(Entity<AiBootComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (args.UiKey is not StationAiBootUiKey.Key)
            return;

        if (args.Actor is not { Valid: true })
            return;

        UpdateUiState(ent);
    }

    private void OnBootUiClosed(Entity<AiBootComponent> ent, ref BoundUIClosedEvent args)
    {
        if (args.UiKey is not StationAiBootUiKey.Key)
            return;

        if (args.Actor is not { Valid: true } actor)
            return;

        if (!HasComp<ActorComponent>(ent))
            return;

        if (!ShouldBlock(ent.Comp))
            return;

        _ui.TryOpenUi(ent.Owner, StationAiBootUiKey.Key, actor);
        UpdateUiState(ent);
    }

    private void OnBootComplete(Entity<AiBootComponent> ent, ref StationAiBootCompleteMessage args)
    {
        if (args.Actor is not { Valid: true } actor)
            return;

        if (!TryComp<ActorComponent>(actor, out var actorComp))
            return;

        if (!HasComp<StationAiHeldComponent>(ent))
            return;

        UpdateBootContext(ent);

        if (!ent.Comp.ShowBootFlow || ent.Comp.Initialized)
            return;

        ent.Comp.Initialized = true;
        Dirty(ent);

        UpdateUiState(ent);
        _ui.CloseUi(ent.Owner, StationAiBootUiKey.Key, actor);

        var session = actorComp.PlayerSession;
        _popup.PopupClient(Loc.GetString("station-ai-boot-ready-final-status", ("name", ent.Comp.AiName)), ent.Owner, session.AttachedEntity, PopupType.Medium);
        _audio.PlayGlobal(InitCompleteSound, session, AudioParams.Default);
        _actionBlocker.UpdateCanMove(ent);
    }

    private static void OnInteractionAttempt(Entity<AiBootComponent> ent, ref InteractionAttemptEvent args)
    {
        if (!ShouldBlock(ent.Comp))
            return;

        args.Cancelled = true;
    }

    private static void OnUseAttempt(Entity<AiBootComponent> ent, ref UseAttemptEvent args)
    {
        if (!ShouldBlock(ent.Comp))
            return;

        args.Cancel();
    }

    private static void OnToggleLawsAttempt(Entity<AiBootComponent> ent, ref ToggleLawsScreenEvent args)
    {
        if (!ShouldBlock(ent.Comp))
            return;

        args.Handled = true;
    }

    public bool IsBootCompleted(EntityUid uid, AiBootComponent? comp = null)
    {
        return Resolve(uid, ref comp, false) && (comp.Initialized || !comp.ShowBootFlow);
    }

    private static bool ShouldBlock(AiBootComponent comp)
    {
        return comp is { ShowBootFlow: true, Initialized: false };
    }

    public bool CanUseNormalAiUi(EntityUid uid, AiBootComponent? comp = null)
    {
        return Resolve(uid, ref comp, false) && !ShouldBlock(comp);
    }

    public void TryStartBootFlow(EntityUid uid, EntityUid actor, AiBootComponent? comp = null)
    {
        if (!Resolve(uid, ref comp, false))
            return;

        TryStartBootFlow((uid, comp), actor);
    }

    private void TryStartBootFlow(Entity<AiBootComponent> ent, EntityUid actor)
    {
        if (!ent.Comp.ShowBootFlow || ent.Comp.Initialized)
            return;

        _ui.TryOpenUi(ent.Owner, StationAiBootUiKey.Key, actor);
        UpdateUiState(ent);
    }

    private void UpdateUiState(Entity<AiBootComponent> ent)
    {
        var state = new StationAiBootBuiState(ent.Comp.AiName, ent.Comp.Initialized, ent.Comp.ShowBootFlow, ent.Comp.IsMalf);
        _ui.SetUiState(ent.Owner, StationAiBootUiKey.Key, state);
    }

    private void UpdateBootContext(Entity<AiBootComponent> ent)
    {
        var name = MetaData(ent).EntityName;
        if (name != ent.Comp.AiName)
        {
            ent.Comp.AiName = name;
            Dirty(ent);
        }

        var isMalf = TryComp<SiliconLawProviderComponent>(ent, out var lawProvider) && lawProvider.Subverted;
        if (isMalf == ent.Comp.IsMalf)
            return;

        ent.Comp.IsMalf = isMalf;
        Dirty(ent);
    }
}
