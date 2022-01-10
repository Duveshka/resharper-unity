using JetBrains.ReSharper.Plugins.Unity.CSharp.Daemon.Stages.Highlightings;
using JetBrains.ReSharper.Plugins.Unity.Tests.CSharp.Daemon.Stages.Analysis;
using NUnit.Framework;

namespace JetBrains.ReSharper.Plugins.Unity.Tests.CSharp.Daemon.Stages.GutterMarks
{
    [TestUnity]
    public class GutterMarkTests : CSharpHighlightingTestWithProductDependentGoldBase<IUnityHighlighting>
    {
        protected override string RelativeTestDataRoot => @"CSharp\Daemon\Stages\GutterMark";

        [Test] public void Test01() { DoNamedTest(); }

        [Test, TestUnity(UnityVersion.Unity2019_4)] public void TestGenericSerialisedFields_2019_4() { DoNamedTest2(); }
        [Test, TestUnity(UnityVersion.Unity2020_1)] public void TestGenericSerialisedFields_2020_1() { DoNamedTest2(); }
    }
}