#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

// Keep Windows speech outside the Unity player so a missing voice or a failed
// speech engine cannot interrupt the simulation.
internal sealed class DriverRadioSpeech : IDisposable
{
    private const string StopCommand = "!";
    private const string SpeechScript = @"
$ErrorActionPreference = 'Stop'
$voice = New-Object -ComObject SAPI.SpVoice
if ($voice.GetVoices().Count -eq 0) { exit }
while ($null -ne ($line = [Console]::ReadLine())) {
    if ($line -eq '!') {
        [void]$voice.Speak('', 19)
        continue
    }
    $flags = if ($line[0] -eq '+') { 19 } else { 17 }
    $voice.Volume = [int]$line.Substring(1, 3)
    $text = [Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($line.Substring(4)))
    [void]$voice.Speak($text, $flags)
}
[void]$voice.Speak('', 19)
";

    private readonly BlockingCollection<string> commands = new(8);

    internal DriverRadioSpeech()
    {
        _ = Task.Run(RunSpeech);
    }

    internal void Speak(string text, bool urgent, int volume)
    {
        if (urgent)
            ClearPending();
        string payload = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        string level = volume.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
        commands.TryAdd((urgent ? "+" : "-") + level + payload);
    }

    internal void Stop()
    {
        ClearPending();
        commands.TryAdd(StopCommand);
    }

    private void ClearPending()
    {
        while (commands.TryTake(out _)) { }
    }

    public void Dispose()
    {
        Stop();
        commands.CompleteAdding();
    }

    private void RunSpeech()
    {
        Process process = null;
        try
        {
            string systemDirectory = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string executable = Path.Combine(systemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
            string script = Convert.ToBase64String(Encoding.Unicode.GetBytes(SpeechScript));
            var start = new ProcessStartInfo(executable, "-NoLogo -NoProfile -NonInteractive -EncodedCommand " + script)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true
            };
            process = Process.Start(start);
            if (process == null)
                return;

            using var input = process.StandardInput;
            foreach (string command in commands.GetConsumingEnumerable())
                input.WriteLine(command);
        }
        catch (Exception)
        {
            // Speech is optional. The caller has already displayed the message.
        }
        finally
        {
            if (process != null)
            {
                try
                {
                    if (!process.WaitForExit(500))
                        process.Kill();
                }
                catch (Exception)
                {
                    // The worker may have already exited or become inaccessible.
                }
                process.Dispose();
            }
        }
    }
}
#endif
