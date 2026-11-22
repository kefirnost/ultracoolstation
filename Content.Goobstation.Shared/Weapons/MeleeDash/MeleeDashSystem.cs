// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Goobstation.Common.Weapons.MeleeDash;
using Content.Shared.Emoting;
using Content.Shared.Gravity; // Omu
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Popups; // Omu
using Content.Shared.Standing;
using Content.Shared.Throwing;
using Content.Shared.Timing;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Input.Binding; // Omu
using Content.Shared.Input; // Omu
namespace Content.Goobstation.Shared.Weapons.MeleeDash;

public sealed class MeleeDashSystem : EntitySystem
{
    [Dependency] private UseDelaySystem _useDelay = default!;
    [Dependency] private Emoting.SharedAnimatedEmotesSystem _emotes = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private ThrowingSystem _throwing = default!;
    [Dependency] private SharedMeleeWeaponSystem _melee = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private StandingStateSystem _standing = default!;
    [Dependency] private SharedPhysicsSystem _physics = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedGravitySystem _gravity = default!;
    [Dependency] private SharedPopupSystem _popupSystem = default!;

    private const int DashCollisionLayer = (int) CollisionGroup.MidImpassable;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DashingComponent, LandEvent>(OnLand);
        SubscribeLocalEvent<DashingComponent, StartCollideEvent>(OnCollide);
        SubscribeAllEvent<MeleeDashEvent>(OnDash);
    }

    private void OnCollide(Entity<DashingComponent> ent, ref StartCollideEvent args)
    {
        var (uid, comp) = ent;

        if (!TryComp(uid, out ActorComponent? actor))
            return;

        if (!TryComp(comp.Weapon, out MeleeWeaponComponent? melee))
            return;

        if (comp.HitEntities.Contains(args.OtherEntity))
            return;

        if (!HasComp<MobStateComponent>(args.OtherEntity))
            return;

        if (!_hands.IsHolding(ent.Owner, ent.Comp.Weapon))
            return;

        comp.HitEntities.Add(args.OtherEntity);
        Dirty(ent);

        var ev = new LightAttackEvent(GetNetEntity(args.OtherEntity),
            GetNetEntity(comp.Weapon.Value),
            GetNetCoordinates(Transform(args.OtherEntity).Coordinates));
        _melee.DoLightAttack(uid, ev, comp.Weapon.Value, melee, actor.PlayerSession);
    }

    private void OnLand(Entity<DashingComponent> ent, ref LandEvent args)
    {
        var (uid, comp) = ent;

        if (TryComp(uid, out FixturesComponent? fixtureComponent))
        {
            foreach (var key in comp.ChangedFixtures)
            {
                if (!fixtureComponent.Fixtures.TryGetValue(key, out var fixture))
                    continue;

                _physics.SetCollisionMask(uid,
                    key,
                    fixture,
                    fixture.CollisionMask | DashCollisionLayer,
                    fixtureComponent);
            }
        }

        RemCompDeferred(uid, comp);
    }

    private void OnDash(MeleeDashEvent msg, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity == null)
            return;

        var user = args.SenderSession.AttachedEntity.Value;
        var weapon = GetEntity(msg.Weapon); // Omu - added gravity check for dash and a bypass. also added popups.

        if (!TryComp<MeleeDashComponent>(weapon, out var dash))
            return;

        if (_gravity.IsWeightless(user) && !dash.CanDashWhileWeightless) // Omu - Start
        {
            _popupSystem.PopupClient(Loc.GetString("no-dash-while-weightless"), user, user, PopupType.Medium);
            return;
        } // Omu - End

        if (_standing.IsDown(user))
        {
            _popupSystem.PopupClient(Loc.GetString("no-dash-while-lying"), user, user, PopupType.Medium); // Omu
            return;
        }

        if (_container.IsEntityInContainer(user))
        {
            _popupSystem.PopupClient(Loc.GetString("no-dash-while-contained"), user, user, PopupType.Medium); // Omu
            return;
        }

        // var weapon = GetEntity(msg.Weapon); - Omu - moved up

        if (/* !TryComp(weapon, out MeleeDashComponent? dash) -  Omu - moved up||*/ !TryComp(weapon, out UseDelayComponent? delay) || _useDelay.IsDelayed((weapon, delay)))
            return;

        var length = MathF.Min(msg.Direction.Length(), dash.MaxDashLength);
        if (length <= 0f)
            return;
        var dir = msg.Direction.Normalized() * length;

        _useDelay.TryResetDelay((weapon, delay));

        var dashing = EnsureComp<DashingComponent>(user);

        if (TryComp(user, out FixturesComponent? fixtureComponent))
        {
            foreach (var (key, fixture) in fixtureComponent.Fixtures)
            {
                if ((fixture.CollisionMask & DashCollisionLayer) == 0)
                    continue;

                dashing.ChangedFixtures.Add(key);
                _physics.SetCollisionMask(user,
                    key,
                    fixture,
                    fixture.CollisionMask & ~DashCollisionLayer,
                    manager: fixtureComponent);
            }
        }

        dashing.Weapon = weapon;
        Dirty(user, dashing);

        _throwing.TryThrow(user, dir, dash.DashForce, null, 0f, null, false, false, false, false, false);
        _audio.PlayPredicted(dash.DashSound, user, user);

        if (dash.EmoteOnDash != null)
            _emotes.SetEmote(user, dash.EmoteOnDash);
    }
}
