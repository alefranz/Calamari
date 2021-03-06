using System;
using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Threading;
using Calamari.Util;

namespace Calamari.Integration.Processes
{
    public static class SilentProcessRunner
    {
        // ReSharper disable once InconsistentNaming
        private const int CP_OEMCP = 1;
        private static readonly Encoding oemEncoding;

        static SilentProcessRunner()
        {
            try
            {
                CPINFOEX info;
                if (GetCPInfoEx(CP_OEMCP, 0, out info))
                {
                    oemEncoding = Encoding.GetEncoding(info.CodePage);
                }
                else
                {
                    oemEncoding = Encoding.GetEncoding(850);
                }
            }
            catch (Exception)
            {
                Trace.WriteLine("Couldn't get default OEM encoding");
                oemEncoding = Encoding.UTF8;
            }
        }

        public static int ExecuteCommand(string executable, string arguments, string workingDirectory, Action<string> output, Action<string> error)
        {
            return ExecuteCommand(executable, arguments, workingDirectory, null, null, output, error);
        }

        public static int ExecuteCommand(string executable, string arguments, string workingDirectory, string userName, SecureString password, Action<string> output, Action<string> error)
        {
            try
            {
                using (var process = new Process())
                {
                    process.StartInfo.FileName = executable;
                    process.StartInfo.Arguments = arguments;
                    process.StartInfo.WorkingDirectory = workingDirectory;
                    process.StartInfo.UseShellExecute = false;
                    process.StartInfo.CreateNoWindow = true;
                    process.StartInfo.RedirectStandardOutput = true;
                    process.StartInfo.RedirectStandardError = true;
                    process.StartInfo.StandardOutputEncoding = oemEncoding;
                    process.StartInfo.StandardErrorEncoding = oemEncoding;

#if CAN_RUN_PROCESS_AS
                    if (!string.IsNullOrEmpty(userName) && password != null)
                    {
                        process.StartInfo.UserName = userName;
                        process.StartInfo.Password = password;

                        WindowStationAndDesktopAccess.GrantAccessToWindowStationAndDesktop(userName);

                        // Environment variables (such as {env:TentacleHome}) are usually inherited from the parent process.
                        // When running as a different user they are not inherited, so manually add them to the process.
                        AddTentacleEnvironmentVariablesToProcess(process.StartInfo);
                    }
#endif

                    using (var outputWaitHandle = new AutoResetEvent(false))
                    using (var errorWaitHandle = new AutoResetEvent(false))
                    {
                        process.OutputDataReceived += (sender, e) =>
                        {
                            try
                            {
                                if (e.Data == null)
                                    outputWaitHandle.Set();
                                else
                                    output(e.Data);
                            }
                            catch (Exception ex)
                            {
                                try
                                {
                                    error($"Error occured handling message: {ex.PrettyPrint()}");
                                }
                                catch
                                {
                                    // Ignore
                                }
                            }
                        };

                        process.ErrorDataReceived += (sender, e) =>
                        {
                            try
                            {
                                if (e.Data == null)
                                    errorWaitHandle.Set();
                                else
                                    error(e.Data);
                            }
                            catch (Exception ex)
                            {
                                try
                                {
                                    error($"Error occured handling message: {ex.PrettyPrint()}");
                                }
                                catch
                                {
                                    // Ignore
                                }
                            }
                        };

                        process.Start();

                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();

                        process.WaitForExit();

                        outputWaitHandle.WaitOne();
                        errorWaitHandle.WaitOne();

                        return process.ExitCode;
                    }
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Error when attempting to execute {executable}: {ex.Message}", ex);
            }
        }

#if CAN_RUN_PROCESS_AS
        static void AddTentacleEnvironmentVariablesToProcess(ProcessStartInfo processStartInfo)
        {
            foreach (DictionaryEntry environmentVariable in Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process))
            {
                var key = environmentVariable.Key.ToString();
                if (!key.StartsWith("Tentacle"))
                {
                    continue;
                }
                processStartInfo.EnvironmentVariables[key] = environmentVariable.Value.ToString();
            }
        }
#endif

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetCPInfoEx([MarshalAs(UnmanagedType.U4)] int CodePage, [MarshalAs(UnmanagedType.U4)] int dwFlags, out CPINFOEX lpCPInfoEx);

        private const int MAX_DEFAULTCHAR = 2;
        private const int MAX_LEADBYTES = 12;
        private const int MAX_PATH = 260;

        [StructLayout(LayoutKind.Sequential)]
        private struct CPINFOEX
        {
            [MarshalAs(UnmanagedType.U4)]
            public int MaxCharSize;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_DEFAULTCHAR)]
            public byte[] DefaultChar;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = MAX_LEADBYTES)]
            public byte[] LeadBytes;

            public char UnicodeDefaultChar;

            [MarshalAs(UnmanagedType.U4)]
            public int CodePage;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            public string CodePageName;
        }
    }
}