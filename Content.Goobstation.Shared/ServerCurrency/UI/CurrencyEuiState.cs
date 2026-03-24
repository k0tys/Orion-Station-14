// SPDX-FileCopyrightText: 2025 Aiden <28298836+Aidenkrz@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 Misandry <mary@thughunt.ing>
// SPDX-FileCopyrightText: 2025 gluesniffler <159397573+gluesniffler@users.noreply.github.com>
// SPDX-FileCopyrightText: 2025 gus <august.eymann@gmail.com>
//
// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared.Eui;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Goobstation.Shared.ServerCurrency.UI
{
    [Serializable, NetSerializable]
    public sealed class CurrencyEuiState : EuiStateBase
    {
        // Orion-Start
        public List<TokenInventoryEntry> TokenInventory;

        public CurrencyEuiState(List<TokenInventoryEntry> tokenInventory)
        {
            TokenInventory = tokenInventory;
        }
        // Orion-End
    }

    // Orion-Start
    [Serializable, NetSerializable]
    public sealed class TokenInventoryEntry
    {
        public ProtoId<TokenListingPrototype> TokenId;
        public int Amount;

        public TokenInventoryEntry(ProtoId<TokenListingPrototype> tokenId, int amount)
        {
            TokenId = tokenId;
            Amount = amount;
        }
    }
    // Orion-End

    public static class CurrencyEuiMsg
    {
        [Serializable, NetSerializable]
        public sealed class Close : EuiMessageBase
        {
        }

        [Serializable, NetSerializable]
        public sealed class Buy : EuiMessageBase
        {
            public ProtoId<TokenListingPrototype> TokenId = default!; // Orion-Edit
        }

        // Orion-Start
        [Serializable, NetSerializable]
        public sealed class Use : EuiMessageBase
        {
            public ProtoId<TokenListingPrototype> TokenId = default!;
        }
        // Orion-End
    }
}
