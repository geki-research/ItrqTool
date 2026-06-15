namespace ItrqTool.Tasks.QuestionnaireValidation.Findings;

// ── Sheet-agnostic finding catalogue ─────────────────────────────────────────
//
// Constructed from whatever set of <see cref="FindingDescriptor"/>s it is handed
// (e.g. the CLQ baseline set, or a later RLQ/GD set). This type has NO dependency
// on any sheet's finding set — the set is injected, never referenced.
//
// It is the lookup surface the emitter and fail-loud diagnostics use: id validity,
// descriptor resolution, and the full id list in construction order.

public sealed class FindingCatalogue
{
    private readonly IReadOnlyDictionary<string, FindingDescriptor> _byId;
    private readonly IReadOnlyList<string> _allIds;

    public FindingCatalogue(IEnumerable<FindingDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        var byId = new Dictionary<string, FindingDescriptor>(StringComparer.Ordinal);
        var allIds = new List<string>();
        foreach (var descriptor in descriptors)
        {
            if (!byId.TryAdd(descriptor.Id, descriptor))
            {
                throw new ArgumentException(
                    $"Duplicate finding id '{descriptor.Id}' in catalogue; finding-ids must be unique.",
                    nameof(descriptors));
            }

            allIds.Add(descriptor.Id);
        }

        _byId = byId;
        _allIds = allIds;
    }

    /// <summary>True when <paramref name="id"/> is one of the catalogue's finding-ids.</summary>
    public bool IsValidId(string id) => _byId.ContainsKey(id);

    /// <summary>The descriptor for a finding-id (id string, default severity, check, description).</summary>
    public FindingDescriptor Descriptor(string id) =>
        _byId.TryGetValue(id, out var descriptor)
            ? descriptor
            : throw new KeyNotFoundException(
                $"Finding id '{id}' is not in the catalogue.");

    /// <summary>All valid finding-ids, in construction order — for fail-loud diagnostics.</summary>
    public IReadOnlyCollection<string> AllIds => _allIds;
}
