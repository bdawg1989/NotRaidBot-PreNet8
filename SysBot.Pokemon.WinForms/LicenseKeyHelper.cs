using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using System.Management;
using System.Collections.Generic;
using System;

public static class LicenseKeyHelper
{
    private static readonly string licenseFilePath = "license.key";

    public static void SaveLicenseKey(string key)
    {
        File.WriteAllText(licenseFilePath, key);
    }
    public static void DeleteLicenseKey()
    {
        if (File.Exists(licenseFilePath))
            File.Delete(licenseFilePath);
    }

    public static string ReadLicenseKey()
    {
        if (File.Exists(licenseFilePath))
            return File.ReadAllText(licenseFilePath);
        else
            return null;
    }
    public static string GetCpuId()
    {
        string cpuId = string.Empty;
        ManagementClass managementClass = new ManagementClass("win32_processor");
        ManagementObjectCollection managementObjectCollection = managementClass.GetInstances();

        foreach (ManagementObject managementObject in managementObjectCollection)
        {
            cpuId = managementObject.Properties["processorID"].Value.ToString();
            break;
        }

        return cpuId;
    }
    public static async Task<bool> ValidateLicenseAsync(string licenseKey)
    {
        using (HttpClient httpClient = new HttpClient())
        {
            string cpuId = GetCpuId();

            var content = new FormUrlEncodedContent(new[]
            {
            new KeyValuePair<string, string>("license_key", licenseKey),
            new KeyValuePair<string, string>("cpu_id", cpuId)
        });

            var response = await httpClient.PostAsync("https://genpkm.com/rblicenses/validate_license.php", content);
            var responseString = await response.Content.ReadAsStringAsync();

            JObject jsonResponse = JObject.Parse(responseString);
            if ((string)jsonResponse["status"] == "success")
            {
                return true;
            }
        }

        return false;
    }
    public static async Task<string> ValidateLicenseAndFetchNameAsync(string licenseKey)
    {
        using (HttpClient httpClient = new HttpClient())
        {
            string cpuId = GetCpuId();
            var content = new FormUrlEncodedContent(new[]
            {
            new KeyValuePair<string, string>("license_key", licenseKey),
            new KeyValuePair<string, string>("cpu_id", cpuId)
        });

            var response = await httpClient.PostAsync("https://genpkm.com/rblicenses/validate_license.php", content);
            var responseString = await response.Content.ReadAsStringAsync();

            JObject jsonResponse = JObject.Parse(responseString);
            if ((string)jsonResponse["status"] == "success")
            {
                return (string)jsonResponse["discord_name"];
            }
        }
        return string.Empty;
    }
}
