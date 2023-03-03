using System;
using JetBrains.Collections.Viewable;
using JetBrains.Core;
using JetBrains.DataFlow;
using JetBrains.Lifetimes;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Features.Inspections.Bookmarks.NumberedBookmarks;
using JetBrains.ReSharper.Plugins.Unity.AsmDef.Psi.Caches;
using JetBrains.ReSharper.Plugins.Unity.Core.ProjectModel;
using JetBrains.ReSharper.Plugins.Unity.Rider.Integration.Protocol;
using JetBrains.ReSharper.Plugins.Unity.Rider.Resources;
using JetBrains.ReSharper.Resources.Shell;
using JetBrains.Rider.Backend.Features.Notifications;
using JetBrains.Rider.Backend.Features.ProjectModel;
using JetBrains.Rider.Model.Unity.BackendUnity;
using JetBrains.TextControl.TextControlsManagement;
using JetBrains.Util;
using JetBrains.Util.Extension;

#nullable enable

namespace JetBrains.ReSharper.Plugins.Unity.Rider.Integration.AsmDef.Feature.Notifications
{
    [SolutionComponent]
    public class GeneratedFileNotification
    {
        public GeneratedFileNotification(Lifetime lifetime,
                                         FrontendBackendHost frontendBackendHost,
                                         BackendUnityHost backendUnityHost,
                                         UnitySolutionTracker solutionTracker,
                                         ISolution solution,
                                         AsmDefCache asmDefCache,
                                         TextControlManager? textControlHost = null,
                                         SolutionLifecycleHost? solutionLifecycleHost = null,
                                         NotificationPanelHost? notificationPanelHost = null)
        {
            // TODO: Why are these [CanBeNull]?
            if (solutionLifecycleHost == null || textControlHost == null || notificationPanelHost == null)
                return;

            if (!solutionTracker.IsUnityGeneratedProject.Value)
                return;
            
            solutionLifecycleHost.BeforeFullStartupFinished.AdviseOnce(lifetime, _ =>
            {
                textControlHost.TextControls.ForEachItem(lifetime, (lt, host) =>
                {
                    IProjectFile projectFile;
                    using (ReadLockCookie.Create())
                    { 
                        projectFile = host.ToProjectFile(solution);
                    }
                    
                    if (projectFile == null)
                        return;

                    if (!projectFile.Location.ExtensionNoDot.Equals("csproj", StringComparison.OrdinalIgnoreCase))
                        return;

                    // TODO: ReactiveEx.ViewNotNull isn't NRT ready
                    backendUnityHost.BackendUnityModel!.ViewNotNull<BackendUnityModel>(lt, (modelLifetime, backendUnityModel) =>
                    {
                        var name = projectFile.Location.NameWithoutExtension;

                        IPath? path;
                        using (ReadLockCookie.Create())
                        {
                            var location = asmDefCache.GetAsmDefLocationByAssemblyName(name);
                            path = location.IsEmpty ? null : location.TryMakeRelativeTo(solution.SolutionFilePath);
                        }

                        var links = new LocalList<INotificationPanelHyperlink>();
                        if (path != null)
                        {
                            var strPath = path.Components.Join("/").RemoveStart("../");
                            links.Add(new NotificationPanelCallbackHyperlink(modelLifetime,
                                Strings.GeneratedFileNotification_GeneratedFileNotification_Edit_corresponding__asmdef_in_Unity, false,
                                () =>
                                {
                                    frontendBackendHost.Do(t =>
                                    {
                                        t.AllowSetForegroundWindow.Start(modelLifetime, Unit.Instance)
                                            .Result.AdviseOnce(modelLifetime, _ =>
                                            {
                                                backendUnityModel.ShowFileInUnity.Fire(strPath);
                                            });
                                    });
                                }));
                        }

                        notificationPanelHost.AddNotificationPanel(modelLifetime, host,
                            new NotificationPanel(Strings.GeneratedFileNotification_GeneratedFileNotification_This_file_is_generated_by_Unity__Any_changes_made_will_be_lost_,
                                "UnityGeneratedFile", links.ToArray()));
                    });
                });
            });
        }
    }
}