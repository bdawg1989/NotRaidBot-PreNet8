using System.IO;

public static class LicenseKeyHelper
{
    private static readonly string licenseFilePath = "license.key";

    public static void SaveLicenseKey(string key)
    {
        File.WriteAllText(licenseFilePath, key);
    }

    public static string ReadLicenseKey()
    {
        if (File.Exists(licenseFilePath))
            return File.ReadAllText(licenseFilePath);
        else
            return null;
    }
}