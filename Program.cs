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

Config config;

if (File.Exists("quicksave.json"))
{
    Console.WriteLine("quicksave.json config detected, loading");
    config = JsonSerializer.Deserialize<Config>(File.ReadAllText("quicksave.json"))!;
}
else
{
    config = new(
        ProcessName: "Starfield",
        SaveDirectory: Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "My Games", "Starfield", "Saves"),
        QuicksaveSave: true,
        QuicksaveSaveInterval: 120.0f,
        QuicksaveCopy: true);

    Console.WriteLine("quicksave.json config not detected, creating one and using defaults");
    File.WriteAllText("quicksave.json", JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
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
    Thread.Sleep(TimeSpan.FromSeconds(10));

    GetWindowThreadProcessId(GetForegroundWindow(), out uint processId);
    Process process = Process.GetProcessById((int)processId);

    if (process.MainWindowTitle != config.ProcessName)
    {
        continue;
    }

    IEnumerable<string> saveFiles = Directory.EnumerateFiles(config.SaveDirectory);
    string? quicksavePath = saveFiles.FirstOrDefault(x => Path.GetFileName(x).StartsWith("Quicksave0"));
    DateTime quicksaveWriteTime = quicksavePath == null ? DateTime.MinValue : File.GetLastWriteTime(quicksavePath);
    TimeSpan quicksaveTimesSinceWrite = DateTime.Now.Subtract(quicksaveWriteTime);
    quicksaveLastCopy ??= quicksaveWriteTime;

    // Handles copying (detect when quicksave has changed, copy it to standalone save file)
    if (config.QuicksaveCopy && quicksavePath != null && quicksaveWriteTime != quicksaveLastCopy)
    {
        int highestSaveId = saveFiles
            .Select(file => Regex.Match(Path.GetFileName(file), @"Save(\d+)_.*\.sfs"))
            .Where(match => match.Success)
            .Select(match => int.Parse(match.Groups[1].Value))
            .DefaultIfEmpty(0)
            .Max();

        string savePath = quicksavePath.Replace("Quicksave0", $"Save{highestSaveId + 1}");
        Console.WriteLine($"Quicksave modified at {quicksaveWriteTime}, copying from {quicksavePath} to {savePath}.");

        if (TryCopyFile(quicksavePath, savePath))
        {
            quicksaveLastCopy = quicksaveWriteTime;
        }
    }

    // Handles saving (detect when it's been an interval after our most recent quicksave, make one)
    if (config.QuicksaveSave && quicksaveTimesSinceWrite >= TimeSpan.FromSeconds(config.QuicksaveSaveInterval))
    {
        Console.WriteLine($"Sending F5. Time since last save: {quicksaveTimesSinceWrite}");
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
    string ProcessName,
    string SaveDirectory,
    bool QuicksaveSave,
    float QuicksaveSaveInterval,
    bool QuicksaveCopy);