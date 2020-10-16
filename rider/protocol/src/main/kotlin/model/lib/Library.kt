package model.lib

import com.jetbrains.rd.generator.nova.*
import com.jetbrains.rd.generator.nova.PredefinedType.*
import com.jetbrains.rd.generator.nova.csharp.CSharp50Generator
import com.jetbrains.rd.generator.nova.kotlin.Kotlin11Generator

object Library : Root() {

    init {
        setting(Kotlin11Generator.Namespace, "com.jetbrains.rider.model.unity")
        setting(CSharp50Generator.Namespace, "JetBrains.Rider.Model.Unity")
    }

    val UnityEditorState = enum {
        +"Disconnected"
        +"Idle"
        +"Play"
        +"Pause"
        +"Refresh"
    }

    val UnityApplicationData = structdef {
        field("applicationPath", string)
        field("applicationContentsPath", string)
        field("applicationVersion", string)
        field("editorLogPath", string.nullable).documentation = "Editor log path. Will be null when Unity protocol is not connected"
        field("playerLogPath", string.nullable).documentation = "Player log path. Will be null when Unity protocol is not connected"
        field("unityProcessId", int.nullable).documentation = "Used by the test runner and the frontend uses it in a call " +
            "to AllowSetForegroundWindow to allow Unity to bring itself to the foreground, e.g. when opening an .asmdef file." +
            "Will be null when the Unity protocol is not connected"
    }

    val LogEvent = structdef {
        field("time", long)
        field("type", enum("LogEventType") {
            +"Error"
            +"Warning"
            +"Message"
        })
        field("mode", enum("LogEventMode") {
            +"Edit"
            +"Play"
        })
        field("message", string)
        field("stackTrace", string)
    }

    val RunMethodData = structdef {
        field("assemblyName", string)
        field("typeName", string)
        field("methodName", string)
    }

    val RunMethodResult = structdef {
        field("success", bool)
        field("message", string)
        field("stackTrace", string)
    }

    val ScriptCompilationDuringPlay = enum {
        +"RecompileAndContinuePlaying"
        +"RecompileAfterFinishedPlaying"
        +"StopPlayingAndRecompile"
    }
}