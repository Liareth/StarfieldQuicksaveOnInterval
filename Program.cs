using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using WindowsInput;
using WindowsInput.Native;

[DllImport("user32.dll")]
static extern IntPtr GetForegroundWindow();

[DllImport("user32.dll")]
static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

Config config = new(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Starfield", "Saves"));

if (File.Exists("quicksave.json"))
{
    Console.WriteLine("Loading quicksave.json... existing file found");
    config = JsonSerializer.Deserialize<Config>(File.ReadAllText("quicksave.json"))!;
}
else
{
    string path = Path.Combine(Directory.GetCurrentDirectory(), "quicksave.json");
    Console.WriteLine($"Loading quicksave.json... writing default settings to {path} because no existing file found");
    File.WriteAllText(path, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
}

if (!Directory.Exists(config.SaveDirectory))
{
    Console.WriteLine($"saveDirectory {config.SaveDirectory} does not exist");
    return;
}

CancellationTokenSource cancel = new();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancel.Cancel();
};

DateTime? quicksaveLastCopy = null;
InputSimulator inputSimulator = new();

while (!cancel.Token.IsCancellationRequested)
{
    try
    {
        Task.Delay(TimeSpan.FromSeconds(config.UpdateInterval), cancel.Token).Wait();
    }
    catch (AggregateException ex)
    {
        if (ex.InnerException is TaskCanceledException)
        {
            break;
        }

        throw;
    }

    GetWindowThreadProcessId(GetForegroundWindow(), out uint processId);
    Process process = Process.GetProcessById((int)processId);

    if (process.MainWindowTitle != config.ProcessName)
    {
        Console.WriteLine($"Skipping this update because {config.ProcessName} was not in focus");
        continue;
    }

    IEnumerable<string> saveFiles = Directory.EnumerateFiles(config.SaveDirectory);
    IEnumerable<(string, DateTime)> quicksaveFileCandidates = saveFiles
        .Where(x => Path.GetFileName(x).StartsWith("Quicksave0"))
        .Select(x => (x, File.GetLastWriteTime(x)))
        .OrderByDescending(x => x.Item2);

    int quicksaveFileCandidatesCount = quicksaveFileCandidates.Count();
    if (quicksaveFileCandidatesCount == 0)
    {
        Console.WriteLine($"Skipping this update because no quicksaves were found in '{config.SaveDirectory}'");
        continue;
    }

    (string quicksaveFilePath, DateTime quicksaveFileWriteTime) = quicksaveFileCandidates.First();
    quicksaveLastCopy ??= quicksaveFileWriteTime;

    if (quicksaveFileCandidatesCount > 1)
    {
        Console.WriteLine($"Found more than one quicksave file in '{config.SaveDirectory}'. " +
            $"Selected '{Path.GetFileName(quicksaveFilePath)}' as it was most recently modified. " +
            $"Candidates were:");

        string candidates = string.Join("\n  ", quicksaveFileCandidates.Select(x => $"'{Path.GetFileName(x.Item1)}'"));
        Console.WriteLine($"  {candidates}");
    }

    TimeSpan timeSinceLastQuicksave = DateTime.Now.Subtract(quicksaveFileWriteTime);

    // Handles copying (detect when quicksave has changed, copy it to standalone save file)
    if (config.QuicksaveCopy && quicksaveFileCandidatesCount > 0 && quicksaveFileWriteTime != quicksaveLastCopy)
    {
        int highestSaveId = saveFiles
            .Select(file => Regex.Match(Path.GetFileName(file), @"Save(\d+)_.*\.sfs"))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max();

        string savePath = quicksaveFilePath.Replace("Quicksave0", $"Save{highestSaveId + 1}");

        Console.WriteLine(
            $"Copying '{quicksaveFilePath}' to '{savePath}' because quicksave was " +
            $"modified {timeSinceLastQuicksave} ago (at {quicksaveFileWriteTime})");

        if (TryCopyFile(quicksaveFilePath, savePath))
        {
            quicksaveLastCopy = quicksaveFileWriteTime;
        }
    }

    // Handles saving (detect when it's been an interval after our most recent quicksave, make one)
    if (config.QuicksaveSave && timeSinceLastQuicksave >= TimeSpan.FromSeconds(config.QuicksaveSaveInterval))
    {
        Console.WriteLine(
            $"Sending F5 to {config.ProcessName} because quicksave was " +
            $"modified {timeSinceLastQuicksave} ago (at {quicksaveFileWriteTime})");

        inputSimulator.Keyboard.KeyDown(VirtualKeyCode.F5).Sleep(200).KeyUp(VirtualKeyCode.F5);
    }
}

bool TryCopyFile(string source, string dest)
{
    try
    {
        using FileStream original = File.Open(source, FileMode.Open, FileAccess.Read, FileShare.None);
        using FileStream copy = File.Create(dest);
        original.CopyTo(copy);
    }
    catch (Exception ex)
    {
        Console.WriteLine($"A wild {ex.GetType().Name} appeared when copying {source} to {dest}! It used {ex.Message}. It's super effective!");
        return false;
    }

    return true;
}

record Config(
    string SaveDirectory,
    string ProcessName = "Starfield",
    float UpdateInterval = 10.0f,
    bool QuicksaveSave = true,
    float QuicksaveSaveInterval = 120.0f,
    bool QuicksaveCopy = true);