using System.Diagnostics;
using System.Text;

namespace ParadisCompress.Core.Jobs;

/// <summary>The outcome of one ffmpeg or ffprobe run.</summary>
public sealed record ProcessResult(int ExitCode, string StandardOutput, IReadOnlyList<string> ErrorTail, bool Cancelled)
{
    public bool Succeeded => !Cancelled && ExitCode == 0;
    public string ErrorText => string.Join('\n', ErrorTail);
}

/// <summary>
/// Runs ffmpeg/ffprobe as a child process. Arguments are always passed as a list
/// (<see cref="ProcessStartInfo.ArgumentList"/>), never as a command string, so file names with spaces,
/// quotes, ampersands, percent signs or non-Latin characters reach FFmpeg unchanged and nothing goes
/// through a shell.
/// </summary>
public static class FfmpegProcess
{
    /// <summary>Called with every process this class starts, so the app can tie its lifetime to ours.</summary>
    public static Action<Process>? ProcessStarted { get; set; }

    private const int ErrorTailLines = 60;

    /// <param name="onStdoutLine">Each line of standard output as it arrives (ffmpeg's <c>-progress pipe:1</c>).</param>
    /// <param name="captureStdout">Collect standard output into the result (ffprobe's JSON).</param>
    public static async Task<ProcessResult> RunAsync(
        string exe, IReadOnlyList<string> args, CancellationToken cancel,
        Action<string>? onStdoutLine = null, bool captureStdout = false)
    {
        var psi = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = Path.GetTempPath(),
        };
        foreach (string a in args) psi.ArgumentList.Add(a);

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var stdout = captureStdout ? new StringBuilder() : null;
        var errTail = new Queue<string>();
        var stdoutDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) { stdoutDone.TrySetResult(); return; }
            if (stdout != null) lock (stdout) stdout.AppendLine(e.Data);
            try { onStdoutLine?.Invoke(e.Data); } catch { /* a progress consumer must never kill the run */ }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) { stderrDone.TrySetResult(); return; }
            lock (errTail)
            {
                errTail.Enqueue(e.Data);
                while (errTail.Count > ErrorTailLines) errTail.Dequeue();
            }
        };

        cancel.ThrowIfCancellationRequested();
        if (!process.Start()) throw new InvalidOperationException($"Could not start {Path.GetFileName(exe)}.");
        try { ProcessStarted?.Invoke(process); } catch { /* best effort */ }
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        bool cancelled = false;
        using (cancel.Register(() =>
        {
            cancelled = true;
            // Only ever the process this method started; ffmpeg starts no children of its own.
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { /* already gone */ }
        }))
        {
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAny(Task.WhenAll(stdoutDone.Task, stderrDone.Task), Task.Delay(5000)).ConfigureAwait(false);
        }

        string[] tail;
        lock (errTail) tail = errTail.ToArray();
        string outText;
        if (stdout != null) lock (stdout) outText = stdout.ToString();
        else outText = "";
        return new ProcessResult(process.ExitCode, outText, tail, cancelled || cancel.IsCancellationRequested);
    }
}
