using PoeNoobMan.Core.Guides;
using PoeNoobMan.Core.Log;

namespace PoeNoobMan.Core.Progress;

/// <param name="Current">Кроки, які зараз варто показати, по порядку гайда.</param>
/// <param name="Blocker">Найближчий крок, що чекає на рівень, очки чи інший крок (для рядка «Далі»).</param>
/// <param name="Passed">Невиконані кроки, які гравець уже залишив позаду.</param>
public sealed record StepsSnapshot(IReadOnlyList<GuideStep> Current, GuideStep? Blocker, IReadOnlySet<string> Passed);

/// <param name="Now">Ноди, які вже час взяти (умову виконано), але ще не взяті.</param>
/// <param name="Next">Наступна нода, умову якої ще не виконано.</param>
public sealed record NodesSnapshot(IReadOnlyList<TreeNode> Now, TreeNode? Next);

/// <summary>
/// Уся логіка підказок. Не залежить від інтерфейсу; змінює лише переданий <see cref="ProgressState"/>.
///
/// Модель повністю автоматична:
/// <list type="bullet">
/// <item>крок маршруту «зона X: зробіть Y, далі Z» (<c>when.area: X</c>, <c>done.area: Z</c>) з'являється при вході в X
/// і закривається при вході в Z;</item>
/// <item>квестовий крок з <c>points</c> закривається рядком «You have received a Passive Skill Point»;</item>
/// <item>крок з <c>done.passive</c> — коли взято пасивку (асценд після лабіринту), <c>done.level</c> — за рівнем;</item>
/// <item>підказка без <c>done</c> (геми, вендори) закривається, коли її показали в місті і гравець вийшов з міста.</item>
/// </list>
/// Список не блокується: кроки, які гравець пропустив або залишив позаду, ховаються, а кроки майбутніх актів
/// не з'являються наперед.
/// </summary>
public sealed class ProgressEngine(GuideView view, ProgressState state)
{
    public GuideView View { get; } = view;
    public ProgressState State { get; } = state;

    // ---------------- Очки ----------------

    /// <summary>
    /// Очки дерева: рівень − 1 + очки за квести + ручна поправка. Очки за квести — точне число з /passives,
    /// а якщо його немає — більше з двох: отримані за логом або сума закритих квестових кроків
    /// (застосунок могли запустити посеред кампанії).
    /// </summary>
    public int Points => Math.Max(0, Math.Max(0, State.Level - 1) + QuestPoints + State.PointsAdjust);

    public int QuestPoints => State.QuestPointsExact ? State.QuestPoints : Math.Max(State.QuestPoints, DoneStepPoints());

    private int DoneStepPoints() => View.Steps.Where(s => s.Points != 0 && IsDone(s)).Sum(s => s.Points);

    /// <summary>
    /// Отримано очко за квест: рахуємо його і закриваємо найраніший квестовий крок, у зону якого вже заходили.
    /// Нагороду часто забирають пізніше, вже в наступному акті, тому шукаємо і серед кроків, що лишились позаду.
    /// </summary>
    public IReadOnlyList<string> OnPassivePointsReceived(int count)
    {
        if (!State.QuestPointsExact) State.QuestPoints = Math.Max(State.QuestPoints, DoneStepPoints());
        State.QuestPoints += count;

        var done = new List<string>();
        var left = count;
        while (left > 0 && View.Steps.FirstOrDefault(s => s.Points > 0 && !IsDone(s) && IsGateMet(s)) is { } quest)
        {
            Mark(quest.Id);
            done.Add(quest.Id);
            left -= quest.Points;
        }
        return [.. done, .. Recompute()];
    }

    /// <summary>Точна кількість з /passives.</summary>
    public void OnQuestPointsReported(int count)
    {
        State.QuestPoints = count;
        State.QuestPointsExact = true;
    }

    // ---------------- Кроки ----------------

    public bool IsDone(GuideStep s) => State.DoneSteps.Contains(s.Id);

    public bool IsGateMet(GuideStep s)
    {
        var w = s.When;
        if (w is null) return true;
        if (w.Level is { } l && State.Level < l) return false;
        if (w.Points is { } p && Points < p) return false;
        if (w.Step is { } id && !State.DoneSteps.Contains(id)) return false;
        if (w.Passive is { } n && !State.AllocatedPassives.Contains(n)) return false;
        if (w.Ascendancy is { } asc && State.AscendancyPoints < asc) return false;
        if (w.Area is not null && !State.UnlockedSteps.Contains(s.Id)) return false;
        return true;
    }

    /// <summary>Крок чекає на рівень, очки, пасивку чи інший крок (а не на зону).</summary>
    private static bool IsProgressGated(GuideStep s) =>
        s.When is { } w && (w.Level is not null || w.Points is not null || w.Step is not null || w.Passive is not null || w.Ascendancy is not null);

    /// <summary>Крок маршруту: «у зоні X … далі Z».</summary>
    private static bool IsRouteStep(GuideStep s) => s.When?.Area is not null && s.Done?.Area is not null;

    /// <summary>Персонаж уже пройшов хоч один крок маршруту кампанії (а не лише кроки перед ним, як-от мула).</summary>
    public bool HasRouteProgress => View.Steps.Any(s => IsRouteStep(s) && IsDone(s));

    /// <summary>Найдальший акт, у якому побував персонаж (за відвіданими зонами).</summary>
    public int ProgressAct => Math.Max(1, State.VisitedAreas.Select(AreaIds.ActOf).Max() ?? 1);

    /// <summary>
    /// Що показувати зараз. Крок прихований, якщо:
    /// <list type="bullet">
    /// <item>він з акту, до якого гравець ще не дійшов;</item>
    /// <item>його зону пропустили — уже відкрився крок пізніше за гайдом;</item>
    /// <item>це крок маршруту, а гравець уже перейшов у зону іншого кроку маршруту;</item>
    /// <item>він без умови або лише з умовою по зоні, а гравець уже в наступному акті;</item>
    /// <item>він з виконаною умовою по рівню/очках, а гравець пішов на два акти далі
    /// (так крок рівня 28 з Act 3, набраного аж в Act 4, ще встигне показатися).</item>
    /// </list>
    /// </summary>
    public StepsSnapshot GetSteps()
    {
        var progressAct = ProgressAct;
        var reachedIndex = -1;
        for (var i = 0; i < View.Steps.Count; i++)
        {
            var s = View.Steps[i];
            if (s.When?.Area is not null && (State.UnlockedSteps.Contains(s.Id) || IsDone(s))) reachedIndex = i;
        }

        var currentRoute = CurrentRouteId();

        var current = new List<GuideStep>();
        var passed = new HashSet<string>();
        GuideStep? blocker = null;
        for (var i = 0; i < View.Steps.Count; i++)
        {
            var s = View.Steps[i];
            if (IsDone(s) || s.Act > progressAct) continue;

            var unlocked = State.UnlockedSteps.Contains(s.Id);
            var isPassed = IsProgressGated(s)
                ? s.Act < progressAct - 1 && IsGateMet(s)
                : s.Act < progressAct
                  || (s.When?.Area is not null && !unlocked && i < reachedIndex)
                  || (IsRouteStep(s) && unlocked && s.Id != currentRoute);
            if (isPassed)
            {
                passed.Add(s.Id);
                continue;
            }

            if (IsGateMet(s)) current.Add(s);
            else if (blocker is null && IsProgressGated(s)) blocker = s;
        }
        return new StepsSnapshot(current, blocker, passed);
    }

    /// <summary>Поточний крок маршруту — відкритий останнім за часом (порядок зон в актах з розгалуженнями буває різний).</summary>
    private string? CurrentRouteId()
    {
        foreach (var id in Enumerable.Reverse(State.UnlockOrder))
        {
            var s = View.Steps.FirstOrDefault(x => x.Id == id);
            if (s is not null && IsRouteStep(s)) return IsDone(s) ? null : s.Id;
        }
        return null;
    }

    /// <summary>
    /// Вхід у зону. Відкриває кроки з цією зоною в <c>when</c>; закриває підказки, якщо гравець вийшов з міста;
    /// закриває перший видимий крок з цією зоною в <c>done</c> (один на вхід — щоб мул, який закрив свій крок
    /// у місті, не закрив і такий самий крок основного персонажа).
    /// Повторний вхід у ту саму зону поспіль (перезахід у гру, перезавантаження) нічого не змінює.
    /// </summary>
    /// <returns>Кроки, відмічені автоматично.</returns>
    public IReadOnlyList<string> OnAreaEntered(string? areaId, string areaName)
    {
        var prevId = State.CurrentAreaId;
        var prevName = State.CurrentAreaName;
        var repeated = string.Equals(prevId, areaId, StringComparison.OrdinalIgnoreCase)
                       && string.Equals(prevName, areaName, StringComparison.OrdinalIgnoreCase);
        var visibleBefore = repeated ? [] : GetSteps().Current;
        var actBefore = ProgressAct;
        var routeBefore = repeated ? null : CurrentRouteId();

        State.CurrentAreaId = areaId;
        State.CurrentAreaName = areaName;
        var key = areaId ?? areaName;
        if (!State.VisitedAreas.Contains(key)) State.VisitedAreas.Add(key);
        if (repeated) return [];

        // ID зон унікальні для кожного акту; збіг лише за назвою (без ID) обмежено поточним і наступним актом,
        // бо назви в актах 1–5 і 6–10 повторюються.
        foreach (var s in View.Steps)
        {
            if (IsDone(s) || State.UnlockedSteps.Contains(s.Id) || s.When?.Area is not { } a) continue;
            var byId = areaId is not null && string.Equals(a, areaId, StringComparison.OrdinalIgnoreCase);
            var byName = areaId is null && string.Equals(a, areaName, StringComparison.OrdinalIgnoreCase)
                         && s.Act >= actBefore && s.Act <= actBefore + 1;
            if ((byId || byName) && State.UnlockedSteps.Add(s.Id)) State.UnlockOrder.Add(s.Id);
        }
        // Повторний вхід у зону з уже відкритим кроком маршруту робить його знову поточним
        foreach (var s in View.Steps.Where(s => IsRouteStep(s) && !IsDone(s) && s.When!.Area is { } a
                     && areaId is not null && string.Equals(a, areaId, StringComparison.OrdinalIgnoreCase)))
        {
            State.UnlockOrder.Remove(s.Id);
            State.UnlockOrder.Add(s.Id);
        }

        var newly = new List<string>();
        if (AreaIds.IsTown(prevId, prevName) && !AreaIds.IsTown(areaId, areaName))
        {
            foreach (var s in visibleBefore.Where(s => s.Done is null && s.Points == 0 && !State.SuppressedAuto.Contains(s.Id)))
            {
                Mark(s.Id);
                newly.Add(s.Id);
            }
        }

        // Шукаємо серед кроків, видимих до входу (крок «зона X … далі Z» після входу в Z уже прихований), після нього,
        // а також серед відкритих кроків маршруту, які сховалися через інший порядок зон. Беремо перший за гайдом.
        var candidates = visibleBefore.Concat(GetSteps().Current)
            .Concat(View.Steps.Where(s => IsRouteStep(s) && State.UnlockedSteps.Contains(s.Id)))
            .Select(s => s.Id).ToHashSet();
        var hit = View.Steps.FirstOrDefault(s => candidates.Contains(s.Id)
            && !IsDone(s) && s.Done?.Area is { } d && !State.SuppressedAuto.Contains(s.Id)
            && AreaMatches(d, areaId, areaName) && IsDoneMet(s.Done, (areaId, areaName)));
        if (hit is not null)
        {
            Mark(hit.Id);
            newly.Add(hit.Id);
        }

        // Гравець пішов уперед за маршрутом у зону іншого кроку, не через «очікувану» зону: попередній крок теж
        // виконано (у грі зони проходять у різному порядку). Повернення назад (персонаж після мула, бектрекінг)
        // нічого не закриває. Якщо очікувана зона — саме ця, крок закривається вище за правилом «один на вхід».
        if (routeBefore is not null && CurrentRouteId() is { } routeNow && routeNow != routeBefore
            && IndexOf(routeNow) > IndexOf(routeBefore)
            && View.Steps.First(s => s.Id == routeBefore) is { } prevRoute && !IsDone(prevRoute)
            && !AreaMatches(prevRoute.Done!.Area!, areaId, areaName) && !State.SuppressedAuto.Contains(prevRoute.Id))
        {
            Mark(prevRoute.Id);
            newly.Add(prevRoute.Id);
        }

        newly.AddRange(Recompute());
        return newly;
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

    /// <summary>Взяли чи зняли пасивку: ноди дерева і кроки з <c>done.passive</c> відмічаються самі.</summary>
    public IReadOnlyList<string> OnPassiveAllocated(string name, bool allocated, bool ascendancy = false)
    {
        if (allocated) State.AllocatedPassives.Add(name);
        else State.AllocatedPassives.Remove(name);
        if (ascendancy) State.AscendancyPoints = Math.Max(0, State.AscendancyPoints + (allocated ? 1 : -1));

        foreach (var n in View.Nodes)
        {
            if (State.TakenNodes.Contains(n.Id) || n.Passives is not { Count: > 0 } list) continue;
            var take = n.Refund
                ? !allocated && list.Contains(name, StringComparer.OrdinalIgnoreCase) && list.All(p => !State.AllocatedPassives.Contains(p))
                : list.All(State.AllocatedPassives.Contains);
            if (take) State.TakenNodes.Add(n.Id);
        }
        return Recompute();
    }

    /// <summary>Закриває видимі кроки, чия умова done (крім зони) виконана, доки нічого не зміниться.</summary>
    public IReadOnlyList<string> Recompute()
    {
        var newly = new List<string>();
        var changed = true;
        while (changed)
        {
            changed = false;
            foreach (var s in GetSteps().Current)
            {
                if (s.Done is null || s.Done.Area is not null || State.SuppressedAuto.Contains(s.Id) || !IsDoneMet(s.Done, null)) continue;
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
        State.SuppressedAuto.Add(id);
        return [];
    }

    // ---------------- Дерево ----------------

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
        State.QuestPoints = 0;
        State.QuestPointsExact = false;
        State.CurrentAreaId = null;
        State.CurrentAreaName = null;
        State.VisitedAreas.Clear();
        State.DoneSteps.Clear();
        State.UnlockedSteps.Clear();
        State.UnlockOrder.Clear();
        State.SuppressedAuto.Clear();
        State.TakenNodes.Clear();
        State.AllocatedPassives.Clear();
        State.AscendancyPoints = 0;
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
        if (w.Passive is { } name && !State.AllocatedPassives.Contains(name)) return false;
        if (w.Ascendancy is { } asc && State.AscendancyPoints < asc) return false;
        if (w.Area is { } a && !State.VisitedAreas.Any(v => string.Equals(v, a, StringComparison.OrdinalIgnoreCase))) return false;
        return true;
    }

    /// <summary>
    /// Після завантаження прогресу (новий гайд або нова версія): відкриває кроки поточної зони,
    /// щоб крок маршруту було видно одразу, а не лише після переходу в наступну зону.
    /// </summary>
    public void RestoreCurrentArea()
    {
        if (State.CurrentAreaId is not { } id) return;
        foreach (var s in View.Steps)
        {
            if (IsDone(s) || s.When?.Area is not { } a || !string.Equals(a, id, StringComparison.OrdinalIgnoreCase)) continue;
            if (State.UnlockedSteps.Add(s.Id)) State.UnlockOrder.Add(s.Id);
            else if (IsRouteStep(s))
            {
                State.UnlockOrder.Remove(s.Id);
                State.UnlockOrder.Add(s.Id);
            }
        }
    }

    private bool IsDoneMet(Condition d, (string? Id, string Name)? entered)
    {
        if (d.IsEmpty) return false;
        if (d.Level is { } l && State.Level < l) return false;
        if (d.Points is { } p && Points < p) return false;
        if (d.Step is { } id && !State.DoneSteps.Contains(id)) return false;
        if (d.Passive is { } name && !State.AllocatedPassives.Contains(name)) return false;
        if (d.Ascendancy is { } asc && State.AscendancyPoints < asc) return false;
        if (d.Area is { } a && (entered is not { } e || !AreaMatches(a, e.Id, e.Name))) return false;
        return true;
    }

    private void Mark(string id)
    {
        if (State.DoneSteps.Add(id)) State.History.Add(id);
    }

    private int IndexOf(string id)
    {
        for (var i = 0; i < View.Steps.Count; i++)
            if (View.Steps[i].Id == id) return i;
        return -1;
    }
}
