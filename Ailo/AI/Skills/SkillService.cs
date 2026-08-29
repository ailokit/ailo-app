using Ailo.Data;
using Ailo.Models;

namespace Ailo.AI.Skills;

public sealed class SkillService(SkillRepository skills)
{
    public async Task<Skill> SaveCustomAsync(Skill draft, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.SystemPrompt);
        var existing = await skills.GetByIdAsync(draft.Id, cancellationToken).ConfigureAwait(false);

        var byName = await skills.GetByNameAsync(draft.Name, cancellationToken).ConfigureAwait(false);
        if (byName is not null && byName.Id != draft.Id)
        {
            throw new InvalidOperationException("A skill with this name already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var saved = draft with
        {
            Name = draft.Name.Trim(),
            SystemPrompt = draft.SystemPrompt.Trim(),
            IsBuiltIn = existing?.IsBuiltIn ?? false,
            Version = (existing?.Version ?? 0) + 1,
            CreatedAt = existing?.CreatedAt ?? now,
            UpdatedAt = now
        };
        await skills.SaveAsync(saved, cancellationToken).ConfigureAwait(false);
        return saved;
    }

    public async Task DeleteCustomAsync(string id, CancellationToken cancellationToken = default)
    {
        await skills.DeleteAsync(id, cancellationToken).ConfigureAwait(false);
    }
}
