package com.jetbrains.rider.plugins.unity.notifications

import com.intellij.openapi.project.Project
import com.jetbrains.rd.platform.util.idea.ProtocolSubscribedProjectComponent
import com.jetbrains.rd.util.reactive.adviseOnce
import com.jetbrains.rider.plugins.unity.model.frontendBackend.frontendBackendModel
import com.jetbrains.rider.projectView.solution

class UpdateMonoNotification(project: Project) : ProtocolSubscribedProjectComponent(project) {

    init {
        project.solution.frontendBackendModel.showInstallMonoDialog.adviseOnce(projectComponentLifetime) {
            val dialog = com.jetbrains.rider.environmentSetup.EnvironmentSetupDialog(project, "mono")
            dialog.showAndGet()
        }
    }
}