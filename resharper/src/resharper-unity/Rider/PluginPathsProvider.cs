﻿using System.Reflection;
using JetBrains.Application.Environment;
using JetBrains.ProjectModel;
using JetBrains.Util;

namespace JetBrains.ReSharper.Plugins.Unity.Rider
{
    [SolutionComponent]
    public class PluginPathsProvider
    {
        private readonly ApplicationPackages myApplicationPackages;
        private readonly IDeployedPackagesExpandLocationResolver myResolver;

        public PluginPathsProvider(ApplicationPackages applicationPackages, IDeployedPackagesExpandLocationResolver resolver)
        {
            myApplicationPackages = applicationPackages;
            myResolver = resolver;
        }
        
        public FileSystemPath GetEditorPluginPathDir()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var package = myApplicationPackages.FindPackageWithAssembly(assembly, OnError.LogException);
            var installDirectory = myResolver.GetDeployedPackageDirectory(package);
            var editorPluginPathDir = installDirectory.Parent.Combine(@"EditorPlugin");
            return editorPluginPathDir;
        }
    }
}