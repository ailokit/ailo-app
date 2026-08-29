using Ailo.AI;
using Ailo.AI.Conversations;
using Ailo.Models;
using Ailo.Services;

namespace Ailo.Tests;

public sealed class SessionStateValidatorTests
{
    [Fact]
    public void CanRestore_RequiresExactAgentConfigurationMatch()
    {
        var persisted = new SessionSnapshot("chat", "provider", "model", 1, "1.13.0", "hash", "state");
        var missingState = persisted with { SerializedState = string.Empty };

        Assert.False(SessionStateValidator.CanRestore(missingState, persisted));
        Assert.False(SessionStateValidator.CanRestore(persisted, persisted with { ModelId = "other" }));
        Assert.True(SessionStateValidator.CanRestore(persisted, persisted));
    }
}
