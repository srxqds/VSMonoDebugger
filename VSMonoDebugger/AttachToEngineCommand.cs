using System;
using System.ComponentModel.Design;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using static Microsoft.VisualStudio.VSConstants;
using Task = System.Threading.Tasks.Task;

namespace VSMonoDebugger
{
    /// <summary>
    /// Command handler
    /// </summary>
    internal sealed class AttachToEngineCommand
    {

        public const string guidDynamicMenuPackageCmdSet = "07ff0b92-681c-4667-8172-40282cf03bdd";  // get the GUID from the .vsct file
        public const uint cmdidMyCommand = 0x104;
        public const uint cmdidMyAnchorCommand = 0x0103;
        private DTE2 dte2;
        private int rootItemId = 0;

        private void OnInvokedDynamicItem(object sender, EventArgs args)
        {
            AttachSubmenuCommand invokedCommand = (AttachSubmenuCommand)sender;
            // If the command is already checked, we don't need to do anything
            if (invokedCommand.Checked)
                return;

            // Find the project that corresponds to the command text and set it as the startup project
            var projects = dte2.Solution.Projects;
            foreach (Project proj in projects)
            {
                if (invokedCommand.Text.Equals(proj.Name))
                {
                    dte2.Solution.SolutionBuild.StartupProjects = proj.FullName;
                    return;
                }
            }
        }

        private void OnBeforeQueryStatusDynamicItem(object sender, EventArgs args)
        {
            AttachSubmenuCommand matchedCommand = (AttachSubmenuCommand)sender;
            matchedCommand.Enabled = true;
            matchedCommand.Visible = true;

            // Find out whether the command ID is 0, which is the ID of the root item.
            // If it is the root item, it matches the constructed DynamicItemMenuCommand,
            // and IsValidDynamicItem won't be called.
            bool isRootItem = (matchedCommand.MatchedCommandId == 0);

            // The index is set to 1 rather than 0 because the Solution.Projects collection is 1-based.
            int indexForDisplay = (isRootItem ? 1 : (matchedCommand.MatchedCommandId - (int)cmdidMyCommand) + 1);

            matchedCommand.Text = dte2.Solution.Projects.Item(indexForDisplay).Name;

            Array startupProjects = (Array)dte2.Solution.SolutionBuild.StartupProjects;
            string startupProject = System.IO.Path.GetFileNameWithoutExtension((string)startupProjects.GetValue(0));

            // Check the command if it isn't checked already selected
            matchedCommand.Checked = (matchedCommand.Text == startupProject);

            // Clear the ID because we are done with this item.
            matchedCommand.MatchedCommandId = 0;
        }

        private bool IsValidDynamicItem(int commandId)
        {
            // The match is valid if the command ID is >= the id of our root dynamic start item
            // and the command ID minus the ID of our root dynamic start item
            // is less than or equal to the number of projects in the solution.
            return (commandId >= (int)cmdidMyCommand) && ((commandId - (int)cmdidMyCommand) < dte2.Solution.Projects.Count);
        }



        /// <summary>
        /// Command ID.
        /// </summary>
        public const int CommandId = 256;

        /// <summary>
        /// Command menu group (command set GUID).
        /// </summary>
        public static readonly Guid CommandSet = new Guid("07ff0b92-681c-4667-8172-40282cf03bdd");

        /// <summary>
        /// VS Package that provides this command, not null.
        /// </summary>
        private readonly AsyncPackage package;

        /// <summary>
        /// Initializes a new instance of the <see cref="AttachToEngineCommand"/> class.
        /// Adds our command handlers for menu (commands must exist in the command table file)
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        /// <param name="commandService">Command service to add command to, not null.</param>
        private AttachToEngineCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            if (package == null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            this.package = package;

            dte2 = (DTE2)(package as System.IServiceProvider).GetService(typeof(DTE));
            if (commandService != null)
            {
                // Add the DynamicItemMenuCommand for the expansion of the root item into N items at run time.
                CommandID dynamicItemRootId = new CommandID(new Guid(guidDynamicMenuPackageCmdSet), (int)cmdidMyCommand);
                AttachSubmenuCommand dynamicMenuCommand = new AttachSubmenuCommand(dynamicItemRootId,
                    IsValidDynamicItem,
                    OnInvokedDynamicItem,
                    OnBeforeQueryStatusDynamicItem);
                commandService.AddCommand(dynamicMenuCommand);
                AttachCommand = dynamicMenuCommand;
                // add，find不到
                CommandID defaultCommandId = new CommandID(new Guid(guidDynamicMenuPackageCmdSet), (int)cmdidMyAnchorCommand);
                MenuCommand defaultMenuCommand = new MenuCommand(this.OnDefaultCommandExecute, defaultCommandId);
                commandService.AddCommand(defaultMenuCommand);
                //CommandID startCommand = new CommandID(new Guid("{5EFC7975-14BC-11CF-9B2B-00AA00573819}"), (int)VSStd97CmdID.Start);
                //foreach(Command command in dte2.Commands)
                //{
                //    if(command.ID == (int)VSStd97CmdID.Start)
                //    {
                //        (command as MenuCommand)
                //        int a = 1;
                //    }
                //}
                //MenuCommand startMenu = commandService.FindCommand(startCommand);
                
                //if(startMenu != null)
                //{
                //    startMenu.Visible = false;
                //}
            }

            //var menuCommandID = new CommandID(CommandSet, CommandId);
            //var menuItem = new MenuCommand(this.Execute, menuCommandID);
            //commandService.AddCommand(menuItem);
        }

        /// <summary>
        /// Gets the instance of the command.
        /// </summary>
        public static AttachToEngineCommand Instance
        {
            get;
            private set;
        }

        public MonoVisualStudioExtension MonoExtension
        {
            private set;
            get;
        }

        public MenuCommand AttachCommand
        {
            private set;
            get;
        }
        /// <summary>
        /// Gets the service provider from the owner package.
        /// </summary>
        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get
            {
                return this.package;
            }
        }

        /// <summary>
        /// Initializes the singleton instance of the command.
        /// </summary>
        /// <param name="package">Owner package, not null.</param>
        public static async Task InitializeAsync(AsyncPackage package, IMenuCommandService inCommandService, MonoVisualStudioExtension monoVisualStudioExtension)
        {
            // Switch to the main thread - the call to AddCommand in AttachToEngineCommand's constructor requires
            // the UI thread.
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            OleMenuCommandService commandService = inCommandService as OleMenuCommandService;
            Project monoProject = monoVisualStudioExtension.GetStartupProject();
            CommandID dynamicItemRootId = new CommandID(new Guid(guidDynamicMenuPackageCmdSet), (int)0x1030);
            if (monoProject == null || !monoProject.Name.Contains("UnrealEngine"))
            {
               
                return;
            }
            Instance = new AttachToEngineCommand(package, commandService);
            Instance.MonoExtension = monoVisualStudioExtension;
        }

        public void OnAttach()
        {
            Instance.AttachCommand.Enabled = false;
            AttachCommandNotify.Instance.NotifyToEngineAttach(true);
        }

        public void OnDetach()
        {
            Instance.AttachCommand.Enabled = true;
            AttachCommandNotify.Instance.NotifyToEngineDetach(true);
        }

        private async void OnDefaultCommandExecute(object sender, EventArgs e)
        {
            try
            {
                await MonoExtension.BuildStartupProjectAsync();
                await VSMonoDebuggerCommands.Instance.DeployAndRunCommandOverSSHAsync(VSMonoDebuggerCommands.DebuggerMode.AttachProcess);
            }
            catch(Exception ex)
            {
                throw ex;
            }
        }
    }
}
