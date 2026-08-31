using NosAi.Runtime.Gate1;
using Xunit;

namespace NosAi.ControlPanel.Tests;

public sealed class ChannelViewTests
{
    [Fact]
    public void Wire_label_matches_this_build()
    {
        Assert.Equal($"v{WireHeader.CurrentVersion}", ChannelView.WireLabel);
    }

    [Fact]
    public void Missing_flags_stay_unknown()
    {
        var (label, _) = ChannelView.Slot(null, null, null);
        Assert.Equal("UNKNOWN", label);
    }

    [Fact]
    public void Authenticated_peer_owns_the_slot()
    {
        var (label, _) = ChannelView.Slot(true, true, null);
        Assert.Equal("SESSIONE AUTENTICATA", label);
    }

    [Fact]
    public void Connected_without_auth_is_occupied()
    {
        var (label, hint) = ChannelView.Slot(true, false, null);
        Assert.Equal("SLOT OCCUPATO", label);
        Assert.Contains("non autenticato", hint);
    }

    [Fact]
    public void Idle_channel_is_free()
    {
        var (label, _) = ChannelView.Slot(false, false, null);
        Assert.Equal("SLOT LIBERO", label);
    }

    [Fact]
    public void Phone_reminder_is_not_a_pass()
    {
        Assert.Contains("ancora aperto", ChannelView.PhoneReminder);
        Assert.Contains("Non è Verified", ChannelView.PhoneReminder);
    }
}
