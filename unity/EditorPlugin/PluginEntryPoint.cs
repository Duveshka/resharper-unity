using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using JetBrains.Annotations;
using JetBrains.Collections.Viewable;
using JetBrains.Core;
using JetBrains.Rider.Model.Unity.BackendUnity;
using JetBrains.Diagnostics;
using JetBrains.Lifetimes;
using JetBrains.Rd;
using JetBrains.Rd.Base;
using JetBrains.Rd.Impl;
using JetBrains.Rd.Tasks;
using JetBrains.Rider.Model.Unity;
using JetBrains.Rider.Unity.Editor.Logger;
using JetBrains.Rider.Unity.Editor.NonUnity;
using JetBrains.Rider.Unity.Editor.Utils;
using UnityEditor;
using UnityEditor.Callbacks;
using Application = UnityEngine.Application;
using Debug = UnityEngine.Debug;

namespace JetBrains.Rider.Unity.Editor
{
  // DO NOT RENAME!
  // Used by package via reflection
  [InitializeOnLoad, PublicAPI]
  public static class PluginEntryPoint
  {
    private static readonly ILog ourLogger = Log.GetLog("RiderPlugin");
    private static readonly IPluginSettings ourPluginSettings;
    private static readonly RiderPathProvider ourRiderPathProvider;
    private static readonly long ourInitTime = DateTime.UtcNow.Ticks;

    internal static string SlnFile;

    public static readonly List<ModelWithLifetime> UnityModels = new List<ModelWithLifetime>();

    // This an entry point
    static PluginEntryPoint()
    {
      if (UnityUtils.IsInBatchModeAndNotInRiderTests)
        return;

      var lifetimeDefinition = Lifetime.Define(Lifetime.Eternal);
      AppDomain.CurrentDomain.DomainUnload += (_, __) =>
      {
        ourLogger.Verbose("LifetimeDefinition.Terminate");
        lifetimeDefinition.Terminate();
      };

      // Init log before doing any logging (the log above is in a lambda)
      LogInitializer.InitLog(lifetimeDefinition.Lifetime, PluginSettings.SelectedLoggingLevel);

      // Start collecting Unity messages ASAP
      UnityEventLogSender.Start(lifetimeDefinition.Lifetime);

      ourPluginSettings = new PluginSettings();
      ourRiderPathProvider = new RiderPathProvider(ourPluginSettings);

      if (IsLoadedFromAssets()) // old mechanism, when EditorPlugin was copied to Assets folder
      {
        var riderPath = ourRiderPathProvider.GetActualRider(EditorPrefsWrapper.ExternalScriptEditor,
        RiderPathLocator.GetAllFoundPaths(ourPluginSettings.OperatingSystemFamilyRider));
        if (!string.IsNullOrEmpty(riderPath))
        {
          AddRiderToRecentlyUsedScriptApp(riderPath);
          if (IsRiderDefaultEditor() && PluginSettings.UseLatestRiderFromToolbox)
          {
            EditorPrefsWrapper.ExternalScriptEditor = riderPath;
          }
        }

        if (!PluginSettings.RiderInitializedOnce)
        {
          EditorPrefsWrapper.ExternalScriptEditor = riderPath;
          PluginSettings.RiderInitializedOnce = true;
        }

        InitForPluginLoadedFromAssets(lifetimeDefinition.Lifetime);
      }

      Init(lifetimeDefinition.Lifetime);
    }

    public delegate void OnModelInitializationHandler(UnityModelAndLifetime e);
    public static event OnModelInitializationHandler OnModelInitialization = delegate {};

    internal static bool CheckConnectedToBackendSync(BackendUnityModel model)
    {
      if (model == null)
        return false;
      var connected = false;
      try
      {
        // HostConnected also means that in Rider and in Unity the same solution is opened
        connected = model.IsBackendConnected.Sync(Unit.Instance,
          new RpcTimeouts(TimeSpan.FromMilliseconds(200), TimeSpan.FromMilliseconds(200)));
      }
      catch (Exception)
      {
        ourLogger.Verbose("Rider Protocol not connected.");
      }

      return connected;
    }

    public static bool CallRider(string args)
    {
      return OpenAssetHandler.CallRider(args);
    }

    public static bool IsRiderDefaultEditor()
    {
        if (UnityUtils.UseRiderTestPath)
            return true;

        // Regular check
        var defaultApp = EditorPrefsWrapper.ExternalScriptEditor;
        bool isEnabled = !string.IsNullOrEmpty(defaultApp) &&
                         Path.GetFileName(defaultApp).ToLower().Contains("rider") &&
                         !UnityUtils.IsInBatchModeAndNotInRiderTests;
        return isEnabled;
    }

    private static void Init(Lifetime lifetime)
    {
      var projectDirectory = Directory.GetParent(Application.dataPath).NotNull().FullName;
      var projectName = Path.GetFileName(projectDirectory);
      SlnFile = Path.GetFullPath($"{projectName}.sln");

      InitializeEditorInstanceJson(lifetime);

#if UNITY_2017_3_OR_NEWER
      EditorApplication.playModeStateChanged += state =>
      {
        if (state == PlayModeStateChange.EnteredPlayMode)
        {
          var time = DateTime.UtcNow.Ticks.ToString();
          SessionState.SetString("Rider_EnterPlayMode_DateTime", time);
        }
      };
#endif

      InitializeProtocol(lifetime);

      OpenAssetHandler = new OnOpenAssetHandler(ourRiderPathProvider, ourPluginSettings, SlnFile);

      PlayModeSavedState = GetPlayModeState();

      // Done
      if (PluginSettings.SelectedLoggingLevel >= LoggingLevel.VERBOSE)
      {
        var executingAssembly = Assembly.GetExecutingAssembly();
        var location = executingAssembly.Location;
        Debug.Log(
          $"Rider plugin \"{executingAssembly.GetName().Name}\" initialized{(string.IsNullOrEmpty(location) ? "" : " from: " + location)}. " +
          $"LoggingLevel: {PluginSettings.SelectedLoggingLevel}. Change it in Unity Preferences -> Rider. Logs path: {LogInitializer.LogPath}.");
      }
    }

    private static void InitializeProtocol(Lifetime lifetime)
    {
      var currentDirectory = new DirectoryInfo(Directory.GetCurrentDirectory());
      var solutionNames = new List<string> { currentDirectory.Name };

      ourLogger.Verbose("Initialising protocol. Looking for solution files");

      var solutionFiles = currentDirectory.GetFiles("*.sln", SearchOption.TopDirectoryOnly);
      foreach (var solutionFile in solutionFiles)
      {
        var solutionName = Path.GetFileNameWithoutExtension(solutionFile.FullName);
        if (!solutionName.Equals(currentDirectory.Name))
        {
          solutionNames.Add(solutionName);
        }
      }

      var protocols = new List<ProtocolInstance>();

      // If any protocol connection is lost, we will drop all connections and recreate them
      var allProtocolsLifetimeDefinition = lifetime.CreateNested();
      foreach (var solutionName in solutionNames)
      {
        var port = CreateProtocolForSolution(allProtocolsLifetimeDefinition.Lifetime, solutionName,
          () => allProtocolsLifetimeDefinition.Terminate());

        if (port == -1)
          continue;

        protocols.Add(new ProtocolInstance(solutionName, port));
      }

      allProtocolsLifetimeDefinition.Lifetime.OnTermination(() =>
      {
        if (lifetime.IsAlive)
        {
          ourLogger.Verbose("Recreating protocol, project lifetime is alive");
          InitializeProtocol(lifetime);
        }
        else
        {
          ourLogger.Verbose("Protocol will be recreated on next domain load, plugin lifetime is not alive");
        }
      });

      ourLogger.Verbose("Writing Library/ProtocolInstance.json");
      var protocolInstancePath = Path.GetFullPath("Library/ProtocolInstance.json");
      var result = ProtocolInstance.ToJson(protocols);
      File.WriteAllText(protocolInstancePath, result);
      lifetime.OnTermination(() =>
      {
        ourLogger.Verbose("Deleting Library/ProtocolInstance.json");
        File.Delete(protocolInstancePath);
      });
    }

    private static void InitForPluginLoadedFromAssets(Lifetime lifetime)
    {
      ResetDefaultFileExtensions();

      // process csproj files once per Unity process
      if (!RiderScriptableSingleton.Instance.CsprojProcessedOnce)
      {
        // Perform on next editor frame update, so we avoid this exception:
        // "Must set an output directory through SetCompileScriptsOutputDirectory before compiling"
        EditorApplication.update += SyncSolutionOnceCallBack;
      }

      SetupAssemblyReloadEvents(lifetime);
    }

    private static void SyncSolutionOnceCallBack()
    {
      ourLogger.Verbose("Call SyncSolution once per Unity process.");
      UnityUtils.SyncSolution();
      RiderScriptableSingleton.Instance.CsprojProcessedOnce = true;
      EditorApplication.update -= SyncSolutionOnceCallBack;
    }

    // Unity 2017.3 added "asmdef" to the default list of file extensions used to generate the C# projects, but only for
    // new projects. Existing projects have this value serialised, and Unity doesn't update or reset it. We need .asmdef
    // files in the project, so we'll add it if it's missing.
    // For the record, the default list of file extensions in Unity 2017.4.6f1 is: txt;xml;fnt;cd;asmdef;rsp
    private static void ResetDefaultFileExtensions()
    {
      // ReSharper disable once JoinDeclarationAndInitializer
      string[] currentValues;

      // EditorSettings.projectGenerationUserExtensions (and projectGenerationBuiltinExtensions) were added in 5.2
#if UNITY_5_6_OR_NEWER
      currentValues = EditorSettings.projectGenerationUserExtensions;
#else
      var propertyInfo = typeof(EditorSettings)
        .GetProperty("projectGenerationUserExtensions", BindingFlags.Public | BindingFlags.Static);
      currentValues = propertyInfo?.GetValue(null, null) as string[];
#endif

      if (currentValues != null && !currentValues.Contains("asmdef"))
      {
        var newValues = new string[currentValues.Length + 1];
        Array.Copy(currentValues, newValues, currentValues.Length);
        newValues[currentValues.Length] = "asmdef";

#if UNITY_5_6_OR_NEWER
        EditorSettings.projectGenerationUserExtensions = newValues;
#else
        propertyInfo.SetValue(null, newValues, null);
#endif
      }
    }

    public enum PlayModeState
    {
      Stopped,
      Playing,
      Paused
    }

    public static PlayModeState PlayModeSavedState = PlayModeState.Stopped;

    private static PlayModeState GetPlayModeState()
    {
      if (EditorApplication.isPaused)
        return PlayModeState.Paused;
      if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
        return PlayModeState.Playing;
      return PlayModeState.Stopped;
    }

    private static void SetupAssemblyReloadEvents(Lifetime lifetime)
    {
      // Unity supports recompile/reload settings natively for Unity 2018.2+
      if (UnityUtils.UnityVersion >= new Version(2018, 2))
        return;

      // playmodeStateChanged was marked obsolete in 2017.1. Still working in 2018.3
#pragma warning disable 618
      EditorApplication.playmodeStateChanged += () =>
#pragma warning restore 618
      {
        if (PluginSettings.AssemblyReloadSettings == ScriptCompilationDuringPlay.RecompileAfterFinishedPlaying)
        {
          MainThreadDispatcher.Instance.Queue(() =>
          {
            var newPlayModeState = GetPlayModeState();
            if (PlayModeSavedState != newPlayModeState)
            {
              if (newPlayModeState == PlayModeState.Playing)
              {
                ourLogger.Info("LockReloadAssemblies");
                EditorApplication.LockReloadAssemblies();
              }
              else if (newPlayModeState == PlayModeState.Stopped)
              {
                ourLogger.Info("UnlockReloadAssemblies");
                EditorApplication.UnlockReloadAssemblies();
              }

              PlayModeSavedState = newPlayModeState;
            }
          });
        }
      };

      lifetime.OnTermination(() =>
      {
        // Make sure the assemblies are unlocked during AppDomain unload
        if (PluginSettings.AssemblyReloadSettings == ScriptCompilationDuringPlay.StopPlayingAndRecompile &&
            EditorApplication.isPlaying)
        {
          EditorApplication.isPlaying = false;
        }
      });
    }

    private static int CreateProtocolForSolution(Lifetime lifetime, string solutionName, Action onDisconnected)
    {
      ourLogger.Verbose($"Initialising protocol for {solutionName}");
      try
      {
        var dispatcher = MainThreadDispatcher.Instance;
        var currentWireAndProtocolLifetimeDef = lifetime.CreateNested();
        var currentWireAndProtocolLifetime = currentWireAndProtocolLifetimeDef.Lifetime;

        var riderProtocolController = new RiderProtocolController(dispatcher, currentWireAndProtocolLifetime);

        var serializers = new Serializers();
        var identities = new Identities(IdKind.Server);

        MainThreadDispatcher.AssertThread();
        var protocol = new Protocol("UnityEditorPlugin" + solutionName, serializers, identities, MainThreadDispatcher.Instance, riderProtocolController.Wire, currentWireAndProtocolLifetime);
        riderProtocolController.Wire.Connected.WhenTrue(currentWireAndProtocolLifetime, connectionLifetime =>
        {
          ourLogger.Log(LoggingLevel.VERBOSE, "Create UnityModel and advise for new sessions...");
          var model = new BackendUnityModel(connectionLifetime, protocol);
          AdviseUnityActions(model, connectionLifetime);
          AdviseEditorState(model);
          OnModelInitialization(new UnityModelAndLifetime(model, connectionLifetime));
          AdviseRefresh(model);
          var paths = GetLogPaths();

          model.UnityApplicationData.SetValue(new UnityApplicationData(
              EditorApplication.applicationPath,
              EditorApplication.applicationContentsPath,
              UnityUtils.UnityApplicationVersion,
              paths[0], paths[1],
              Process.GetCurrentProcess().Id));

          model.UnityApplicationSettings.ScriptCompilationDuringPlay.Set(UnityUtils.SafeScriptCompilationDuringPlay);

          model.UnityProjectSettings.ScriptingRuntime.SetValue(UnityUtils.ScriptingRuntime);

          AdviseShowPreferences(model, connectionLifetime, ourLogger);
          AdviseGenerateUISchema(model);
          AdviseExitUnity(model);
          GetBuildLocation(model);
          AdviseRunMethod(model);
          AdviseStartProfiling(model);
          GetInitTime(model);

          ourLogger.Verbose("UnityModel initialized.");
          var pair = new ModelWithLifetime(model, connectionLifetime);
          connectionLifetime.OnTermination(() => { UnityModels.Remove(pair); });
          UnityModels.Add(pair);

          connectionLifetime.OnTermination(() =>
          {
              ourLogger.Verbose($"Connection lifetime is not alive for {solutionName}, destroying protocol");
              onDisconnected();
          });
        });

        return riderProtocolController.Wire.Port;
      }
      catch (Exception ex)
      {
        ourLogger.Error("Init Rider Plugin " + ex);
        return -1;
      }
    }

    private static void AdviseStartProfiling(BackendUnityModel model)
    {
        model.StartProfiling.Set((lifetime, data) =>
        {
            MainThreadDispatcher.Instance.Queue(() =>
            {
                try
                {
                    UnityProfilerApiInterop.StartProfiling(data.UnityProfilerApiPath, data.NeedRestartScripts);

                    var current = EditorApplication.isPlayingOrWillChangePlaymode && EditorApplication.isPlaying;
                    if (current != data.EnterPlayMode)
                    {
                        ourLogger.Verbose("StartProfiling. Request to change play mode from model: {0}", data.EnterPlayMode);
                        EditorApplication.isPlaying = data.EnterPlayMode;
                    }
                }
                catch (Exception e)
                {
                    if (PluginSettings.SelectedLoggingLevel >= LoggingLevel.VERBOSE)
                        Debug.LogError(e);
                    throw;
                }
            });

            return Unit.Instance;
        });

        model.StopProfiling.Set((_, data) =>
        {
            MainThreadDispatcher.Instance.Queue(() =>
            {
                try
                {
                    UnityProfilerApiInterop.StopProfiling(data.UnityProfilerApiPath);
                }
                catch (Exception e)
                {
                    if (PluginSettings.SelectedLoggingLevel >= LoggingLevel.VERBOSE)
                        Debug.LogError(e);
                    throw;
                }
            });

            return Unit.Instance;
        });
    }

    private static void GetInitTime(BackendUnityModel model)
    {
        model.ConsoleLogging.LastInitTime.SetValue(ourInitTime);

#if UNITY_2017_3_OR_NEWER
        var enterPlayTime = long.Parse(SessionState.GetString("Rider_EnterPlayMode_DateTime", "0"));
        model.ConsoleLogging.LastPlayTime.SetValue(enterPlayTime);
#endif
    }

    private static void AdviseRunMethod(BackendUnityModel model)
    {
        model.RunMethodInUnity.Set((lifetime, data) =>
        {
            var task = new RdTask<RunMethodResult>();
            MainThreadDispatcher.Instance.Queue(() =>
            {
                if (!lifetime.IsAlive)
                {
                    task.SetCancelled();
                    return;
                }

                try
                {
                    ourLogger.Verbose($"Attempt to execute {data.MethodName}");
                    var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                    var assembly = assemblies
                        .FirstOrDefault(a => a.GetName().Name.Equals(data.AssemblyName));
                    if (assembly == null)
                        throw new Exception($"Could not find {data.AssemblyName} assembly in current AppDomain");

                    var type = assembly.GetType(data.TypeName);
                    if (type == null)
                        throw new Exception($"Could not find {data.TypeName} in assembly {data.AssemblyName}.");

                    var method = type.GetMethod(data.MethodName,BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

                    if (method == null)
                        throw new Exception($"Could not find {data.MethodName} in type {data.TypeName}");

                    try
                    {
                        method.Invoke(null, null);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e);
                    }

                    task.Set(new RunMethodResult(true, string.Empty, string.Empty));
                }
                catch (Exception e)
                {
                    ourLogger.Log(LoggingLevel.WARN, $"Execute {data.MethodName} failed.", e);
                    task.Set(new RunMethodResult(false, e.Message, e.StackTrace));
                }
            });
            return task;
        });
    }

    private static void GetBuildLocation(BackendUnityModel model)
    {
        var path = EditorUserBuildSettings.GetBuildLocation(EditorUserBuildSettings.selectedStandaloneTarget);
        if (PluginSettings.SystemInfoRiderPlugin.operatingSystemFamily == OperatingSystemFamilyRider.MacOSX)
            path = Path.Combine(Path.Combine(Path.Combine(path, "Contents"), "MacOS"), PlayerSettings.productName);
        if (!string.IsNullOrEmpty(path) && File.Exists(path))
            model.UnityProjectSettings.BuildLocation.Value = path;
    }

    private static void AdviseGenerateUISchema(BackendUnityModel model)
    {
      model.GenerateUIElementsSchema.Set(_ => UIElementsSupport.GenerateSchema());
    }

    private static void AdviseExitUnity(BackendUnityModel model)
    {
      model.ExitUnity.Set((_, rdVoid) =>
      {
        var task = new RdTask<bool>();
        MainThreadDispatcher.Instance.Queue(() =>
        {
          try
          {
            ourLogger.Verbose("ExitUnity: Started");
            EditorApplication.Exit(0);
            ourLogger.Verbose("ExitUnity: Completed");
            task.Set(true);
          }
          catch (Exception e)
          {
            ourLogger.Log(LoggingLevel.WARN, "EditorApplication.Exit failed.", e);
            task.Set(false);
          }
        });
        return task;
      });
    }

    private static void AdviseShowPreferences(BackendUnityModel model, Lifetime connectionLifetime, ILog log)
    {
      model.ShowPreferences.Advise(connectionLifetime, result =>
      {
        if (result != null)
        {
          MainThreadDispatcher.Instance.Queue(() =>
          {
            try
            {
              var tab = UnityUtils.UnityVersion >= new Version(2018, 2) ? "_General" : "Rider";

              var type = typeof(SceneView).Assembly.GetType("UnityEditor.SettingsService");
              if (type != null)
              {
                // 2018+
                var method = type.GetMethod("OpenUserPreferences", BindingFlags.Static | BindingFlags.Public);

                if (method == null)
                {
                  log.Error("'OpenUserPreferences' was not found");
                }
                else
                {
                  method.Invoke(null, new object[] {$"Preferences/{tab}"});
                }
              }
              else
              {
                // 5.5, 2017 ...
                type = typeof(SceneView).Assembly.GetType("UnityEditor.PreferencesWindow");
                var method = type?.GetMethod("ShowPreferencesWindow", BindingFlags.Static | BindingFlags.NonPublic);

                if (method == null)
                {
                  log.Error("'ShowPreferencesWindow' was not found");
                }
                else
                {
                  method.Invoke(null, null);
                }
              }
            }
            catch (Exception ex)
            {
              log.Error("Show preferences " + ex);
            }
          });
        }
      });
    }

    private static void AdviseEditorState(BackendUnityModel modelValue)
    {
      modelValue.GetUnityEditorState.Set(rdVoid =>
      {
        if (EditorApplication.isPaused)
        {
          return UnityEditorState.Pause;
        }

        if (EditorApplication.isPlaying)
        {
          return UnityEditorState.Play;
        }

        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
          return UnityEditorState.Refresh;
        }

        return UnityEditorState.Idle;
      });
    }

    private static void AdviseRefresh(BackendUnityModel model)
    {
      model.Refresh.Set((l, force) =>
      {
        var refreshTask = new RdTask<Unit>();
        void SendResult()
        {
          if (!EditorApplication.isCompiling)
          {
            // ReSharper disable once DelegateSubtraction
            EditorApplication.update -= SendResult;
            ourLogger.Verbose("Refresh: SyncSolution Completed");
            refreshTask.Set(Unit.Instance);
          }
        }

        ourLogger.Verbose("Refresh: SyncSolution Enqueue");
        MainThreadDispatcher.Instance.Queue(() =>
        {
          if (!EditorApplication.isPlaying && EditorPrefsWrapper.AutoRefresh || force != RefreshType.Normal)
          {
            try
            {
              if (force == RefreshType.ForceRequestScriptReload)
              {
                ourLogger.Verbose("Refresh: RequestScriptReload");
                UnityEditorInternal.InternalEditorUtility.RequestScriptReload();
              }

              ourLogger.Verbose("Refresh: SyncSolution Started");
              RiderPackageInterop.SyncSolution();
            }
            catch (Exception e)
            {
              ourLogger.Error(e, "Refresh failed with exception");
            }
            finally
            {
              EditorApplication.update += SendResult;
            }
          }
          else
          {
            if (EditorApplication.isPlaying)
            {
              refreshTask.Set(Unit.Instance);
              ourLogger.Verbose("Avoid calling Refresh, when EditorApplication.isPlaying.");
            }
            else if (!EditorPrefsWrapper.AutoRefresh)
            {
              refreshTask.Set(Unit.Instance);
              ourLogger.Verbose("AutoRefresh is disabled by Unity preferences.");
            }
            else
            {
              refreshTask.Set(Unit.Instance);
              ourLogger.Verbose("Avoid calling Refresh, for the unknown reason.");
            }
          }
        });
        return refreshTask;
      });
    }

    private static void AdviseUnityActions(BackendUnityModel model, Lifetime connectionLifetime)
    {
      var syncPlayState = new Action(() =>
      {
        MainThreadDispatcher.Instance.Queue(() =>
        {
          var isPlaying = EditorApplication.isPlayingOrWillChangePlaymode && EditorApplication.isPlaying;

          if (!model.PlayControls.Play.HasValue() || model.PlayControls.Play.HasValue() && model.PlayControls.Play.Value != isPlaying)
          {
            ourLogger.Verbose("Reporting play mode change to model: {0}", isPlaying);
            model.PlayControls.Play.SetValue(isPlaying);
          }

          var isPaused = EditorApplication.isPaused;
          if (!model.PlayControls.Pause.HasValue() || model.PlayControls.Pause.HasValue() && model.PlayControls.Pause.Value != isPaused)
          {
            ourLogger.Verbose("Reporting pause mode change to model: {0}", isPaused);
            model.PlayControls.Pause.SetValue(isPaused);
          }
        });
      });

      syncPlayState();

      model.PlayControls.Play.Advise(connectionLifetime, play =>
      {
        MainThreadDispatcher.Instance.Queue(() =>
        {
          var current = EditorApplication.isPlayingOrWillChangePlaymode && EditorApplication.isPlaying;
          if (current != play)
          {
            ourLogger.Verbose("Request to change play mode from model: {0}", play);
            EditorApplication.isPlaying = play;
          }
        });
      });

      model.PlayControls.Pause.Advise(connectionLifetime, pause =>
      {
        MainThreadDispatcher.Instance.Queue(() =>
        {
          ourLogger.Verbose("Request to change pause mode from model: {0}", pause);
          EditorApplication.isPaused = pause;
        });
      });

      model.PlayControls.Step.Advise(connectionLifetime, x =>
      {
        MainThreadDispatcher.Instance.Queue(EditorApplication.Step);
      });

      var onPlaymodeStateChanged = new EditorApplication.CallbackFunction(() => syncPlayState());

// left for compatibility with Unity <= 5.5
#pragma warning disable 618
      connectionLifetime.AddBracket(() => { EditorApplication.playmodeStateChanged += onPlaymodeStateChanged; },
        () => { EditorApplication.playmodeStateChanged -= onPlaymodeStateChanged; });
#pragma warning restore 618
      // new api - not present in Unity 5.5
      // private static Action<PauseState> IsPauseStateChanged(UnityModel model)
      //    {
      //      return state => model?.Pause.SetValue(state == PauseState.Paused);
      //    }
    }

    private static string[] GetLogPaths()
    {
      // https://docs.unity3d.com/Manual/LogFiles.html
      //PlayerSettings.productName;
      //PlayerSettings.companyName;
      //~/Library/Logs/Unity/Editor.log
      //C:\Users\username\AppData\Local\Unity\Editor\Editor.log
      //~/.config/unity3d/Editor.log

      string editorLogpath = string.Empty;
      string playerLogPath = string.Empty;

      switch (PluginSettings.SystemInfoRiderPlugin.operatingSystemFamily)
      {
        case OperatingSystemFamilyRider.Windows:
        {
          var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
          editorLogpath = Path.Combine(localAppData, @"Unity\Editor\Editor.log");
          var userProfile = Environment.GetEnvironmentVariable("USERPROFILE");
          if (!string.IsNullOrEmpty(userProfile))
            playerLogPath = Path.Combine(
              Path.Combine(Path.Combine(Path.Combine(userProfile, @"AppData\LocalLow"), PlayerSettings.companyName),
                PlayerSettings.productName),"output_log.txt");
          break;
        }
        case OperatingSystemFamilyRider.MacOSX:
        {
          var home = Environment.GetEnvironmentVariable("HOME");
          if (!string.IsNullOrEmpty(home))
          {
            editorLogpath = Path.Combine(home, "Library/Logs/Unity/Editor.log");
            playerLogPath = Path.Combine(home, "Library/Logs/Unity/Player.log");
          }
          break;
        }
        case OperatingSystemFamilyRider.Linux:
        {
          var home = Environment.GetEnvironmentVariable("HOME");
          if (!string.IsNullOrEmpty(home))
          {
            editorLogpath = Path.Combine(home, ".config/unity3d/Editor.log");
            playerLogPath = Path.Combine(home, $".config/unity3d/{PlayerSettings.companyName}/{PlayerSettings.productName}/Player.log");
          }
          break;
        }
      }

      return new[] {editorLogpath, playerLogPath};
    }

    // DO NOT RENAME OR REFACTOR!
    // Accessed by package via reflection
    [PublicAPI, Obsolete("Use LogInitializer.LogPath")]
    internal static readonly string LogPath = LogInitializer.LogPath;

    // DO NOT RENAME OR REFACTOR!
    // Accessed by package via reflection
    [PublicAPI]
    internal static OnOpenAssetHandler OpenAssetHandler;

    // Creates and deletes Library/EditorInstance.json containing info about unity instance. Unity 2017.1+ writes this
    // file itself. We'll always overwrite, just to be sure it's up to date. The file contents are exactly the same
    private static void InitializeEditorInstanceJson(Lifetime lifetime)
    {
      if (UnityUtils.UnityVersion >= new Version(2017, 1))
        return;

      ourLogger.Verbose("Writing Library/EditorInstance.json");

      var editorInstanceJsonPath = Path.GetFullPath("Library/EditorInstance.json");

      File.WriteAllText(editorInstanceJsonPath, $@"{{
  ""process_id"": {Process.GetCurrentProcess().Id},
  ""version"": ""{UnityUtils.UnityApplicationVersion}""
}}");

      lifetime.OnTermination(() =>
      {
        ourLogger.Verbose("Deleting Library/EditorInstance.json");
        File.Delete(editorInstanceJsonPath);
      });
    }

    private static void AddRiderToRecentlyUsedScriptApp(string userAppPath)
    {
      const string recentAppsKey = "RecentlyUsedScriptApp";

      for (var i = 0; i < 10; ++i)
      {
        var path = EditorPrefs.GetString($"{recentAppsKey}{i}");
        if (File.Exists(path) && Path.GetFileName(path).ToLower().Contains("rider"))
          return;
      }

      EditorPrefs.SetString($"{recentAppsKey}{9}", userAppPath);
    }

    /// <summary>
    /// Called when Unity is about to open an asset. This method is for pre-2019.2
    /// </summary>
    [OnOpenAsset]
    static bool OnOpenedAsset(int instanceID, int line)
    {
      if (!IsRiderDefaultEditor())
        return false;

      // if (UnityUtils.UnityVersion >= new Version(2019, 2)
      //   return false;
      return OpenAssetHandler.OnOpenedAsset(instanceID, line, 0);
    }

    public static bool IsLoadedFromAssets()
    {
      var currentDir = Directory.GetCurrentDirectory();
      var location = Assembly.GetExecutingAssembly().Location;
      return location.StartsWith(currentDir, StringComparison.InvariantCultureIgnoreCase);
    }
  }

  public struct UnityModelAndLifetime
  {
    public BackendUnityModel Model;
    public Lifetime Lifetime;

    public UnityModelAndLifetime(BackendUnityModel model, Lifetime lifetime)
    {
      Model = model;
      Lifetime = lifetime;
    }
  }
}
