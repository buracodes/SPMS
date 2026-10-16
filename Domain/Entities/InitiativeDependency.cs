namespace Domain.Entities;

/// <summary>
/// Directed edge between two initiatives: <see cref="InitiativeId"/> cannot be
/// delivered before <see cref="DependsOnInitiativeId"/> is delivered.
/// Self-references and cycles are rejected by the application layer.
/// </summary>
public class InitiativeDependency
{
    public Guid Id { get; set; }

    /// <summary>The dependent initiative (the one that is blocked).</summary>
    public Guid InitiativeId { get; set; }
    public Initiative Initiative { get; set; } = null!;

    /// <summary>The prerequisite initiative.</summary>
    public Guid DependsOnInitiativeId { get; set; }
    public Initiative DependsOnInitiative { get; set; } = null!;
}
