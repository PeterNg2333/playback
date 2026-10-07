using Playback.Domain.Workspace;
namespace Playback.Application.Workspace;
public sealed class DemoAccounts(IWorkspaceRepository repository)
{
    public async Task<bool> SignIn(string account, string supplied, string expected, CancellationToken ct)
    {
        if (!(DemoCredentials.ValidUsername(account) & DemoCredentials.Equal(supplied, expected))) return false;
        await repository.GetOrCreateDemoUser(account, ct); return true;
    }
}
