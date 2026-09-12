using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

public interface ISettingsService
{
    AppSettings Current { get; }

    void Load();

    void Save();
}
