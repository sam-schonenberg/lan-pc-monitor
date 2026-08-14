namespace PCMonitor.Application.Data;
public static class DatabaseConstants
{
    public const string FileName = "lanpcmonitor.db3";
    public static string Path => System.IO.Path.Combine(FileSystem.AppDataDirectory, FileName);
}
