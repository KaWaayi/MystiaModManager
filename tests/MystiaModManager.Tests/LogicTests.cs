using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using MystiaModManager.Logic;
using Newtonsoft.Json.Linq;
using Xunit;

namespace MystiaModManager.Tests;

public class LogicTests
{
    [Fact]
    public void WritesRejectedWhileGameRunning()
    {
        string[] actions = { "启用", "禁用", "卸载", "安装", "更新模组", "保存配置" };
        foreach (var action in actions)
        {
            Assert.Equal("请先退出游戏。", ModWriteGuard.Reject(true, action));
            Assert.Null(ModWriteGuard.Reject(false, action));
        }
    }

    [Fact]
    public void ExitCodeZero_DoesNotExport_NonZeroDoes()
    {
        var clean = new GameLaunchWatch();
        clean.Arm();
        Assert.Null(clean.Tick(new[] { 42 }, _ => 0));
        Assert.False(clean.Tick(new int[0], _ => 0));

        var crashed = new GameLaunchWatch();
        crashed.Arm();
        Assert.Null(crashed.Tick(new[] { 42 }, _ => 1));
        Assert.True(crashed.Tick(new int[0], _ => 1));
    }

    [Fact]
    public void ProcessNotLaunchedByManager_DoesNotExport()
    {
        var watch = new GameLaunchWatch();
        Assert.Null(watch.Tick(new[] { 42 }, _ => 1));
        Assert.Null(watch.Tick(new int[0], _ => 1));
        Assert.False(watch.Armed);
    }

    [Fact]
    public void UpdatePrompt_OnlyWhenRemoteIsNewer()
    {
        Assert.Equal("0.2.0", ManagerUpdate.ReadVersion("{\"version\":\"0.2.0\",\"objectKey\":\"MystiaModManager.zip\"}"));
        Assert.True(ManagerUpdate.ShouldPrompt("0.2.0", "0.1.7.0"));
        Assert.False(ManagerUpdate.ShouldPrompt("0.1.7", "0.1.7.0"));
        Assert.False(ManagerUpdate.ShouldPrompt("0.1.6", "0.1.7.0"));
    }

    [Fact]
    public void SteamLaunch_UsesAppLaunchOnly_AndPrefersHkcu()
    {
        Assert.Equal("-applaunch 1584090", SteamLaunch.Arguments);
        Assert.Equal(
            @"C:\Steam\Steam.exe",
            SteamLaunch.Choose(@"C:/Steam", @"D:\Other", path => path == @"C:\Steam\Steam.exe"));
        Assert.Equal(
            @"D:\Other\Steam.exe",
            SteamLaunch.Choose(@"C:/Missing", @"D:\Other", path => path == @"D:\Other\Steam.exe"));
    }

    [Fact]
    public void LaunchButton_ShowsPlayingAndIgnoresClick()
    {
        Assert.Equal("游戏中", LaunchState.Caption(true));
        Assert.Equal("带模组启动", LaunchState.Caption(false));
        Assert.False(LaunchState.AcceptsClick(true));
        Assert.True(LaunchState.AcceptsClick(false));
        Assert.True(LaunchState.JustStopped(true, false));
        Assert.False(LaunchState.JustStopped(false, false));
        Assert.False(LaunchState.JustStopped(true, true));
    }

    [Fact]
    public void Session_StoresTokenBesideManagerJson()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmm-session-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "manager.json"), "{}");
            var session = SessionStore.FilePath(root);
            Assert.Equal(Path.GetDirectoryName(Path.Combine(root, "manager.json")), Path.GetDirectoryName(session));
            SessionStore.SaveToken(root, "sample-token");
            var text = File.ReadAllText(session);
            var json = JObject.Parse(text);
            Assert.Equal(new[] { "token" }, json.Properties().Select(p => p.Name).ToArray());
            Assert.Equal("sample-token", SessionStore.ReadToken(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CrashPackage_IncludesLog_ErrorOnlyWhenPresent_AndSaveFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmm-crash-" + Guid.NewGuid().ToString("n"));
        var profile = Path.Combine(root, "profile");
        var user = Path.Combine(root, "user");
        var zip = Path.Combine(root, "out", CrashPackage.FileName(new DateTime(2026, 10, 7, 13, 24, 5)));
        Directory.CreateDirectory(Path.Combine(profile, "BepInEx"));
        File.WriteAllText(Path.Combine(profile, "BepInEx", "LogOutput.log"), "log");
        var save = CrashPackage.SaveFolder(user);
        Directory.CreateDirectory(Path.Combine(save, "slot"));
        File.WriteAllText(Path.Combine(save, "slot", "a.dat"), "save");
        try
        {
            Assert.Equal("2026-10-07_13-24-05.zip", Path.GetFileName(zip));
            var withoutError = CrashPackage.Collect(profile, user).Select(item => item.EntryName).ToArray();
            Assert.Contains("BepInEx/LogOutput.log", withoutError);
            Assert.DoesNotContain("BepInEx/ErrorLog.log", withoutError);
            Assert.Contains("Save/slot/a.dat", withoutError);

            File.WriteAllText(Path.Combine(profile, "BepInEx", "ErrorLog.log"), "err");
            CrashPackage.Create(profile, user, zip);
            using (var archive = ZipFile.OpenRead(zip))
            {
                var names = archive.Entries.Select(entry => entry.FullName).ToArray();
                Assert.Contains("BepInEx/LogOutput.log", names);
                Assert.Contains("BepInEx/ErrorLog.log", names);
                Assert.Contains("Save/slot/a.dat", names);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Subscribe409Missing_IsNotSuccess()
    {
        var denied = MarketClient.ReadSubscribe(409, "{\"error\":\"前置尚未上架\",\"missing\":[\"need.a\",\"need.b\"]}");
        Assert.False(denied.Succeeded);
        Assert.Equal(new[] { "need.a", "need.b" }, denied.Missing);

        var other = MarketClient.ReadSubscribe(409, "{\"error\":\"该版本已存在\"}");
        Assert.False(other.Succeeded);
        Assert.Empty(other.Missing);

        var ok = MarketClient.ReadSubscribe(200, "{\"ok\":true}");
        Assert.True(ok.Succeeded);
        Assert.Empty(ok.Missing);
    }

    [Fact]
    public void DownloadFileName_IsIdVersionZip()
    {
        Assert.Equal("alpha_1.2.3.zip", MarketClient.DownloadFileName(null, "alpha", "1.2.3"));
        Assert.Equal(
            "alpha_1.2.3.zip",
            MarketClient.DownloadFileName("attachment; filename*=UTF-8''alpha_1.2.3.zip", "alpha", "1.2.3"));
        Assert.Equal(
            "alpha_1.2.3.zip",
            MarketClient.DownloadFileName("attachment; filename=\"alpha_1.2.3.zip\"", "alpha", "1.2.3"));
    }

    [Fact]
    public void Catalog_ShowsUploadOnlyForAdmin()
    {
        var guest = MarketClient.ParseList("{\"isAdmin\":false,\"mods\":[{\"id\":\"a\",\"name\":\"A\"}]}");
        var admin = MarketClient.ParseList("{\"isAdmin\":true,\"mods\":[]}");
        Assert.False(guest.ShowUpload);
        Assert.True(admin.ShowUpload);
        Assert.Equal("a", guest.Mods[0].Id);
    }

    [Fact]
    public void UpdatePlan_UsesNumericOrder()
    {
        var installed = new[] { new InstalledMod("alpha", "1.9"), new InstalledMod("beta", "2.0") };
        var remote = new[]
        {
            new MarketMod("alpha", "Alpha", "1.10", null),
            new MarketMod("beta", "Beta", "2.0", null)
        };
        var plan = ModUpdatePlan.Select(installed, remote);
        Assert.Single(plan);
        Assert.Equal("alpha", plan[0].Id);
        Assert.Equal("1.10", plan[0].RemoteVersion);
        Assert.Equal("您有1个模组更新可用。是否要更新全部？", ModUpdatePlan.Banner(1));
        Assert.Equal("", ModUpdatePlan.Banner(0));
    }

    [Fact]
    public void Logout_RemovesSessionFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmm-logout-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            SessionStore.SaveToken(root, "abc");
            SessionStore.Clear(root);
            Assert.Null(SessionStore.ReadToken(root));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ZipLayout_RoutesThreeFolders()
    {
        Assert.Equal(
            Path.Combine(@"C:\profile", "BepInEx", "plugins", "Mod", "a.dll"),
            ZipLayout.Destination("plugins/Mod/a.dll", @"C:\profile", @"D:\game"));
        Assert.Equal(
            Path.Combine(@"C:\profile", "BepInEx", "patchers", "p.dll"),
            ZipLayout.Destination("patchers/p.dll", @"C:\profile", @"D:\game"));
        Assert.Equal(
            Path.Combine(@"C:\profile", "ResourceEx", "a.txt"),
            ZipLayout.Destination("ResourceEx/a.txt", @"C:\profile", @"D:\game"));
        Assert.Null(ZipLayout.Destination("README.md", @"C:\profile", @"D:\game"));
    }

    [Fact]
    public void Enable_PullsInstalledHardDependency_AndSkipsMissing()
    {
        var mods = new[]
        {
            new ModNode("Child", "child", false, new[] { "base", "missing" }),
            new ModNode("Base", "base", false, new string[0])
        };
        var plan = ModActionPlan.FoldersToEnable(new[] { "Child" }, mods);
        Assert.Equal(new[] { "Base", "Child" }, plan);
    }

    [Fact]
    public void Disable_AlsoDisablesDependents()
    {
        var mods = new[]
        {
            new ModNode("Base", "base", true, new string[0]),
            new ModNode("Child", "child", true, new[] { "base" })
        };
        var plan = ModActionPlan.FoldersToDisable(new[] { "Base" }, mods);
        Assert.Contains("Base", plan);
        Assert.Contains("Child", plan);
    }

    [Fact]
    public void Enable_SkipsPromptWhenDependencyAlreadyOn()
    {
        var mods = new[]
        {
            new ModNode("Child", "child", false, new[] { "base" }),
            new ModNode("Base", "base", true, new string[0])
        };
        var plan = ModActionPlan.FoldersToEnable(new[] { "Child" }, mods);
        Assert.Empty(ModActionPlan.ExtraStateChanges(plan, new[] { "Child" }, mods, true));
    }

    [Fact]
    public void Disable_SkipsPromptWhenDependentAlreadyOff()
    {
        var mods = new[]
        {
            new ModNode("Base", "base", true, new string[0]),
            new ModNode("Child", "child", false, new[] { "base" })
        };
        var plan = ModActionPlan.FoldersToDisable(new[] { "Base" }, mods);
        Assert.Empty(ModActionPlan.ExtraStateChanges(plan, new[] { "Base" }, mods, false));
        Assert.Equal(new[] { "Child" }, ModActionPlan.ExtraStateChanges(plan, new[] { "Base" }, new[]
        {
            new ModNode("Base", "base", true, new string[0]),
            new ModNode("Child", "child", true, new[] { "base" })
        }, false));
    }

    [Fact]
    public void ListWithoutIsAdmin_HidesUpload()
    {
        var catalog = MarketClient.ParseList("[{\"id\":\"m\",\"name\":\"示例\",\"latestVersion\":\"1.0.0\"}]");
        Assert.Null(catalog.IsAdmin);
        Assert.False(catalog.ShowUpload);
        Assert.Equal("m", catalog.Mods[0].Id);

        Assert.True(MarketClient.ParseList("{\"isAdmin\":true,\"mods\":[]}").ShowUpload);
        Assert.False(MarketClient.ParseList("{\"isAdmin\":false,\"mods\":[]}").ShowUpload);
    }

    [Fact]
    public void Detail_ReadsDescriptionAndDependencies()
    {
        var detail = MarketClient.ParseDetail("{\"name\":\"甲\",\"description\":\"简介\",\"author\":\"乙\",\"latestVersion\":\"1.0\",\"downloadCount\":3,\"dependencies\":[{\"id\":\"need\",\"hard\":true},{\"id\":\"soft\",\"hard\":false}],\"versions\":[\"1.0\"]}");
        Assert.Equal("简介", detail.Description);
        Assert.Equal("need", detail.Dependencies[0].Id);
        Assert.True(detail.Dependencies[0].Hard);
        Assert.False(detail.Dependencies[1].Hard);
    }

    [Fact]
    public void InstalledLabel_UsesManifestNameFromZip()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmm-label-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            var folder = Path.Combine(root, "profile", "plugins", "com.example.mod");
            Directory.CreateDirectory(folder);
            var zipPath = Path.Combine(root, "com.example.mod_1.0.0.zip");
            using (var stream = File.Create(zipPath))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("Manifest.json");
                using (var writer = new StreamWriter(entry.Open()))
                    writer.Write("{\"id\":\"com.example.mod\",\"name\":\"训练器开关\",\"version\":\"1.0.0\"}");
            }

            var label = InstalledMods.ReadLabel(folder, "com.example.mod", root);
            Assert.Equal("训练器开关", label.Name);
            Assert.Equal("1.0.0", label.Version);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void LogRead_WorksWhileAnotherProcessIsWriting()
    {
        var path = Path.Combine(Path.GetTempPath(), "mmm-log-" + Guid.NewGuid().ToString("n") + ".log");
        File.WriteAllText(path, "[Info:test] start\r\n");
        try
        {
            using (var writer = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
            {
                var lines = LogFile.ReadLines(path);
                Assert.Contains("[Info:test] start", lines);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void LocalZip_PromptsWhenDownloadIsNewer()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmm-zipplan-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "mod.a_1.2.0.zip"), "zip");
            var plan = LocalZipPlan.NewerThanInstalled(root, new[] { new InstalledMod("mod.a", "1.0.0") });
            Assert.Single(plan);
            Assert.Equal("1.2.0", plan[0].RemoteVersion);
            Assert.Equal("1.0.0", plan[0].LocalVersion);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void CfgRange_ParsesFromTo()
    {
        Assert.True(CfgRange.TryParse("From 0 to 100", out var min, out var max));
        Assert.Equal(0, min);
        Assert.Equal(100, max);
        Assert.False(CfgRange.TryParse("可选值很多，直接填写", out _, out _));
    }

    [Fact]
    public void DownloadProgress_FormatsReceivedAndTotal()
    {
        Assert.Equal(0, DownloadText.Percent(0, null));
        Assert.Equal(50, DownloadText.Percent(512, 1024));
        Assert.Equal(100, DownloadText.Percent(2000, 1024));
        Assert.Equal("1 KB / 2 KB", DownloadText.Detail(1024, 2048));
        Assert.Equal("1.5 MB", DownloadText.Size(1536 * 1024));
        Assert.Equal("正在连接", DownloadText.Detail(0, null));
    }

    [Fact]
    public async System.Threading.Tasks.Task DownloadCopy_ReportsBytes()
    {
        var input = new MemoryStream(new byte[2500]);
        var output = new MemoryStream();
        long last = 0;
        long? seenTotal = null;
        await DownloadHub.CopyAsync(input, output, 2500, (got, total) =>
        {
            last = got;
            seenTotal = total;
        });
        Assert.Equal(2500, last);
        Assert.Equal(2500, seenTotal);
        Assert.Equal(2500, output.Length);
    }

    [Fact]
    public void DownloadHub_RemovesFinishedJob()
    {
        var job = DownloadHub.Begin("sample");
        try
        {
            job.Report(10, 20);
            var snap = DownloadHub.Snapshot();
            Assert.Contains(snap, item => item.Title == "sample" && item.Received == 10 && item.Total == 20);
        }
        finally
        {
            job.Dispose();
        }

        Assert.DoesNotContain(DownloadHub.Snapshot(), item => item.Title == "sample");
    }

    [Fact]
    public void Sort_UsesDownloadUpdateAndAuthor()
    {
        var older = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var newer = new DateTime(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);
        var first = new ModSortKeyset("甲", "1.0", "zeta", older, newer);
        var second = new ModSortKeyset("乙", "1.2", "alpha", newer, older);
        Assert.True(ModListSort.Compare(ModListSort.DownloadTime, true, first, second) < 0);
        Assert.True(ModListSort.Compare(ModListSort.UpdateTime, true, first, second) > 0);
        Assert.True(ModListSort.Compare(ModListSort.Author, true, first, second) > 0);
        Assert.True(ModListSort.Compare(ModListSort.Version, true, first, second) < 0);
        Assert.True(ModListSort.Compare(ModListSort.Version, false, first, second) > 0);
    }

    [Fact]
    public void PackageTimes_UsesEarliestDownloadAndLatestWrite()
    {
        var root = Path.Combine(Path.GetTempPath(), "mmm-times-" + Guid.NewGuid().ToString("n"));
        var download = Path.Combine(root, "download");
        Directory.CreateDirectory(download);
        var folder = Path.Combine(root, "plugins", "mod.a");
        Directory.CreateDirectory(folder);
        try
        {
            var oldZip = Path.Combine(download, "mod.a_1.0.0.zip");
            var newZip = Path.Combine(download, "mod.a_1.2.0.zip");
            File.WriteAllText(oldZip, "a");
            File.WriteAllText(newZip, "b");
            var first = new DateTime(2026, 10, 1, 1, 0, 0, DateTimeKind.Utc);
            var last = new DateTime(2026, 10, 3, 1, 0, 0, DateTimeKind.Utc);
            File.SetCreationTimeUtc(oldZip, first);
            File.SetLastWriteTimeUtc(oldZip, first);
            File.SetCreationTimeUtc(newZip, last);
            File.SetLastWriteTimeUtc(newZip, last);

            var times = InstalledMods.PackageTimes(root, folder, "mod.a", "mod.a");
            Assert.Equal(first, times.Download);
            Assert.Equal(last, times.Update);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
