namespace Solace.AdminPanel.Programs;

internal static partial class ProgramsLogs
{
    [LoggerMessage(Level = LogLevel.Error, Message = "{ProgramName} executable doesn't exits: {ExecutablePath}")]
    public static partial void LogExecutableNotFound(ILogger logger, string ProgramName, string ExecutablePath);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not find {Name}, expected '{Path}', with minimum version {Version}")]
    public static partial void LogCouldNotFindVersionedFile(ILogger logger, string Name, string Path, Version Version);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Running {ProgramName}")]
    public static partial void LogRunning(ILogger logger, string ProgramName);
}