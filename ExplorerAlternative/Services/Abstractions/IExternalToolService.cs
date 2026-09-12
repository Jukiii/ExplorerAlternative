using ExplorerAlternative.Models;

namespace ExplorerAlternative.Services.Abstractions;

public interface IExternalToolService
{
    void Run(ExternalToolDefinition tool, string targetPath);
}
