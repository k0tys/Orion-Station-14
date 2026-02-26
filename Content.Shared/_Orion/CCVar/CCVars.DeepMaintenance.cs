using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    public static readonly CVarDef<bool> DeepMaintenanceDebugHitboxes =
        CVarDef.Create("deepmaintenance.debug_hitboxes_enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);
}
