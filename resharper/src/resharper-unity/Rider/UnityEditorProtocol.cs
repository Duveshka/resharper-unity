﻿using System;
using System.Diagnostics;
using System.IO;
using JetBrains.Annotations;
using JetBrains.Application.Threading;
using JetBrains.DataFlow;
using JetBrains.DataFlow.StandardPreconditions;
using JetBrains.DocumentModel;
using JetBrains.IDE;
using JetBrains.Platform.RdFramework;
using JetBrains.Platform.RdFramework.Base;
using JetBrains.Platform.RdFramework.Impl;
using JetBrains.Platform.RdFramework.Util;
using JetBrains.Platform.Unity.EditorPluginModel;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Host.Features;
using JetBrains.ReSharper.Resources.Shell;
using JetBrains.Rider.Model;
using JetBrains.TextControl;
using JetBrains.Util;
using JetBrains.Util.dataStructures.TypedIntrinsics;
using Newtonsoft.Json;

namespace JetBrains.ReSharper.Plugins.Unity.Rider
{
    [SolutionComponent]
    public class UnityEditorProtocol
    {
        private readonly Lifetime myComponentLifetime;
        private readonly SequentialLifetimes mySessionLifetimes;
        private readonly ILogger myLogger;
        private readonly IScheduler myDispatcher;
        private readonly IShellLocks myLocks;
        private readonly ISolution mySolution;
        private readonly PluginPathsProvider myPluginPathsProvider;
        private readonly UnityHost myHost;

        private readonly ReadonlyToken myReadonlyToken = new ReadonlyToken("unityModelReadonlyToken");
        public readonly Platform.RdFramework.Util.Signal<bool> Refresh = new Platform.RdFramework.Util.Signal<bool>();
        
        private readonly IProperty<EditorPluginModel> myUnityModel;

        [NotNull]
        public IProperty<EditorPluginModel> UnityModel => myUnityModel;

        public UnityEditorProtocol(Lifetime lifetime, ILogger logger, UnityHost host,
            IScheduler dispatcher, IShellLocks locks, ISolution solution, PluginPathsProvider pluginPathsProvider)
        {
            myComponentLifetime = lifetime;
            myLogger = logger;
            myDispatcher = dispatcher;
            myLocks = locks;
            mySolution = solution;
            myPluginPathsProvider = pluginPathsProvider;
            myHost = host;
            mySessionLifetimes = new SequentialLifetimes(lifetime);
            myUnityModel = new Property<EditorPluginModel>(lifetime, "unityModelProperty", null).EnsureReadonly(myReadonlyToken).EnsureThisThread();
            
            if (!ProjectExtensions.IsSolutionGeneratedByUnity(solution.SolutionFilePath.Directory))
                return;

            if (solution.GetData(ProjectModelExtensions.ProtocolSolutionKey) == null)
                return;

            var solFolder = mySolution.SolutionFilePath.Directory;
            AdviseModelData(lifetime, mySolution.GetProtocolSolution());

            // todo: consider non-Unity Solution with Unity-generated projects
            var protocolInstancePath = solFolder.Combine("Library/ProtocolInstance.json");

            protocolInstancePath.Directory.CreateDirectory();
            
            var watcher = new FileSystemWatcher();
            watcher.Path = protocolInstancePath.Directory.FullPath;
            watcher.NotifyFilter =
                NotifyFilters.LastAccess |
                NotifyFilters.LastWrite; //Watch for changes in LastAccess and LastWrite times
            watcher.Filter = protocolInstancePath.Name;

            // Add event handlers.
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;

            watcher.EnableRaisingEvents = true; // Begin watching.

            // connect on start of Rider
            CreateProtocol(protocolInstancePath);
        }

        private void OnChanged(object sender, FileSystemEventArgs e)
        {
            var protocolInstancePath = FileSystemPath.Parse(e.FullPath);
            // connect on reload of server
            if (!myComponentLifetime.IsTerminated)
              myLocks.ExecuteOrQueue(myComponentLifetime, "CreateProtocol",
                () => CreateProtocol(protocolInstancePath));
        }

        private void AdviseModelData(Lifetime lifetime, Solution solution)
        {
            myHost.Model.Data.Advise(lifetime, e =>
            {
                var model = UnityModel.Value;
                if (e.NewValue == e.OldValue)
                    return;
                if (e.NewValue == null)
                    return;
                if (model==null)
                    return;
                
                switch (e.Key)
                {
                    case "UNITY_Refresh":
                        myLogger.Info($"{e.Key} = {e.NewValue} came from frontend.");
                        var force = Convert.ToBoolean(e.NewValue);
                        Refresh.Fire(force);
                        solution.CustomData.Data.Remove("UNITY_Refresh");
                        break;
                    
                    case "UNITY_Step":
                        myLogger.Info($"{e.Key} = {e.NewValue} came from frontend.");
                        model.Step.Start(RdVoid.Instance);
                        solution.CustomData.Data.Remove("UNITY_Step");
                        break;
                    
                    case "UNITY_Play":
                        myLogger.Info($"{e.Key} = {e.NewValue} came from frontend.");
                        model.Play.SetValue(Convert.ToBoolean(e.NewValue));
                        break;

                    case "UNITY_Pause":
                        myLogger.Info($"{e.Key} = {e.NewValue} came from frontend.");
                        model.Pause.SetValue(Convert.ToBoolean(e.NewValue));
                        break;
                }
            });
        }

        private void CreateProtocol(FileSystemPath protocolInstancePath)
        {
            int port;
            try
            {
                var protocolInstance = JsonConvert.DeserializeObject<ProtocolInstance>(protocolInstancePath.ReadAllText2().Text);
                port = protocolInstance.port_id;
            }
            catch (Exception e)
            {
                myLogger.Warn($"Unable to parse {protocolInstancePath}" + Environment.NewLine + e);
                return;
            }

            myLogger.Info($"UNITY_Port {port}.");

            try
            {
                var lifetime = mySessionLifetimes.Next();
                myLogger.Info("Create protocol...");

                myLogger.Info("Creating SocketWire with port = {0}", port);
                var wire = new SocketWire.Client(lifetime, myDispatcher, port, "UnityClient");
                wire.Connected.WhenTrue(lifetime, lf =>
                {
                    myLogger.Info("WireConnected.");
                    myHost.SetModelData("UNITY_Play", "undef");
                
                    var protocol = new Protocol("UnityEditorPlugin", new Serializers(), new Identities(IdKind.Client), myDispatcher, wire);
                    var model = new EditorPluginModel(lf, protocol);
                    model.IsBackendConnected.Set(rdVoid => true);
                    model.RiderProcessId.SetValue(Process.GetCurrentProcess().Id);
                    myHost.SetModelData("UNITY_SessionInitialized", "true");

                    SubscribeToLogs(lf, model);
                    SubscribeToOpenFile(model);
                    model.Play.AdviseNotNull(lf, b => myHost.SetModelData("UNITY_Play", b.ToString().ToLower()));
                    model.Pause.AdviseNotNull(lf, b => myHost.SetModelData("UNITY_Pause", b.ToString().ToLower()));
                    
                    model.FullPluginPath.SetValue(myPluginPathsProvider.GetEditorPluginPathDir().FullPath);

                    if (!myComponentLifetime.IsTerminated)
                        myLocks.ExecuteOrQueueEx(myComponentLifetime, "setModel", () => { myUnityModel.SetValue(model, myReadonlyToken); });
                    
                    lf.AddAction(() =>
                    {
                        if (!myComponentLifetime.IsTerminated)
                            myLocks.ExecuteOrQueueEx(myComponentLifetime, "clearModel", () =>
                            {
                                myLogger.Info("Wire disconnected.");
                                myHost.SetModelData("UNITY_SessionInitialized", "false");
                                myUnityModel.SetValue(null, myReadonlyToken);                                
                            });
                    });
                });
            }
            catch (Exception ex)
            {
                myLogger.Error(ex);
            }
        }

        private void SubscribeToOpenFile([NotNull] EditorPluginModel model)
        {
            model.OpenFileLineCol.Set(args =>
            {
                using (ReadLockCookie.Create())
                {
                    var textControl = mySolution.GetComponent<IEditorManager>()
                        .OpenFile(FileSystemPath.Parse(args.Path), OpenFileOptions.DefaultActivate);
                    if (textControl == null)
                        return false;
                    if (args.Line > 0 || args.Col > 0)
                    {
                        textControl.Caret.MoveTo((Int32<DocLine>) (args.Line - 1), (Int32<DocColumn>) args.Col,
                            CaretVisualPlacement.Generic);
                    }
                }

                myHost.SetModelData("UNITY_ActivateRider", "true");
                return true;
            });
        }

        private void SubscribeToLogs(Lifetime lifetime, EditorPluginModel model)
        {

            model.Log.Advise(lifetime, entry =>
            {
                myLogger.Verbose(entry.Mode + " " + entry.Type + " " + entry.Message + " " + Environment.NewLine + " " + entry.StackTrace);
                myHost.SetModelData("UNITY_LogEntry", JsonConvert.SerializeObject(entry));
            });
        }
    }

    // ReSharper disable once ClassNeverInstantiated.Global
    class ProtocolInstance
    {
        // ReSharper disable once InconsistentNaming
        // ReSharper disable once UnusedAutoPropertyAccessor.Global
        public int port_id { get; set; }
    }
}