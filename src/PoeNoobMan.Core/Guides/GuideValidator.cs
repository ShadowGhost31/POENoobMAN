namespace PoeNoobMan.Core.Guides;

public static class GuideValidator
{
    public static IReadOnlyList<string> Validate(Guide g)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(g.Id)) errors.Add("не задано id");
        if (string.IsNullOrWhiteSpace(g.Version)) errors.Add("не задано version");

        var options = new Dictionary<string, HashSet<string>>();
        foreach (var o in g.Options)
        {
            if (!options.TryAdd(o.Id, o.Choices.Select(c => c.Id).ToHashSet()))
                errors.Add($"опція {o.Id}: повторюється id");
            if (o.Choices.Count == 0) errors.Add($"опція {o.Id}: немає варіантів");
            if (o.Default is not null && o.Choices.All(c => c.Id != o.Default))
                errors.Add($"опція {o.Id}: default '{o.Default}' немає серед варіантів");
        }

        var stepIds = new HashSet<string>();
        foreach (var s in g.Steps)
        {
            var where = $"крок {s.Id}";
            if (string.IsNullOrWhiteSpace(s.Id)) errors.Add($"крок з текстом '{s.Text}': не задано id");
            else if (!stepIds.Add(s.Id)) errors.Add($"{where}: повторюється id");
            if (string.IsNullOrWhiteSpace(s.Text)) errors.Add($"{where}: порожній text");
            if (s.Done is { IsEmpty: true }) errors.Add($"{where}: порожня умова done");
            if (s.Kind is { } kind && !GuideStep.KnownKinds.Contains(kind.ToLowerInvariant()))
                errors.Add($"{where}: невідомий kind '{kind}' (можна: {string.Join(", ", GuideStep.KnownKinds)})");
            CheckFilter(where, s.If, options, errors);
        }

        foreach (var s in g.Steps)
        {
            CheckStepRef($"крок {s.Id}: when", s.When, stepIds, errors);
            CheckStepRef($"крок {s.Id}: done", s.Done, stepIds, errors);
        }

        var nodeIds = new HashSet<string>();
        foreach (var n in g.Tree)
        {
            var where = $"нода {n.Id}";
            if (string.IsNullOrWhiteSpace(n.Id)) errors.Add($"нода '{n.Name}': не задано id");
            else if (!nodeIds.Add(n.Id)) errors.Add($"{where}: повторюється id");
            if (string.IsNullOrWhiteSpace(n.Name)) errors.Add($"{where}: порожній name");
            CheckFilter(where, n.If, options, errors);
            CheckStepRef($"{where}: when", n.When, stepIds, errors);
        }

        var splitIds = new HashSet<string>();
        foreach (var sp in g.Splits)
        {
            var where = $"спліт {sp.Id}";
            if (string.IsNullOrWhiteSpace(sp.Id)) errors.Add($"спліт '{sp.Label}': не задано id");
            else if (!splitIds.Add(sp.Id)) errors.Add($"{where}: повторюється id");
            if (sp.On.IsEmpty) errors.Add($"{where}: порожня умова on");
            CheckFilter(where, sp.If, options, errors);
            CheckStepRef($"{where}: on", sp.On, stepIds, errors);
        }

        return errors;
    }

    private static void CheckFilter(string where, Dictionary<string, string>? filter,
        Dictionary<string, HashSet<string>> options, List<string> errors)
    {
        if (filter is null) return;
        foreach (var (key, value) in filter)
        {
            if (!options.TryGetValue(key, out var choices))
            {
                errors.Add($"{where}: невідома опція '{key}'");
                continue;
            }
            foreach (var v in value.Split('|', StringSplitOptions.TrimEntries))
                if (!choices.Contains(v)) errors.Add($"{where}: опція {key} не має варіанта '{v}'");
        }
    }

    private static void CheckStepRef(string where, Condition? c, HashSet<string> stepIds, List<string> errors)
    {
        if (c?.Step is { } id && !stepIds.Contains(id)) errors.Add($"{where}: невідомий крок '{id}'");
    }
}
