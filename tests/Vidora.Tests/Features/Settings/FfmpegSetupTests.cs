using Vidora.Core.Models;
using Vidora.Core.Services;
using Vidora.Features.Settings;

namespace Vidora.Tests.Features.Settings;

[TestClass]
public sealed class FfmpegSetupTests
{
    [TestMethod]
    public async Task SelectFfmpeg_ValidExecutable_AppliesAndPersistsWithoutChangingOtherSettings()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeSettingsAsync(CancellationToken.None);

        await fixture.ViewModel.SelectFfmpegCommand.ExecuteAsync(fixture.Executable);

        Assert.AreEqual(fixture.Executable, fixture.CurrentPath);
        Assert.AreEqual(fixture.Executable, fixture.ViewModel.FfmpegPath);
        Assert.AreEqual(fixture.Executable, fixture.Settings.Value.FfmpegPath);
        Assert.AreEqual(@"C:\Videos", fixture.Settings.Value.LastDestinationDirectory);
        Assert.IsFalse(fixture.ViewModel.HasFfmpegSelectionError);
        Assert.IsTrue(fixture.ViewModel.IsReady);
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task SelectFfmpeg_InvalidOrUnpersistable_KeepsPreviousConfiguration(bool saveFails)
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.InitializeSettingsAsync(CancellationToken.None);
        await fixture.ViewModel.CheckDependenciesCommand.ExecuteAsync(null);
        var previousReport = fixture.ViewModel.Report;
        fixture.Dependency.Valid = saveFails;
        fixture.Settings.FailSave = saveFails;

        await fixture.ViewModel.SelectFfmpegCommand.ExecuteAsync(fixture.Executable);

        Assert.IsTrue(fixture.ViewModel.HasFfmpegSelectionError);
        Assert.IsNull(fixture.CurrentPath);
        Assert.IsNull(fixture.ViewModel.FfmpegPath);
        Assert.IsNull(fixture.Settings.Value.FfmpegPath);
        Assert.AreSame(previousReport, fixture.ViewModel.Report);
    }

    [TestMethod]
    public async Task SelectFfmpeg_MissingOrRelativePath_DoesNotConfigureOrExecute()
    {
        using var fixture = new Fixture();
        await fixture.ViewModel.SelectFfmpegCommand.ExecuteAsync("ffmpeg.exe");
        Assert.IsTrue(fixture.ViewModel.HasFfmpegSelectionError);
        Assert.IsNull(fixture.CurrentPath);
        Assert.AreEqual(0, fixture.Dependency.CheckCalls);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly string directory = Directory.CreateTempSubdirectory("vidora-ffmpeg-setup-").FullName;
        public Fixture()
        {
            Executable = Path.Combine(directory, "ffmpeg.exe");
            File.WriteAllText(Executable, "fixture; never executed");
            ViewModel = new SettingsViewModel(Dependency, settingsService: Settings,
                configureFfmpegPath: value => CurrentPath = value);
        }
        public string Executable { get; }
        public string? CurrentPath { get; private set; }
        public FakeSettings Settings { get; } = new();
        public FakeDependency Dependency { get; } = new();
        public SettingsViewModel ViewModel { get; }
        public void Dispose() => Directory.Delete(directory, true);
    }

    private sealed class FakeSettings : ISettingsService
    {
        public AppSettings Value { get; private set; } = new(@"C:\Videos");
        public bool FailSave { get; set; }
        public Task<AppSettings> LoadAsync(CancellationToken token) => Task.FromResult(Value);
        public async Task UpdateAsync(Func<AppSettings, AppSettings> update, CancellationToken ct) =>
            await SaveAsync(update(await LoadAsync(ct)), ct);

        public Task SaveAsync(AppSettings settings, CancellationToken token)
        {
            if (FailSave) throw new IOException("Settings locked");
            Value = settings;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeDependency : IDependencyService
    {
        public bool Valid { get; set; } = true;
        public int CheckCalls { get; private set; }
        public Task<DependencyReport> CheckAsync(CancellationToken token)
        {
            CheckCalls++;
            return Task.FromResult(new DependencyReport(new(DependencyStatus.Ready, "test"),
                new(Valid ? DependencyStatus.Ready : DependencyStatus.Invalid, Valid ? "test" : null)));
        }
        public Task<string> GetLatestYtDlpVersionAsync(CancellationToken token) => Task.FromResult("test");
        public Task<DependencyComponentReport> CheckFfmpegAsync(string path, CancellationToken token) =>
            Task.FromResult(new DependencyComponentReport(Valid ? DependencyStatus.Ready : DependencyStatus.Invalid, Valid ? "test" : null));
        public Task<DependencyReport> UpdateYtDlpAsync(CancellationToken token) => CheckAsync(token);
    }
}
