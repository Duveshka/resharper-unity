using System;
using System.Linq;
using JetBrains.Application.Progress;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.Feature.Services.QuickFixes;
using JetBrains.ReSharper.Plugins.Unity.AsmDef.Daemon.Errors;
using JetBrains.ReSharper.Plugins.Unity.AsmDef.Psi.Resolve;
using JetBrains.ReSharper.Plugins.Unity.Core.Feature.Services.QuickFixes;
using JetBrains.ReSharper.Plugins.Unity.JsonNew.Psi;
using JetBrains.ReSharper.Plugins.Unity.JsonNew.Psi.Tree;
using JetBrains.ReSharper.Plugins.Unity.Yaml.Psi.Caches;
using JetBrains.ReSharper.Psi.ExtensionsAPI.Tree;
using JetBrains.ReSharper.Psi.Tree;
using JetBrains.ReSharper.Resources.Shell;
using JetBrains.TextControl;

namespace JetBrains.ReSharper.Plugins.Unity.AsmDef.Feature.Services.QuickFixes
{
    [QuickFix]
    public class AsmDefConvertToGuidReferenceQuickFix : UnityScopedQuickFixBase
    {
        private readonly IJsonNewLiteralExpression myLiteralExpression;

        public AsmDefConvertToGuidReferenceQuickFix(PreferGuidReferenceWarning warning)
        {
            myLiteralExpression = warning.LiteralExpression;
        }

        protected override ITreeNode TryGetContextTreeNode() => myLiteralExpression;
        public override string Text => "Convert to GUID reference";

        protected override Action<ITextControl> ExecutePsiTransaction(ISolution solution, IProgressIndicator progress)
        {
            // We should never get null, we check the reference resolves before adding the highlight
            var reference = myLiteralExpression.FindReference<AsmDefNameReference>();
            var declaredElement = reference?.Resolve().DeclaredElement;
            if (declaredElement != null)
            {
                var metaFileCache = solution.GetComponent<MetaFileGuidCache>();

                var guid = declaredElement.GetSourceFiles().Select(sf => metaFileCache.GetAssetGuid(sf))
                    .FirstOrDefault(g => g != null);
                if (guid.HasValue)
                {
                    using (WriteLockCookie.Create())
                    {
                        var factory = JsonNewElementFactory.GetInstance(myLiteralExpression.GetPsiModule());
                        var newLiteralExpression = factory.CreateStringLiteral($"guid:{guid:N}");
                        ModificationUtil.ReplaceChild(myLiteralExpression, newLiteralExpression);
                    }
                }
            }

            return null;
        }
    }
}