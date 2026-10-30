using PoeNoobMan.Core.Guides;

namespace PoeNoobMan.Core.Progress;

/// <param name="Current">Невиконані кроки, чиї умови виконано, по порядку до першого заблокованого.</param>
/// <param name="Blocker">Перший крок, умову якого ще не виконано; на ньому список зупиняється.</param>
public sealed record StepsSnapshot(IReadOnlyList<GuideStep> Current, GuideStep? Blocker);

/// <param name="Now">Ноди, які вже час взяти (умову виконано), але ще не відмічені.</param>
/// <param name="Next">Наступна нода, умову якої ще не виконано.</param>
public sealed record NodesSnapshot(IReadOnlyList<TreeNode> Now, TreeNode? Next);

/// <summary>Уся логіка підказок. Не залежить від інтерфейсу; змінює лише переданий <see cref="ProgressState"/>.</summary>
public sealed class ProgressEngine(GuideView view, ProgressState state)
{
    public GuideView View { get; } = view;
    public ProgressState State { get; } = state;

    public int Points => Math.Max(0,
        Math.Max(0, State.Level - 1)
        + View.Steps.Where(s => s.Points != 0 && State.DoneSteps.Contains(s.Id)).Sum(s => s.Points)
        + State.PointsAdjust);

    public bool IsDone(GuideStep s) => State.DoneSteps.Contains(s.Id);

    public bool IsGateMet(GuideStep s)
    {
        var w = s.When;
        if (w is null) return true;
        if (w.Level is { } l && State.Level < l) return false;
        if (w.Points is { } p && Points < p) return false;
        if (w.Step is { } id && !State.DoneSteps.Contains(id)) return false;
        if (w.Area is not null && !State.UnlockedSteps.Contains(s.Id)) return false;
        return true;
    }

    public StepsSnapshot GetSteps()
    {
        var current = new List<GuideStep>();
        foreach (var s in View.Steps)
        {
            if (IsDone(s)) continue;
            if (!IsGateMet(s)) return new StepsSnapshot(current, s);
            current.Add(s);
        }
        return new StepsSnapshot(current, null);
    }

    /// <summary>
    /// Вхід у зону. Відкриває перший заблокований крок з цією зоною в умові, починаючи з поточної позиції гайда
    /// і не далі наступного акту. Так візит мула або повторний візит у місто не відкриває кроки наперед.
    /// </summary>
    /// <returns>Кроки, відмічені автоматично.</returns>
    public IReadOnlyList<string> OnAreaEntered(string? areaId, string areaName)
    {
        State.CurrentAreaId = areaId;
        State.CurrentAreaName = areaName;
        var key = areaId ?? areaName;
        if (!State.VisitedAreas.Contains(key)) State.VisitedAreas.Add(key);

        var start = IndexOfFirstNotDone();
        if (start >= 0)
        {
            var blocker = GetSteps().Blocker;
            var maxAct = (blocker ?? View.Steps[start]).Act + 1;
            for (var i = start; i < View.Steps.Count; i++)
            {
                var s = View.Steps[i];
                if (s.Act > maxAct) break;
                if (IsDone(s) || State.UnlockedSteps.Contains(s.Id)) continue;
                if (s.When?.Area is { } a && AreaMatches(a, areaId, areaName))
                {
                    State.UnlockedSteps.Add(s.Id);
                    break;
                }
            }
        }
        return Recompute((areaId, areaName));
    }

    public IReadOnlyList<string> SetLevel(int level)
    {
        State.Level = Math.Clamp(level, 1, 100);
        return Recompute();
    }

    public IReadOnlyList<string> AdjustPoints(int delta)
    {
        State.PointsAdjust += delta;
        return Recompute();
    }

    /// <summary>Відмічає кроки, чия умова done виконана, доки нічого не зміниться.</summary>
    public IReadOnlyList<string> Recompute() => Recompute(null);

    /// <summary>
    /// Один вхід у зону закриває не більше одного кроку з умовою done по зоні — першого видимого.
    /// Інакше мул, що закрив свій крок у місті, одразу закрив би і такий самий крок Скіона.
    /// </summary>
    private List<string> Recompute((string? Id, string Name)? entered)
    {
        var newly = new List<string>();
        if (entered is { } e)
        {
            var hit = GetSteps().Current.FirstOrDefault(s =>
                s.Done?.Area is not null && !State.SuppressedAuto.Contains(s.Id) && IsDoneMet(s.Done, e));
            if (hit is not null)
            {
                Mark(hit.Id);
                newly.Add(hit.Id);
            }
        }

        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var s in GetSteps().Current)
            {
                if (s.Done is null || State.SuppressedAuto.Contains(s.Id) || !IsDoneMet(s.Done, null)) continue;
                Mark(s.Id);
                newly.Add(s.Id);
                changed = true;
                break;
            }
        }
        return newly;
    }

    /// <summary>Хоткей відмітки: перший поточний крок.</summary>
    /// <returns>Відмічений крок і ті, що відмітились за ним автоматично.</returns>
    public IReadOnlyList<string> MarkFirstCurrent()
    {
        var first = GetSteps().Current.FirstOrDefault();
        if (first is null) return [];
        Mark(first.Id);
        return [first.Id, .. Recompute()];
    }

    public string? UndoLast()
    {
        for (var i = State.History.Count - 1; i >= 0; i--)
        {
            var id = State.History[i];
            if (!State.DoneSteps.Contains(id)) continue;
            SetStepDone(id, false);
            return id;
        }
        return null;
    }

    public IReadOnlyList<string> SetStepDone(string id, bool done)
    {
        if (done)
        {
            State.SuppressedAuto.Remove(id);
            Mark(id);
            return [id, .. Recompute()];
        }
        State.DoneSteps.Remove(id);
        State.History.Remove(id);
        if (View.Steps.FirstOrDefault(s => s.Id == id)?.Done is not null) State.SuppressedAuto.Add(id);
        return [];
    }

    public NodesSnapshot GetNodes()
    {
        var now = new List<TreeNode>();
        foreach (var n in View.Nodes)
        {
            if (State.TakenNodes.Contains(n.Id)) continue;
            if (!IsNodeMet(n)) return new NodesSnapshot(now, n);
            now.Add(n);
        }
        return new NodesSnapshot(now, null);
    }

    public string? MarkFirstNode()
    {
        var n = GetNodes().Now.FirstOrDefault();
        if (n is null) return null;
        State.TakenNodes.Add(n.Id);
        return n.Id;
    }

    public void SetNodeTaken(string id, bool taken)
    {
        if (taken) State.TakenNodes.Add(id);
        else State.TakenNodes.Remove(id);
    }

    public void Reset()
    {
        State.Level = 1;
        State.PointsAdjust = 0;
        State.CurrentAreaId = null;
        State.CurrentAreaName = null;
        State.VisitedAreas.Clear();
        State.DoneSteps.Clear();
        State.UnlockedSteps.Clear();
        State.SuppressedAuto.Clear();
        State.TakenNodes.Clear();
        State.History.Clear();
    }

    public static bool AreaMatches(string condition, string? areaId, string? areaName) =>
        string.Equals(condition, areaId, StringComparison.OrdinalIgnoreCase)
        || string.Equals(condition, areaName, StringComparison.OrdinalIgnoreCase);

    private bool IsNodeMet(TreeNode n)
    {
        var w = n.When;
        if (w is null) return true;
        if (w.Level is { } l && State.Level < l) return false;
        if (w.Points is { } p && Points < p) return false;
        if (w.Step is { } id && !State.DoneSteps.Contains(id)) return false;
        if (w.Area is { } a && !State.VisitedAreas.Any(v => string.Equals(v, a, StringComparison.OrdinalIgnoreCase))) return false;
        return true;
    }

    private bool IsDoneMet(Condition d, (string? Id, string Name)? entered)
    {
        if (d.IsEmpty) return false;
        if (d.Level is { } l && State.Level < l) return false;
        if (d.Points is { } p && Points < p) return false;
        if (d.Step is { } id && !State.DoneSteps.Contains(id)) return false;
        if (d.Area is { } a && (entered is not { } e || !AreaMatches(a, e.Id, e.Name))) return false;
        return true;
    }

    private void Mark(string id)
    {
        if (State.DoneSteps.Add(id)) State.History.Add(id);
    }

    private int IndexOfFirstNotDone()
    {
        for (var i = 0; i < View.Steps.Count; i++)
            if (!IsDone(View.Steps[i])) return i;
        return -1;
    }
}
