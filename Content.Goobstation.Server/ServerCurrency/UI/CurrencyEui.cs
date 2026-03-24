// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Misandry <mary@thughunt.ing>
// SPDX-FileCopyrightText: 2025 gluesniffler <159397573+gluesniffler@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 gus <august.eymann@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using System.Linq;
using Content.Goobstation.Common.ServerCurrency;
using Content.Goobstation.Shared.ServerCurrency;
using Content.Goobstation.Shared.ServerCurrency.UI;
using Content.Server._Orion.ServerCurrency;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Notes;
using Content.Server.Chat.Managers;
using Content.Server.EUI;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Shared.Database;
using Content.Shared.Eui;
using Content.Shared.Popups;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Goobstation.Server.ServerCurrency.UI
{
    public sealed class CurrencyEui : BaseEui
    {
        [Dependency] private readonly ICommonCurrencyManager _currencyMan = default!;
        [Dependency] private readonly IPrototypeManager _protoMan = default!;
        // Orion-Start
        [Dependency] private readonly TokenInventoryManager _tokenInventory = default!;
        [Dependency] private readonly IChatManager _chat = default!;
        [Dependency] private readonly IAdminLogManager _adminLog = default!;
        private readonly GameTicker _ticker;
        private readonly PopupSystem _popup;
        // Orion-End

        public CurrencyEui()
        {
            IoCManager.InjectDependencies(this);
            // Orion-Start
            var sys = IoCManager.Resolve<IEntitySystemManager>();
            _popup = sys.GetEntitySystem<PopupSystem>();
            _ticker = sys.GetEntitySystem<GameTicker>();
            // Orion-End
        }

        public override void Opened()
        {
            StateDirty();
        }

        public override EuiStateBase GetNewState()
        {
            // Orion-Edit-Start
            var inventory = _tokenInventory.GetInventory(Player.UserId)
                .Where(entry => entry.Value > 0)
                .Select(entry => new TokenInventoryEntry(entry.Key, entry.Value))
                .ToList();
            return new CurrencyEuiState(inventory);
            // Orion-Edit-End
        }


        public override void HandleMessage(EuiMessageBase msg)
        {
            base.HandleMessage(msg);
            switch (msg)
            {
                // Orion-Edit-Start
                case CurrencyEuiMsg.Buy buy:
                    BuyToken(buy.TokenId, Player);
                    StateDirty();
                    break;
                case CurrencyEuiMsg.Use use:
                    UseToken(use.TokenId, Player);
                    StateDirty();
                    break;
                // Orion-Edit-End
            }
        }

        // Orion-Edit-Start
        private void BuyToken(ProtoId<TokenListingPrototype> tokenId, ICommonSession player)
        {
            var balance = _currencyMan.GetBalance(player.UserId);

            if (!_protoMan.TryIndex(tokenId, out var token))
                return;

            if (balance < token.Price)
                return;

            if (!_tokenInventory.TryAddToken(player.UserId, token.ID))
                return;

            _currencyMan.RemoveCurrency(player.UserId, token.Price);
            _popup.PopupCursor(Loc.GetString("token-buy-success", ("name", Loc.GetString(token.Label))), player, PopupType.Medium);
        }
        // Orion-Edit-End

        // Orion-Start
        private void UseToken(ProtoId<TokenListingPrototype> tokenId, ICommonSession player)
        {
            if (!_protoMan.TryIndex(tokenId, out var token))
                return;

            if (!_tokenInventory.TryConsumeToken(player.UserId, token.ID))
            {
                _popup.PopupCursor(Loc.GetString("token-use-fail-missing"), player, PopupType.MediumCaution);
                return;
            }

            var inRound = _ticker.RunLevel == GameRunLevel.InRound;
            if ((inRound && !token.UsableInRound) || (!inRound && !token.UsableInLobby))
            {
                _tokenInventory.TryAddToken(player.UserId, token.ID);
                _popup.PopupCursor(Loc.GetString("token-use-fail-state"), player, PopupType.MediumCaution);
                return;
            }

            var playerName = player.Name;
            _popup.PopupCursor(Loc.GetString("token-use-success", ("name", Loc.GetString(token.Label))), player, PopupType.Medium);

            var alert = Loc.GetString(
                "token-admin-alert",
                ("name", playerName),
                ("userId", player.UserId),
                ("tokenType", token.TokenType),
                ("tokenName", Loc.GetString(token.Label)),
                ("when", DateTimeOffset.UtcNow));

            _chat.SendAdminAlert(alert);
            _adminLog.Add(LogType.Action, LogImpact.High, $"Token used by {player.Name} ({player.UserId}): {token.TokenType}/{token.ID}");
        }
        // Orion-End
    }
}
