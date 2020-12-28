using System.Collections.Generic;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Daemon;
using JetBrains.ReSharper.Feature.Services.Bulbs;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.CallGraph;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.ContextSystem;
using JetBrains.ReSharper.Psi.CSharp.Tree;

namespace JetBrains.ReSharper.Plugins.Unity.CSharp.Feature.Services.CallGraph.BurstCodeAnalysis.ShowBurstCalls
{
    [SolutionComponent]
    public class ShowBurstCallsCodeInsightProvider : BurstCodeInsightProvider
    {
        private readonly SolutionAnalysisConfiguration myConfiguration;

        public ShowBurstCallsCodeInsightProvider(ISolution solution, SolutionAnalysisConfiguration configuration)
            : base(solution)
        {
            myConfiguration = configuration;
        }

        protected override IEnumerable<IBulbAction> GetActions(IMethodDeclaration methodDeclaration)
        {
            var actions = ShowBurstCallsBulbAction.GetAllCalls(methodDeclaration);

            return actions;
        }

        protected override bool CheckCallGraph(IMethodDeclaration methodDeclaration, IReadOnlyCallGraphContext context)
        {
            if (!UnityCallGraphUtil.IsCallGraphReady(myConfiguration))
                return false;
            
            return base.CheckCallGraph(methodDeclaration, context);
        }
    }
}