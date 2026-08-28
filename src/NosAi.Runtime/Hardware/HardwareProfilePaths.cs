namespace NosAi.Runtime.Hardware;

public static class HardwareProfilePaths
{
    public static string PlayAiDefaultProfile()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NosAi", "PlayAi", "hardware-profile.json");

    public static string GuardAiDefaultProfile(string appDataDirectory)
        => Path.Combine(appDataDirectory, "NosAi", "GuardAi", "hardware-profile.json");
}
