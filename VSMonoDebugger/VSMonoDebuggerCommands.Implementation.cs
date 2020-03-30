﻿using Microsoft;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using SshFileSync;
using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using VSMonoDebugger.Services;
using VSMonoDebugger.Settings;
using VSMonoDebugger.Debuggers;
using VSMonoDebugger.Views;

namespace VSMonoDebugger
{
    internal sealed partial class VSMonoDebuggerCommands
    {
        private void InstallMenu(OleMenuCommandService commandService)
        {
            AddMenuItem(commandService, CommandIds.cmdDeployAndDebugOverSSH, SetMenuTextAndVisibility, DeployAndDebugOverSSHClicked);
            AddMenuItem(commandService, CommandIds.cmdDeployOverSSH, SetMenuTextAndVisibility, DeployOverSSHClicked);
            AddMenuItem(commandService, CommandIds.cmdDebugOverSSH, SetMenuTextAndVisibility, DebugOverSSHClicked);
            AddMenuItem(commandService, CommandIds.cmdAttachToMonoDebuggerWithoutSSH, SetMenuTextAndVisibility, AttachToMonoDebuggerWithoutSSHClicked);
            AddMenuItem(commandService, CommandIds.cmdBuildProjectWithMDBFiles, SetMenuTextAndVisibility, BuildProjectWithMDBFilesClicked);

            AddMenuItem(commandService, CommandIds.cmdOpenLogFile, CheckOpenLogFile, OpenLogFile);
            AddMenuItem(commandService, CommandIds.cmdOpenDebugSettings, null, OpenSSHDebugConfigDlg);
        }

        private void CheckOpenLogFile(object sender, EventArgs e)
        {
            var menuCommand = sender as OleMenuCommand;
            if (menuCommand != null)
            {
                menuCommand.Enabled = File.Exists(NLogService.LoggerPath);
            }
        }

        private void OpenLogFile(object sender, EventArgs e)
        {
            if (File.Exists(NLogService.LoggerPath))
            {
                Process.Start(NLogService.LoggerPath);
            }
            else
            {
                // TODO MessageBox
                MessageBox.Show(
                    $"Logfile {NLogService.LoggerPath} not found!",
                    "VSMonoDebugger", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void SetMenuTextAndVisibility(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var menuCommand = sender as OleMenuCommand;
            if (menuCommand != null)
            {
                var allDeviceSettings = UserSettingsManager.Instance.Load();
                var settings = allDeviceSettings.CurrentUserSettings;
                var debuggerName = settings.UseDotnetCoreDebugger ? "dotnet" : "mono";
                var withMdbFiles = settings.UseDotnetCoreDebugger ? "" : "with MDB files";
                var remoteName = settings.DeployAndDebugOnLocalWindowsSystem ? "Local" : "SSH";
                if (menuCommand.CommandID.ID == CommandIds.cmdAttachToMonoDebuggerWithoutSSH)
                {
                    menuCommand.Text = $"{GetMenuText(menuCommand.CommandID.ID)} [mono debugger] (TCP {settings.SSHHostIP})";
                    menuCommand.Enabled = settings.UseDotnetCoreDebugger == false && _monoExtension.IsStartupProjectAvailable();
                    menuCommand.Visible = settings.UseDotnetCoreDebugger == false && _monoExtension.IsStartupProjectAvailable();
                }
                else
                {
                    if (menuCommand.CommandID.ID == CommandIds.cmdBuildProjectWithMDBFiles)
                    {
                        menuCommand.Text = $"{GetMenuText(menuCommand.CommandID.ID)} {withMdbFiles}";
                    }
                    else
                    {
                        menuCommand.Text = $"{GetMenuText(menuCommand.CommandID.ID)} [{debuggerName} debugger] ({remoteName} {settings.SSHHostIP})";                        
                    }

                    menuCommand.Enabled = _monoExtension.IsStartupProjectAvailable();
                }
            }
        }

        private string GetMenuText(int commandId)
        {
            switch (commandId)
            {
                case CommandIds.cmdDeployAndDebugOverSSH:
                    return "Deploy, Run and Debug";
                case CommandIds.cmdDeployOverSSH:
                    return "Deploy";
                case CommandIds.cmdDebugOverSSH:
                    return "Run and Debug";
                case CommandIds.cmdAttachToMonoDebuggerWithoutSSH:
                    return "Attach to mono debugger";
                case CommandIds.cmdBuildProjectWithMDBFiles:
                    return "Build Startup Project";

                case CommandIds.cmdOpenLogFile:
                    return "Open Logfile";
                case CommandIds.cmdOpenDebugSettings:
                    return "Settings ...";
                default:
                    return $"Unknown CommandID {commandId}";
            }
        }

        private void CheckStartupProjects(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var menuCommand = sender as OleMenuCommand;
            if (menuCommand != null)
            {
                menuCommand.Enabled = _monoExtension.IsStartupProjectAvailable();
            }
        }

        private async void DeployAndDebugOverSSHClicked(object sender, EventArgs e)
        {
            await DeployAndRunCommandOverSSHAsync(DebuggerMode.DeployOverSSH | DebuggerMode.DebugOverSSH | DebuggerMode.AttachProcess);
        }

        private async void DeployOverSSHClicked(object sender, EventArgs e)
        {
            await DeployAndRunCommandOverSSHAsync(DebuggerMode.DeployOverSSH);
        }

        private async void DebugOverSSHClicked(object sender, EventArgs e)
        {
            await DeployAndRunCommandOverSSHAsync(DebuggerMode.DebugOverSSH | DebuggerMode.AttachProcess);
        }

        private async void AttachToMonoDebuggerWithoutSSHClicked(object sender, EventArgs e)
        {
            await DeployAndRunCommandOverSSHAsync(DebuggerMode.AttachProcess);
        }

        private async void BuildProjectWithMDBFilesClicked(object sender, EventArgs e)
        {
            await BuildProjectWithMDBFilesAsync();
        }

        private async void OpenSSHDebugConfigDlg(object sender, EventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // https://docs.microsoft.com/en-us/visualstudio/extensibility/creating-and-managing-modal-dialog-boxes?view=vs-2019
            var vsUIShell = await _asyncServiceProvider.GetServiceAsync(typeof(SVsUIShell)) as IVsUIShell;
            Assumes.Present(vsUIShell);

            var dlg = new DebugSettings(vsUIShell);
            vsUIShell.GetDialogOwnerHwnd(out IntPtr vsParentHwnd);
            vsUIShell.EnableModeless(0);
            try
            {
                WindowHelper.ShowModal(dlg, vsParentHwnd);
            }
            finally
            {
                vsUIShell.EnableModeless(1);
            }
        }

        [Flags]
        public enum DebuggerMode
        {
            DeployOverSSH = 0x1,
            DebugOverSSH = 0x2,
            AttachProcess = 0x4
        }

        public async Task<bool> DeployAndRunCommandOverSSHAsync(DebuggerMode debuggerMode)
        {
            // TODO error handling
            // TODO show ssh output stream
            // TODO stop monoRemoteSshDebugTask properly
            try
            {
                Logger.Info($"===== {nameof(DeployAndRunCommandOverSSHAsync)} =====");

                if (!_monoExtension.IsStartupProjectAvailable())
                {
                    Logger.Info($"No startup project/solution loaded yet.");
                    return false;
                }

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                UserSettings settings;
                DebugOptions debugOptions;
                SshDeltaCopy.Options options;
                CreateDebugOptions(out settings, out debugOptions, out options);

                if (debuggerMode.HasFlag(DebuggerMode.DeployOverSSH))
                {
                    await _monoExtension.BuildStartupProjectAsync();
                    if (settings.UseDotnetCoreDebugger == false)
                    {
                        await _monoExtension.CreateMdbForAllDependantProjectsAsync(HostOutputWindowEx.WriteLineLaunchError);
                    }
                }

                IDebugger debugger = settings.DeployAndDebugOnLocalWindowsSystem ? 
                    (IDebugger)new LocalWindowsDebugger() :
                    settings.UseDotnetCoreDebugger ? (IDebugger)new SSHDebuggerDotnet(options) : new SSHDebuggerMono(options);

                System.Threading.Tasks.Task<bool> monoRemoteSshDebugTask = null;

                if (debuggerMode.HasFlag(DebuggerMode.DeployOverSSH) && debuggerMode.HasFlag(DebuggerMode.DebugOverSSH))
                {
                    monoRemoteSshDebugTask = debugger.DeployRunAndDebugAsync(debugOptions, HostOutputWindowEx.WriteLaunchErrorAsync, settings.RedirectOutputOption);
                }
                else if (debuggerMode.HasFlag(DebuggerMode.DeployOverSSH))
                {
                    monoRemoteSshDebugTask = debugger.DeployAsync(debugOptions, HostOutputWindowEx.WriteLaunchErrorAsync, settings.RedirectOutputOption);
                }
                else if (debuggerMode.HasFlag(DebuggerMode.DebugOverSSH))
                {
                    monoRemoteSshDebugTask = debugger.RunAndDebugAsync(debugOptions, HostOutputWindowEx.WriteLaunchErrorAsync, settings.RedirectOutputOption);
                }

                if (debuggerMode.HasFlag(DebuggerMode.AttachProcess))
                {
                    if (settings.UseDotnetCoreDebugger)
                    {
                        var myresult = await monoRemoteSshDebugTask;
                        _monoExtension.AttachDotnetDebuggerToRunningProcess(debugOptions);                        
                    }
                    else
                    {
                        _monoExtension.AttachMonoDebuggerToRunningProcess(debugOptions);
                    }
                }

                if (monoRemoteSshDebugTask != null)
                {
                    var myresult = await monoRemoteSshDebugTask;
                }

                return true;
            }
            catch (Exception ex)
            {
                await HostOutputWindowEx.WriteLineLaunchErrorAsync(ex.Message);
                Logger.Error(ex);
                MessageBox.Show(ex.Message, "VSMonoDebugger", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return false;
        }

        private void CreateDebugOptions(out UserSettings settings, out DebugOptions debugOptions, out SshDeltaCopy.Options options)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var allDeviceSettings = UserSettingsManager.Instance.Load();
            settings = allDeviceSettings.CurrentUserSettings;

            if (settings.UseDeployPathFromProjectFileIfExists)
            {
                try
                {
                    var localProjectConfig = _monoExtension.GetProjectSettingsFromStartupProject();
                    if (localProjectConfig.HasValue)
                    {
                        if (!string.IsNullOrWhiteSpace(localProjectConfig.Value.SSHDeployPath))
                        {
                            Logger.Info($"SSHDeployPath = {settings.SSHDeployPath} was overwritten with local *.VSMonoDebugger.config: {localProjectConfig.Value.SSHDeployPath}");
                            settings.SSHDeployPath = localProjectConfig.Value.SSHDeployPath;
                        }
                        if (!string.IsNullOrWhiteSpace(localProjectConfig.Value.WindowsDeployPath))
                        {
                            Logger.Info($"WindowsDeployPath = {settings.WindowsDeployPath} was overwritten with local *.VSMonoDebugger.config: {localProjectConfig.Value.WindowsDeployPath}");
                            settings.WindowsDeployPath = localProjectConfig.Value.WindowsDeployPath;
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.Error(ex);
                }               
            }            

            debugOptions = _monoExtension.CreateDebugOptions(settings);
            options = new SshDeltaCopy.Options()
            {
                Host = settings.SSHHostIP,
                Port = settings.SSHPort,
                Username = settings.SSHUsername,
                Password = settings.SSHPassword,
                PrivateKeyFile = settings.SSHPrivateKeyFile,
                SourceDirectory = debugOptions.OutputDirectory,
                DestinationDirectory = settings.SSHDeployPath,
                RemoveOldFiles = true,
                PrintTimings = true,
                RemoveTempDeleteListFile = true,
            };
        }

        private async Task<bool> BuildProjectWithMDBFilesAsync()
        {
            try
            {
                Logger.Info($"===== {nameof(BuildProjectWithMDBFilesAsync)} =====");

                if (!_monoExtension.IsStartupProjectAvailable())
                {
                    Logger.Info($"No startup project/solution loaded yet.");
                    return false;
                }

                CreateDebugOptions(out UserSettings settings, out DebugOptions debugOptions, out SshDeltaCopy.Options options);

                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                await _monoExtension.BuildStartupProjectAsync();
                await _monoExtension.CreateMdbForAllDependantProjectsAsync(HostOutputWindowEx.WriteLineLaunchError);

                return true;
            }
            catch (Exception ex)
            {
                await HostOutputWindowEx.WriteLineLaunchErrorAsync(ex.Message);
                Logger.Error(ex);
                MessageBox.Show(ex.Message, "VSMonoDebugger", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return false;
        }
    }
}