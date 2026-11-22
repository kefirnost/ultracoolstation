using Content.Shared._Funkystation.Explosion;

namespace Content.Server.Explosion.EntitySystems;

public sealed partial class ExplosionSystem
{
    private void RaiseExplosiveTriggered(EntityUid uid)
    {
        var ev = new ExplosiveTriggeredEvent();
        RaiseLocalEvent(uid, ref ev);
    }
}
