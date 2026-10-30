using PoeNoobMan.Core.Storage;

namespace PoeNoobMan.Core.Progress;

/// <summary>progress/&lt;guideId&gt;__&lt;персонаж&gt;.json</summary>
public sealed class ProgressStore(string dir)
{
    public string PathFor(string guideId, string character) =>
        Path.Combine(dir, $"{AppPaths.SafeFileName(guideId)}__{AppPaths.SafeFileName(character)}.json");

    public ProgressState Load(string guideId, string character) => JsonStore.Load<ProgressState>(PathFor(guideId, character));

    public void Save(string guideId, string character, ProgressState state) =>
        JsonStore.Save(PathFor(guideId, character), state);
}
