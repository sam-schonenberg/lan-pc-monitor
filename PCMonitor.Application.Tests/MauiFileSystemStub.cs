// DatabaseConstants only needs this MAUI API when AppDatabase is created without an explicit test path.
public static class FileSystem
{
    public static string AppDataDirectory => Path.GetTempPath();
}
