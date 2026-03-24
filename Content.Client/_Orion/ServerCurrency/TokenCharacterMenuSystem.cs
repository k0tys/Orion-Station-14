using Content.Client.CharacterInfo;
using Robust.Client.Console;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._Orion.ServerCurrency;

public sealed class TokenCharacterMenuSystem : EntitySystem
{
    [Dependency] private readonly IClientConsoleHost _console = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CharacterInfoSystem.GetCharacterInfoControlsEvent>(OnGetCharacterInfoControls);
    }

    private void OnGetCharacterInfoControls(ref CharacterInfoSystem.GetCharacterInfoControlsEvent args)
    {
        var button = new Button
        {
            Text = Loc.GetString("token-character-menu-button"),
            HorizontalExpand = false,
        };

        button.OnPressed += _ => _console.ExecuteCommand("tokeninventory");
        args.Controls.Add(button);
    }
}
