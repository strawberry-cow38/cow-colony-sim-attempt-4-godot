using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CowColonyLauncher;

public sealed class MainForm : Form
{
    private const string RepoUrl = "https://github.com/strawberry-cow38/cow-colony-sim-attempt-4-godot.git";
    private const string ConfigFileName = "launcher.cfg";
    private const string DefaultRepoFolderName = "repo";

    private readonly Button _play;
    private readonly Button _update;
    private readonly Button _build;
    private readonly Button _openFolder;
    private readonly Button _setRepo;
    private readonly Button _exit;
    private readonly TextBox _log;
    private readonly Label _repoLabel;
    private string _repoRoot;

    public MainForm()
    {
        Text = "Cow Colony Sim — Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(820, 520);
        BackColor = Color.FromArgb(28, 30, 36);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10f);

        _repoRoot = ResolveRepoRoot();

        _repoLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 28,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(8, 0, 8, 0),
            ForeColor = Color.LightGray,
        };

        var buttonBar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 56,
            Padding = new Padding(8),
            BackColor = Color.FromArgb(38, 40, 48),
        };

        _play = MakeButton("Play", Color.FromArgb(64, 140, 80));
        _update = MakeButton("Update", Color.FromArgb(70, 110, 170));
        _build = MakeButton("Build", Color.FromArgb(110, 90, 150));
        _openFolder = MakeButton("Open Folder", Color.FromArgb(80, 80, 90));
        _setRepo = MakeButton("Set Repo...", Color.FromArgb(80, 80, 90));
        _exit = MakeButton("Exit", Color.FromArgb(150, 60, 60));

        buttonBar.Controls.AddRange(new Control[] { _play, _update, _build, _openFolder, _setRepo, _exit });

        _log = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(18, 20, 24),
            ForeColor = Color.LightGreen,
            Font = new Font("Cascadia Mono", 9f, FontStyle.Regular, GraphicsUnit.Point),
            BorderStyle = BorderStyle.None,
        };

        Controls.Add(_log);
        Controls.Add(buttonBar);
        Controls.Add(_repoLabel);

        _play.Click += async (_, _) => await PlayAsync();
        _update.Click += async (_, _) => await UpdateAsync();
        _build.Click += async (_, _) => await BuildAsync();
        _openFolder.Click += (_, _) => OpenFolder();
        _setRepo.Click += (_, _) => PickRepoFolder();
        _exit.Click += (_, _) => Close();

        RefreshRepoLabel();
        WriteLog($"launcher ready. repo: {_repoRoot}");
        if (!RepoExists())
        {
            WriteLog("repo not present yet — hit Update to clone the latest from github.");
        }
    }

    private static Button MakeButton(string text, Color accent) => new()
    {
        Text = text,
        Width = 120,
        Height = 36,
        FlatStyle = FlatStyle.Flat,
        BackColor = accent,
        ForeColor = Color.White,
        Margin = new Padding(4),
        FlatAppearance = { BorderSize = 0 },
    };

    private static string ConfigPath =>
        Path.Combine(AppContext.BaseDirectory, ConfigFileName);

    private static string DefaultRepoPath =>
        Path.Combine(AppContext.BaseDirectory, DefaultRepoFolderName);

    private string ResolveRepoRoot()
    {
        if (File.Exists(ConfigPath))
        {
            var saved = File.ReadAllText(ConfigPath).Trim();
            if (!string.IsNullOrEmpty(saved)) return saved;
        }
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "CowColonySim.sln")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return DefaultRepoPath;
    }

    private bool RepoExists() =>
        File.Exists(Path.Combine(_repoRoot, "CowColonySim.sln"));

    private void RefreshRepoLabel()
    {
        var status = RepoExists() ? "" : " (missing — Update will clone)";
        _repoLabel.Text = $"Repo: {_repoRoot}{status}";
    }

    private void SaveRepoConfig()
    {
        try { File.WriteAllText(ConfigPath, _repoRoot); }
        catch (Exception ex) { WriteLog($"! could not save config: {ex.Message}"); }
    }

    private void PickRepoFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Pick where the cow-colony-sim repo lives (or should be cloned to)",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
            InitialDirectory = Directory.Exists(_repoRoot) ? _repoRoot : AppContext.BaseDirectory,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        _repoRoot = dlg.SelectedPath;
        SaveRepoConfig();
        RefreshRepoLabel();
        WriteLog($"repo path set: {_repoRoot}");
    }

    private void OpenFolder()
    {
        var target = Directory.Exists(_repoRoot) ? _repoRoot : AppContext.BaseDirectory;
        Process.Start(new ProcessStartInfo("explorer.exe", target) { UseShellExecute = true });
    }

    private async Task PlayAsync()
    {
        SetBusy(true);
        try
        {
            if (!RepoExists())
            {
                WriteLog("! no repo. hit Update first to clone it.");
                return;
            }
            WriteLog("> launching godot...");
            await RunAsync("godot", $"--path \"{_repoRoot}\"", _repoRoot);
        }
        finally { SetBusy(false); }
    }

    private async Task UpdateAsync()
    {
        SetBusy(true);
        try
        {
            int rc;
            if (!RepoExists())
            {
                Directory.CreateDirectory(_repoRoot);
                if (Directory.GetFileSystemEntries(_repoRoot).Length > 0)
                {
                    WriteLog($"! target folder not empty and not a repo: {_repoRoot}");
                    WriteLog("  pick an empty folder via Set Repo... or delete its contents.");
                    return;
                }
                WriteLog($"> git clone {RepoUrl} \"{_repoRoot}\"");
                rc = await RunAsync("git", $"clone {RepoUrl} \"{_repoRoot}\"", AppContext.BaseDirectory);
                if (rc != 0)
                {
                    WriteLog($"! git clone exited with {rc}");
                    return;
                }
                RefreshRepoLabel();
            }
            else
            {
                WriteLog("> git pull --ff-only");
                rc = await RunAsync("git", "pull --ff-only", _repoRoot);
                if (rc != 0)
                {
                    WriteLog($"! git pull exited with {rc}, stopping update");
                    return;
                }
            }

            WriteLog("> dotnet build CowColonySim.csproj");
            rc = await RunAsync("dotnet", "build CowColonySim.csproj --nologo", _repoRoot);
            WriteLog(rc == 0 ? "✓ update complete" : $"! build failed ({rc})");
        }
        finally { SetBusy(false); }
    }

    private async Task BuildAsync()
    {
        SetBusy(true);
        try
        {
            if (!RepoExists())
            {
                WriteLog("! no repo. hit Update first to clone it.");
                return;
            }
            WriteLog("> dotnet build CowColonySim.csproj");
            var rc = await RunAsync("dotnet", "build CowColonySim.csproj --nologo", _repoRoot);
            WriteLog(rc == 0 ? "✓ build complete" : $"! build failed ({rc})");
        }
        finally { SetBusy(false); }
    }

    private async Task<int> RunAsync(string fileName, string args, string workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = args,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        try
        {
            using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            proc.OutputDataReceived += (_, e) => { if (e.Data != null) WriteLog(e.Data); };
            proc.ErrorDataReceived += (_, e) => { if (e.Data != null) WriteLog(e.Data); };
            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync();
            return proc.ExitCode;
        }
        catch (Exception ex)
        {
            WriteLog($"! failed to launch {fileName}: {ex.Message}");
            return -1;
        }
    }

    private void SetBusy(bool busy)
    {
        if (InvokeRequired) { Invoke(() => SetBusy(busy)); return; }
        _play.Enabled = !busy;
        _update.Enabled = !busy;
        _build.Enabled = !busy;
        _setRepo.Enabled = !busy;
    }

    private void WriteLog(string line)
    {
        if (InvokeRequired) { Invoke(() => WriteLog(line)); return; }
        _log.AppendText(line + Environment.NewLine);
    }
}
