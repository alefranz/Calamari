﻿#r "System.Security, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Net;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Security.Principal;
using System.Linq;

public static class Octopus
{
    public static OctopusParametersDictionary Parameters { get; private set; }

    static Octopus()
    {
        InitializeDefaultProxy();
    }

    public static void Initialize(string password)
    {
        if (Parameters != null)
        {
            throw new Exception("Octopus can only be initialized once.");
        }
        Parameters = new OctopusParametersDictionary(password);
        LogEnvironmentInformation();
    }

    static void LogEnvironmentInformation()
    {
        if (Parameters.ContainsKey("Octopus.Action.Script.SuppressEnvironmentLogging") && Parameters["Octopus.Action.Script.SuppressEnvironmentLogging"] == "True")
            return;

        var environmentInformationStamp = $"ScriptCS Environment Information:{Environment.NewLine}" +
            $"  {string.Join($"{Environment.NewLine}  ", SafelyGetEnvironmentInformation())}";

        Console.WriteLine("##octopus[stdout-verbose]");
        Console.WriteLine(environmentInformationStamp);
        Console.WriteLine("##octopus[stdout-default]");
    }

    #region Logging Helpers

    static string[] SafelyGetEnvironmentInformation()
    {
        var envVars = SafelyGetEnvironmentVars()
            .Concat(SafelyGetPathVars())
            .Concat(SafelyGetProcessVars());
        return envVars.ToArray();
    }

    static IEnumerable<string> SafelyGetEnvironmentVars()
    {
        try
        {
            return GetEnvironmentVars();
        }
        catch
        {
            // Fail silently
            return Enumerable.Empty<string>();
        }
    }
    static IEnumerable<string> GetEnvironmentVars()
    {
        yield return $"OperatingSystem: {Environment.OSVersion.ToString()}";
        yield return $"OsBitVersion: {(Environment.Is64BitOperatingSystem ? "x64" : "x86")}";
        yield return $"Is64BitProcess: {Environment.Is64BitProcess.ToString()}";
        yield return $"CurrentUser: {System.Security.Principal.WindowsIdentity.GetCurrent().Name}";
        yield return $"MachineName: {Environment.MachineName}";
        yield return $"ProcessorCount: {Environment.ProcessorCount.ToString()}";
    }

    static IEnumerable<string> SafelyGetPathVars()
    {
        try
        {
            return GetPathVars();
        }
        catch
        {
            // Fail silently
            return Enumerable.Empty<string>();
        }
    }
    static IEnumerable<string> GetPathVars()
    {
        yield return $"CurrentDirectory: {Directory.GetCurrentDirectory()}";
        yield return $"TempDirectory: {Path.GetTempPath()}";
    }

    static IEnumerable<string> SafelyGetProcessVars()
    {
        try
        {
            return GetProcessVars();
        }
        catch
        {
            // Fail silently
            return Enumerable.Empty<string>();
        }
    }
    static IEnumerable<string> GetProcessVars()
    {
        yield return $"HostProcessName: {Process.GetCurrentProcess().ProcessName}";
    }

    #endregion

    public class OctopusParametersDictionary : System.Collections.Generic.Dictionary<string, string>
    {
        private byte[] Key { get; set; }

        public OctopusParametersDictionary(string key) : base(System.StringComparer.OrdinalIgnoreCase)
        {
            Key = Convert.FromBase64String(key);
            /*{{VariableDeclarations}}*/
        }

        public string DecryptString(string encrypted, string iv)
        {
            using (var algorithm = new AesCryptoServiceProvider()
            {
                Mode = CipherMode.CBC,
                Padding = PaddingMode.PKCS7,
                KeySize = 128,
                BlockSize = 128
            })
            {
                algorithm.Key = Key;
                algorithm.IV = Convert.FromBase64String(iv);
                using (var dec = algorithm.CreateDecryptor())
                using (var ms = new MemoryStream(Convert.FromBase64String(encrypted)))
                using (var cs = new CryptoStream(ms, dec, CryptoStreamMode.Read))
                using (var sr = new StreamReader(cs, Encoding.UTF8))
                {
                    return sr.ReadToEnd();
                }
            }
        }
    }

    private static string EncodeServiceMessageValue(string value)
    {
        var valueBytes = System.Text.Encoding.UTF8.GetBytes(value);
        return Convert.ToBase64String(valueBytes);
    }

    public static void SetVariable(string name, string value)
    {
        name = EncodeServiceMessageValue(name);
        value = EncodeServiceMessageValue(value);

        Parameters[name] = value;

        Console.WriteLine("##octopus[setVariable name='{0}' value='{1}']", name, value);
    }

    public static void CreateArtifact(string path, string fileName = null)
    {
        if (fileName == null)
        {
            fileName = System.IO.Path.GetFileName(path);
        }

        fileName = EncodeServiceMessageValue(fileName);

        var length = System.IO.File.Exists(path) ? new System.IO.FileInfo(path).Length.ToString() : "0";
        length = EncodeServiceMessageValue(length);

        path = System.IO.Path.GetFullPath(path);
        path = EncodeServiceMessageValue(path);

        Console.WriteLine("##octopus[createArtifact path='{0}' name='{1}' length='{2}']", path, fileName, length);
    }

    public static void InitializeDefaultProxy()
    {
        var proxyUsername = Environment.GetEnvironmentVariable("TentacleProxyUsername");
        var proxyPassword = Environment.GetEnvironmentVariable("TentacleProxyPassword");
        var proxyHost = Environment.GetEnvironmentVariable("TentacleProxyHost");
        var proxyPortText = Environment.GetEnvironmentVariable("TentacleProxyPort");
        int proxyPort;
        int.TryParse(proxyPortText, out proxyPort);

        var useSystemProxy = string.IsNullOrWhiteSpace(proxyHost);

        var proxy = useSystemProxy
            ? WebRequest.GetSystemWebProxy()
            : new WebProxy(new UriBuilder("http", proxyHost, proxyPort).Uri);

        var useDefaultCredentials = string.IsNullOrWhiteSpace(proxyUsername);

        proxy.Credentials = useDefaultCredentials
            ? useSystemProxy
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential()
            : new NetworkCredential(proxyUsername, proxyPassword);

        WebRequest.DefaultWebProxy = proxy;
    }
}

Octopus.Initialize(Env.ScriptArgs[Env.ScriptArgs.Count - 1]);