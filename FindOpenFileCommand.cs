using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Runtime.InteropServices;

namespace FindOpenFiles
{
    [Cmdlet(VerbsCommon.Find, "OpenFile", DefaultParameterSetName = AllParameterSet)]
    public class FindOpenFileCommand : PSCmdlet
    {
        private const string AllParameterSet = "All";
        private const string FileParameterSet = "File";
        private const string ProcessParameterSet = "Process";

        // Script run on the target computer(s). It loads this module's assembly directly
        // from the bytes streamed over the remote session, so the module does not need to be
        // installed on the target computer(s), and then re-invokes Find-OpenFile locally there.
        private const string RemoteScript = @"
param(
    [byte[]] $AssemblyBytes,
    [string] $ParameterSet,
    [bool] $System,
    [string] $FilePath,
    [int] $ProcessId
)

$assembly = [System.Reflection.Assembly]::Load($AssemblyBytes)
Import-Module -Assembly $assembly -Force

switch ($ParameterSet) {
    'File'    { Find-OpenFile -FilePath $FilePath }
    'Process' { Find-OpenFile -Process (Get-Process -Id $ProcessId) }
    default   { if ($System) { Find-OpenFile -System } else { Find-OpenFile } }
}
";

        [Parameter(ParameterSetName = AllParameterSet)]
        public SwitchParameter System { get; set; }
        [Parameter(ParameterSetName = FileParameterSet, Mandatory = true, ValueFromPipeline = true)]
        public string FilePath { get; set; }
        [Parameter(ParameterSetName = ProcessParameterSet, Mandatory = true, ValueFromPipeline = true)]
        public Process Process { get; set; }

        [Parameter(ParameterSetName = AllParameterSet)]
        [Parameter(ParameterSetName = FileParameterSet)]
        [Parameter(ParameterSetName = ProcessParameterSet)]
        public string[] ComputerName { get; set; }

        [Parameter(ParameterSetName = AllParameterSet)]
        [Parameter(ParameterSetName = FileParameterSet)]
        [Parameter(ParameterSetName = ProcessParameterSet)]
        [Credential]
        [Alias("Credentials")]
        public PSCredential Credential { get; set; }

        protected override void ProcessRecord()
        {
            if (ComputerName != null && ComputerName.Length > 0)
            {
                InvokeRemotely();
                return;
            }

            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                throw new Exception("This cmdlet is only supported on Windows.");
            }

            if (ParameterSetName == AllParameterSet)
            {
                if (System)
                {
                    WriteObject(WalkmanLib.GetFileLocks.GetAllHandles.GetSystemHandles(), true);
                }
                else
                {
                    WriteObject(WalkmanLib.GetFileLocks.GetAllHandles.GetFileHandles(), true);
                }
            }
            else if (ParameterSetName == FileParameterSet)
            {
                FilePath = GetUnresolvedProviderPathFromPSPath(FilePath);

                // Check if the path is a directory
                if (Directory.Exists(FilePath))
                {
                    // For directories, use the GetAllHandles approach
                    WriteObject(WalkmanLib.GetFileLocks.GetAllHandles.GetProcessesLockingDirectory(FilePath), true);
                }
                else
                {
                    // For files, use the RestartManager approach
                    WriteObject(WalkmanLib.RestartManager.GetLockingProcesses(FilePath), true);
                }
            }
            else if (ParameterSetName == ProcessParameterSet)
            {
                WriteObject(WalkmanLib.GetFileLocks.GetAllHandles.GetFileHandles().Where(m => m.ProcessId == Process.Id), true);
            }            
        }

        private void InvokeRemotely()
        {
            string assemblyPath = typeof(FindOpenFileCommand).Assembly.Location;
            if (string.IsNullOrEmpty(assemblyPath) || !File.Exists(assemblyPath))
            {
                throw new Exception("Unable to locate the FindOpenFile assembly to send to the remote computer(s).");
            }

            byte[] assemblyBytes = File.ReadAllBytes(assemblyPath);
            int processId = Process != null ? Process.Id : 0;

            using (var powerShell = PowerShell.Create())
            {
                powerShell.AddCommand("Invoke-Command")
                    .AddParameter("ComputerName", ComputerName)
                    .AddParameter("ScriptBlock", ScriptBlock.Create(RemoteScript))
                    .AddParameter("ArgumentList", new object[]
                    {
                        assemblyBytes,
                        ParameterSetName,
                        System.IsPresent,
                        FilePath,
                        processId
                    });

                // Use the caller's current credentials by default; only pass explicit
                // credentials when they were supplied via -Credential.
                if (Credential != null)
                {
                    powerShell.AddParameter("Credential", Credential);
                }

                System.Collections.ObjectModel.Collection<PSObject> results;
                try
                {
                    results = powerShell.Invoke();
                }
                catch (Exception ex)
                {
                    throw new Exception(
                        $"Failed to run Find-OpenFile on remote computer(s) '{string.Join(", ", ComputerName)}': {ex.Message}",
                        ex);
                }

                foreach (var error in powerShell.Streams.Error)
                {
                    WriteError(error);
                }

                WriteObject(results, true);
            }
        }
    }
}