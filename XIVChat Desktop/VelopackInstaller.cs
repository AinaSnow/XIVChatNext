using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace XIVChat_Desktop;

// Copies installed with Setup.exe update in place from GitHub Releases. The portable ZIP is not installed, so it gets none.
internal sealed class VelopackInstaller : IUpdateInstaller {
    private const string Repository = "https://github.com/AinaSnow/XIVChatNext";
    private readonly UpdateManager manager;
    private VelopackInstaller(UpdateManager manager) => this.manager = manager;

    public static IUpdateInstaller? TryCreate() {
        try {
            // A local folder of vpk output, for testing an update end to end without publishing a release.
            var feed = Environment.GetEnvironmentVariable("XIVCHAT_UPDATE_FEED");
            IUpdateSource source = Path.IsPathFullyQualified(feed ?? "") && Directory.Exists(feed)
                ? new SimpleFileSource(new DirectoryInfo(feed!)) : new GithubSource(Repository, null, false);
            var manager = new UpdateManager(source);
            return manager.IsInstalled ? new VelopackInstaller(manager) : null;
        } catch (Exception) {
            return null;
        }
    }

    public async Task<bool> PrepareAsync(Action<int> progress, CancellationToken token) {
        var update = await manager.CheckForUpdatesAsync().ConfigureAwait(false);
        if (update == null) return false;
        await manager.DownloadUpdatesAsync(update, progress, token).ConfigureAwait(false);
        // Update.exe waits for this process to exit, applies the update and starts the new version.
        manager.WaitExitThenApplyUpdates(update.TargetFullRelease, false, true, Array.Empty<string>());
        return true;
    }
}
