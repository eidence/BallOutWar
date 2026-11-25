using BOW.Meta;
using NUnit.Framework;

namespace BOW.Simulation.Tests.Meta
{
    public class EntitlementTests
    {
        [Test]
        public void FreeModes_AreAlwaysAvailable()
        {
            var e = new Entitlements();
            Assert.That(e.StatusFor(GameMode.Timed), Is.EqualTo(ModeStatus.Available));
            Assert.That(e.StatusFor(GameMode.Practice), Is.EqualTo(ModeStatus.Available));
            Assert.That(e.CanTrial(GameMode.Timed), Is.False, "no trial needed for a free mode");
        }

        [Test]
        public void LockedMode_HasOneTrial_ThenLocks()
        {
            var e = new Entitlements();
            int changes = 0;
            e.Changed += () => changes++;
            Assert.That(e.StatusFor(GameMode.Elimination), Is.EqualTo(ModeStatus.Trial));
            Assert.That(e.CanHost(GameMode.Elimination), Is.False);

            Assert.That(e.UseTrial(GameMode.Elimination), Is.True);
            Assert.That(e.StatusFor(GameMode.Elimination), Is.EqualTo(ModeStatus.Locked));
            Assert.That(e.UseTrial(GameMode.Elimination), Is.False);
            Assert.That(changes, Is.EqualTo(1));
        }

        [Test]
        public void ModesUnlock_OpensEveryModeAtOnce_AndGrantsOnlyOnce()
        {
            var e = new Entitlements();
            int changes = 0;
            e.Changed += () => changes++;
            Assert.That(e.Grant(Products.ModesUnlock), Is.True);
            Assert.That(e.Grant(Products.ModesUnlock), Is.False);
            Assert.That(changes, Is.EqualTo(1));
            Assert.That(e.CanHost(GameMode.Elimination), Is.True);
            Assert.That(e.StatusFor(GameMode.Elimination), Is.EqualTo(ModeStatus.Available));
            Assert.That(e.CanTrial(GameMode.Elimination), Is.False);
        }

        [Test]
        public void Load_RestoresWithoutRaisingChanged()
        {
            var e = new Entitlements();
            bool changed = false;
            e.Changed += () => changed = true;
            e.Load(new[] { Products.ModesUnlock, "", null }, new[] { GameMode.Elimination });
            Assert.That(changed, Is.False);
            Assert.That(e.Owns(Products.ModesUnlock), Is.True);
            Assert.That(e.TrialsUsed, Is.EquivalentTo(new[] { GameMode.Elimination }));
        }
    }
}
