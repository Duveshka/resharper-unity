using JetBrains.ReSharper.FeaturesTestFramework.Intentions;
using JetBrains.ReSharper.Plugins.Tests.Unity.CSharp.Daemon.Stages.Analysis;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Errors;
using JetBrains.ReSharper.Plugins.Unity.CSharp.Feature.Services.QuickFixes;
using NUnit.Framework;

namespace JetBrains.ReSharper.Plugins.Tests.Unity.CSharp.Intentions.QuickFixes
{
    [TestUnity]
    public class PreferAddressByIdToGraphicsParamsQuickFixAvailabilityTests : CSharpHighlightingTestBase<PreferAddressByIdToGraphicsParamsWarning>
    {
        protected override string RelativeTestDataPath => @"CSharp\Intentions\QuickFixes\PreferAddressByIdToGraphicsParams\Availability";

        [Test] public void NameOfTest() { DoNamedTest(); }
        [Test] public void LocalConstantTest() { DoNamedTest(); }
    }

    [TestUnity]
    public class PreferAddressByIdToGraphicsParamsQuickFixTests : QuickFixTestBase<PreferAddressByIdToGraphicsParamsQuickFix>
    {
        protected override string RelativeTestDataPath => @"CSharp\Intentions\QuickFixes\PreferAddressByIdToGraphicsParams";
        protected override bool AllowHighlightingOverlap => true;

        [Test] public void SimpleTest() { DoNamedTest(); }
        [Test] public void UnderscoreNameTest() { DoNamedTest(); }
        [Test] public void NewNameTest() { DoNamedTest(); }
        [Test] public void ReuseTest() { DoNamedTest(); }
        [Test] public void ReuseFailedCreateNewTest() { DoNamedTest(); }
        [Test] public void ReuseConflictNameTest() { DoNamedTest(); }
        [Test] public void NestedClassTest() { DoNamedTest(); }
        [Test] public void WithoutUnityNamespaceTest() { DoNamedTest(); }
        [Test] public void InvalidLiteralForPropertyNameTest() { DoNamedTest(); }
        [Test] public void ShaderPropertyTest() { DoNamedTest(); }
        [Test] public void AnimatorPropertyTest() { DoNamedTest(); }
        [Test] public void ConstantValueReuseTest() { DoNamedTest(); }
        [Test] public void ConstantValueConcatReuseTest() { DoNamedTest(); }
        [Test] public void PropertyReuseTest() { DoNamedTest(); }
        [Test] public void StructTest() { DoNamedTest(); }
        [Test] public void NestedReuseTest() { DoNamedTest(); }
        [Test] public void ConstConcatCreateFieldTest() { DoNamedTest(); }
        [Test] public void PartialClassTest() { DoNamedTest(); }
        [Test] public void UniqueNameTest() { DoNamedTest(); }
    }
}