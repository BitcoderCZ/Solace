namespace Solace.LauncherUI.Programs;

internal static partial class ProgramsLogs
{
    [LoggerMessage(Level = LogLevel.Error, Message = "{ProgramName} executable doesn't exits: {ExecutablePath}")]
    public static partial void LogExecutableNotFound(ILogger logger, string ProgramName, string ExecutablePath);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Running {ProgramName}")]
    public static partial void LogRunning(ILogger logger, string ProgramName);
}