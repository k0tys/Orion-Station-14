// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Misandry <mary@thughunt.ing>
// SPDX-FileCopyrightText: 2025 gluesniffler <159397573+gluesniffler@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 gus <august.eymann@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Client.Eui;
using Content.Goobstation.Shared.ServerCurrency;
using Content.Goobstation.Shared.ServerCurrency.UI;
using Content.Shared.Eui;
using Robust.Shared.Prototypes;

namespace Content.Goobstation.Client.ServerCurrency.UI
{
    public sealed class CurrencyEui : BaseEui
    {
        private readonly CurrencyWindow _window;
        public CurrencyEui()
        {
            _window = new CurrencyWindow();
            _window.OnClose += () => SendMessage(new CurrencyEuiMsg.Close());
            _window.OnBuy += OnBuyMsg;
            _window.OnUse += OnUseMsg; // Orion
        }

        private void OnBuyMsg(ProtoId<TokenListingPrototype> tokenId)
        {
            // Orion-Edit-Start
            SendMessage(new CurrencyEuiMsg.Buy { TokenId = tokenId });
            // Orion-Edit-End
        }

        // Orion-Start
        private void OnUseMsg(ProtoId<TokenListingPrototype> tokenId)
        {
            SendMessage(new CurrencyEuiMsg.Use { TokenId = tokenId });
        }
        // Orion-End

        public override void Opened()
        {
            _window.OpenCentered();
        }

        // Orion-Start
        public override void HandleState(EuiStateBase state)
        {
            base.HandleState(state);

            if (state is CurrencyEuiState currency)
                _window.UpdateInventory(currency.TokenInventory);
        }
        // Orion-End

        public override void Closed()
        {
            _window.Close();
        }
    }
}
