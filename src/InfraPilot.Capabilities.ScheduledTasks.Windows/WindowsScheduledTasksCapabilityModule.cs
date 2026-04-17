namespace InfraPilot.Capabilities.ScheduledTasks.Windows;

using System.Diagnostics;
using System.Text;
using InfraPilot.Capabilities.Abstractions;
using InfraPilot.Contracts.Actions;
using InfraPilot.Contracts.Capabilities;
using InfraPilot.Contracts.ScheduledTasks;

public sealed class WindowsScheduledTasksCapabilityModule : ICapabilityModule
{
    private static readonly CapabilityDescriptorDto Descriptor = new(
        CapabilityKeys.ScheduledTasks,
        "Scheduled Tasks",
        "1.0.0",
        [
            new CapabilityActionDefinitionDto("run", "Run task", true, "Runs a scheduled task immediately.")
        ]);

    public CapabilityDescriptorDto Describe() => Descriptor;

    public async Task<CapabilitySnapshotResult> CollectSnapshotAsync(CancellationToken cancellationToken)
    {
        var rows = await ReadRowsFromSchtasksAsync(cancellationToken);
        var tasks = new List<ScheduledTaskInfoDto>();

        foreach (var row in rows)
        {
            var fullName = First(row, "TaskName", "Nombre de tarea");
            if (string.IsNullOrWhiteSpace(fullName) || IsHeaderLikeRow(fullName))
            {
                continue;
            }

            var (path, name) = SplitTaskNameAndPath(fullName);
            tasks.Add(new ScheduledTaskInfoDto(
                name,
                path,
                First(row, "Status", "Estado"),
                First(row, "Author", "Autor"),
                First(row, "Run As User", "Ejecutar como usuario"),
                First(row, "Last Run Time", "Hora de la ultima ejecucion"),
                First(row, "Next Run Time", "Hora de la proxima ejecucion"),
                First(row, "Last Result", "Resultado de la ultima ejecucion"),
                First(row, "Task To Run", "Tarea que se ejecutara")));
        }

        tasks.Sort((left, right) =>
            string.Compare($"{left.TaskPath}{left.TaskName}", $"{right.TaskPath}{right.TaskName}", StringComparison.OrdinalIgnoreCase));

        return new CapabilitySnapshotResult(
            CapabilityKeys.ScheduledTasks,
            "1.0.0",
            new ScheduledTaskSnapshotDto(tasks));
    }

    public async Task<CapabilityActionExecutionResult> ExecuteActionAsync(
        AgentActionCommandDto command,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(command.ActionKey, "run", StringComparison.OrdinalIgnoreCase))
        {
            return new CapabilityActionExecutionResult(false, $"Unsupported action '{command.ActionKey}'.", "Unsupported action.");
        }

        if (string.IsNullOrWhiteSpace(command.TargetKey))
        {
            return new CapabilityActionExecutionResult(false, "A scheduled task path is required.", "Missing target.");
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = $"/Run /TN \"{command.TargetKey}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = await process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            return new CapabilityActionExecutionResult(false, output, string.IsNullOrWhiteSpace(error) ? output : error);
        }

        return new CapabilityActionExecutionResult(true, string.IsNullOrWhiteSpace(output) ? $"Task '{command.TargetKey}' triggered." : output.Trim());
    }

    private static bool IsHeaderLikeRow(string value)
        => value.Contains("TaskName", StringComparison.OrdinalIgnoreCase)
           || value.Contains("Nombre de tarea", StringComparison.OrdinalIgnoreCase);

    private static async Task<List<Dictionary<string, string>>> ReadRowsFromSchtasksAsync(CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "schtasks",
                Arguments = "/query /fo csv /v",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            }
        };

        process.Start();
        var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        return ParseCsv(output);
    }

    private static (string Path, string Name) SplitTaskNameAndPath(string fullName)
    {
        var index = fullName.LastIndexOf('\\');
        if (index <= 0)
        {
            return ("\\", fullName.Trim('\\'));
        }

        return (fullName[..(index + 1)], fullName[(index + 1)..]);
    }

    private static string? First(IReadOnlyDictionary<string, string> row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static List<Dictionary<string, string>> ParseCsv(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (lines.Length == 0)
        {
            return [];
        }

        var headers = SplitCsvLine(lines[0]);
        var result = new List<Dictionary<string, string>>();

        foreach (var line in lines.Skip(1))
        {
            var values = SplitCsvLine(line);
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < headers.Count && index < values.Count; index++)
            {
                row[headers[index]] = values[index];
            }

            result.Add(row);
        }

        return result;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var values = new List<string>();
        var buffer = new StringBuilder();
        var inQuotes = false;

        foreach (var character in line)
        {
            if (character == '"')
            {
                inQuotes = !inQuotes;
                continue;
            }

            if (character == ',' && !inQuotes)
            {
                values.Add(buffer.ToString());
                buffer.Clear();
                continue;
            }

            buffer.Append(character);
        }

        values.Add(buffer.ToString());
        return values;
    }
}
