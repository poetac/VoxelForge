// SetupWizardShouldShowTests.cs — UI overhaul Sprint 2 Step 7 (2026-04-28).
//
// Pins the static SetupWizardForm.ShouldShow(SessionSettings) decision
// matrix. Pure-function over a data record; no Form instantiation,
// sidesteps the xUnit + PicoGK pitfall (CLAUDE.md #8).

using Voxelforge.UI;

namespace Voxelforge.Tests;

public class SetupWizardShouldShowTests
{
    [Fact]
    public void ShouldShow_FirstLaunch_DefaultSettings_ReturnsTrue()
    {
        // Default-constructed SessionSettings = WizardVersion 0 + skip
        // off → first-launch user sees the wizard.
        var s = new SessionSettings();
        Assert.True(SetupWizardForm.ShouldShow(s));
    }

    [Fact]
    public void ShouldShow_UserOnCurrentVersion_ReturnsFalse()
    {
        // Returning user already on the latest wizard version doesn't
        // see it again on launch.
        var s = new SessionSettings
        {
            WizardVersion = SetupWizardForm.CurrentWizardVersion,
            SkipWizardOnLaunch = false,
        };
        Assert.False(SetupWizardForm.ShouldShow(s));
    }

    [Fact]
    public void ShouldShow_UserAheadOfCurrentVersion_ReturnsFalse()
    {
        // Future wizard version on the persisted side (forward-compat
        // safety): don't show backwards. In practice this only happens
        // if a user downgrades the app.
        var s = new SessionSettings
        {
            WizardVersion = SetupWizardForm.CurrentWizardVersion + 5,
            SkipWizardOnLaunch = false,
        };
        Assert.False(SetupWizardForm.ShouldShow(s));
    }

    [Fact]
    public void ShouldShow_UserOptedOut_ReturnsFalse_EvenWhenStaleVersion()
    {
        // Explicit opt-out trumps the version check. User stays on
        // the main form regardless of how old their wizard version is.
        var s = new SessionSettings
        {
            WizardVersion = 0,
            SkipWizardOnLaunch = true,
        };
        Assert.False(SetupWizardForm.ShouldShow(s));
    }

    [Fact]
    public void ShouldShow_NullSettings_ReturnsFalse()
    {
        // Defensive: null settings means first-launch loaded into a
        // bad state. Bail rather than crashing on a null-deref.
        Assert.False(SetupWizardForm.ShouldShow(null!));
    }

    [Fact]
    public void CurrentWizardVersion_IsPositive()
    {
        // Sanity: bumping the constant is the migration trigger; if
        // someone accidentally sets it to 0, every existing user gets
        // the wizard re-shown after they already completed it once.
        Assert.True(SetupWizardForm.CurrentWizardVersion >= 1);
    }
}
