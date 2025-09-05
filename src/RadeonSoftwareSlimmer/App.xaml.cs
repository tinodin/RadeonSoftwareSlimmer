using RadeonSoftwareSlimmer.Models.PreInstall;
using RadeonSoftwareSlimmer.Services;
using RadeonSoftwareSlimmer.ViewModels;
using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace RadeonSoftwareSlimmer
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            if (e.Args.Length == 0)
            {
                var mainWindow = new MainWindow();
                mainWindow.Show();

                SetupExceptionHandling();

                ThemeService.SetThemeToUserSettings(new WindowsRegistry());
            }
            else
            {
                SetupExceptionHandling();

                string installerDir = null;
                string configFile = null;

                for (int i = 0; i < e.Args.Length; i++)
                {
                    if (e.Args[i].Equals("--extracted-installer", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
                        installerDir = e.Args[i + 1];
                    if (e.Args[i].Equals("--config", StringComparison.OrdinalIgnoreCase) && i + 1 < e.Args.Length)
                        configFile = e.Args[i + 1];
                }

                if (installerDir == null || configFile == null) 
                { 
                    Shutdown();
                    return; 
                }

                var vm = new PreInstallViewModel(new FileSystem());
                vm.InstallerFiles.ExtractedInstallerDirectory = installerDir;
                vm.ReadFromExtractedInstaller();

                var fs = new FileSystem();
                var lines = fs.File.ReadAllLines(configFile);

                var iniData = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
                {
                    { "Packages", new List<string>() },
                    { "ScheduledTasks", new List<string>() },
                    { "DisplayComponents", new List<string>() }
                };

                string currentSection = null;

                foreach (var rawLine in lines)
                {
                    string line = rawLine.Trim();

                    if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                        continue;

                    if (line[0] == '[' && line[line.Length - 1] == ']')
                    {
                        string sectionName = line.Substring(1, line.Length - 2).Trim();
                        currentSection = iniData.ContainsKey(sectionName) ? sectionName : null;
                        continue;
                    }

                    if (currentSection != null)
                        iniData[currentSection].Add(line);
                }
                
                foreach (PackageModel package in vm.PackageList.InstallerPackages)
                    package.Keep = iniData["Packages"].Any(r => string.Equals(package.ProductName, r, StringComparison.OrdinalIgnoreCase));

                foreach (ScheduledTaskXmlModel scheduledTask in vm.ScheduledTaskList.ScheduledTasks)
                    scheduledTask.Enabled = iniData["ScheduledTasks"].Any(r => string.Equals(scheduledTask.Description, r, StringComparison.OrdinalIgnoreCase));

                foreach (DisplayComponentModel displayComponent in vm.DisplayComponentList.DisplayDriverComponents)
                    displayComponent.Keep = iniData["DisplayComponents"].Any(r => string.Equals(displayComponent.Description, r, StringComparison.OrdinalIgnoreCase));

                vm.ModifyInstaller();

                Shutdown();
            }
        }

        private void SetupExceptionHandling()
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, ex) => LogUnhandledException((Exception)ex.ExceptionObject);

            DispatcherUnhandledException += (sender, ex) =>
            {
                LogUnhandledException(ex.Exception);
                ex.Handled = true;
            };

            TaskScheduler.UnobservedTaskException += (sender, ex) =>
            {
                LogUnhandledException(ex.Exception);
                ex.SetObserved();
            };
        }

        private static void LogUnhandledException(Exception exception)
        {
            StaticViewModel.AddLogMessage(exception);
            StaticViewModel.IsLoading = false;
        }
    }
}