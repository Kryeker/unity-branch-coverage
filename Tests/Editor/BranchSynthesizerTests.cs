using System.Reflection;
using NUnit.Framework;
using OpenCover.Framework.Model;

namespace Kryeker.BranchCoverage.Tests
{
    [TestFixture]
    internal sealed class BranchSynthesizerTests
    {
        [Test]
        public void SynthesizeFromSequencePoints_Null_ReturnsEmpty()
        {
            uint next = 1;
            BranchPoint[] result = BranchSynthesizer.SynthesizeFromSequencePoints(null, ref next);
            Assert.IsNotNull(result);
            Assert.AreEqual(0, result.Length);
            Assert.AreEqual(1u, next);
        }

        [Test]
        public void SynthesizeFromSequencePoints_Empty_ReturnsEmpty()
        {
            uint next = 7;
            BranchPoint[] result = BranchSynthesizer.SynthesizeFromSequencePoints(new SequencePoint[0], ref next);
            Assert.AreEqual(0, result.Length);
            Assert.AreEqual(7u, next);
        }

        [Test]
        public void SynthesizeFromSequencePoints_MapsVisitedCorrectly()
        {
            uint next = 100;
            SequencePoint[] points = new SequencePoint[]
            {
                MakePoint(offset: 0, line: 10, visited: 3),
                MakePoint(offset: 7, line: 12, visited: 0),
            };

            BranchPoint[] result = BranchSynthesizer.SynthesizeFromSequencePoints(points, ref next);

            Assert.AreEqual(2, result.Length);
            Assert.AreEqual(1, result[0].VisitCount);
            Assert.AreEqual(0, result[1].VisitCount);
            Assert.AreEqual(102u, next);
        }

        [Test]
        public void SynthesizeFromSequencePoints_AssignsOrdinalAndUspidSequentially()
        {
            uint next = 5;
            SequencePoint[] points = new SequencePoint[]
            {
                MakePoint(offset: 0, line: 10, visited: 1),
                MakePoint(offset: 7, line: 12, visited: 1),
                MakePoint(offset: 16, line: 14, visited: 0),
            };

            BranchPoint[] result = BranchSynthesizer.SynthesizeFromSequencePoints(points, ref next);

            Assert.AreEqual(3, result.Length);
            for (int i = 0; i < result.Length; i++)
            {
                Assert.AreEqual((uint)i, result[i].Ordinal, "ordinal " + i);
                Assert.AreEqual((uint)(5 + i), result[i].UniqueSequencePoint, "uspid " + i);
                Assert.AreEqual(0, result[i].Path);
                Assert.AreEqual(points[i].Offset, result[i].Offset);
                Assert.AreEqual(points[i].Offset, result[i].EndOffset);
                Assert.AreEqual(points[i].StartLine, result[i].StartLine);
                Assert.AreEqual(points[i].FileId, result[i].FileId);
            }
            Assert.AreEqual(8u, next);
        }

        [Test]
        public void SynthesizeFromSequencePoints_SkipsNullEntries()
        {
            uint next = 1;
            SequencePoint[] points = new SequencePoint[] { null, MakePoint(offset: 0, line: 10, visited: 1), null };

            BranchPoint[] result = BranchSynthesizer.SynthesizeFromSequencePoints(points, ref next);

            Assert.AreEqual(1, result.Length);
            Assert.AreEqual(2u, next);
        }

        [Test]
        public void Synthesize_NullMethod_ReturnsEmpty()
        {
            uint next = 1;
            BranchPoint[] result = BranchSynthesizer.Synthesize(null, new SequencePoint[] { MakePoint(0, 10, 1) }, ref next);
            Assert.AreEqual(0, result.Length);
            Assert.AreEqual(1u, next);
        }

        [Test]
        public void Synthesize_NullPoints_ReturnsEmpty()
        {
            uint next = 1;
            MethodBase method = typeof(BranchSynthesizerTests).GetMethod("SampleBranch", BindingFlags.NonPublic | BindingFlags.Static);
            BranchPoint[] result = BranchSynthesizer.Synthesize(method, null, ref next);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void Synthesize_EmptyPoints_ReturnsEmpty()
        {
            uint next = 1;
            MethodBase method = typeof(BranchSynthesizerTests).GetMethod("SampleBranch", BindingFlags.NonPublic | BindingFlags.Static);
            BranchPoint[] result = BranchSynthesizer.Synthesize(method, new SequencePoint[0], ref next);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void Synthesize_InterfaceMethodWithoutBody_ReturnsEmpty()
        {
            uint next = 1;
            MethodBase method = typeof(System.IDisposable).GetMethod("Dispose");
            SequencePoint[] points = new SequencePoint[] { MakePoint(0, 10, 1) };
            BranchPoint[] result = BranchSynthesizer.Synthesize(method, points, ref next);
            Assert.AreEqual(0, result.Length);
        }

        [Test]
        public void Synthesize_MethodWithBranch_ReturnsPairedPaths()
        {
            uint next = 1000;
            MethodBase method = typeof(BranchSynthesizerTests).GetMethod("SampleBranch", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "SampleBranch not found");
            SequencePoint[] points = new SequencePoint[]
            {
                MakePoint(offset: 0, line: 10, visited: 1),
                MakePoint(offset: 10000, line: 20, visited: 0),
            };

            BranchPoint[] result = BranchSynthesizer.Synthesize(method, points, ref next);

            Assert.GreaterOrEqual(result.Length, 2, "expected at least one cond branch (2 paths)");
            Assert.AreEqual(0, result.Length % 2, "cond branches must come in fall-through/target pairs");
            for (int i = 0; i < result.Length; i += 2)
            {
                Assert.AreEqual(0, result[i].Path, "pair start at index " + i);
                Assert.AreEqual(1, result[i + 1].Path, "pair target at index " + (i + 1));
                Assert.AreEqual(result[i].Offset, result[i + 1].Offset, "pair shares branch offset");
            }
            for (int i = 0; i < result.Length; i++)
            {
                Assert.AreEqual((uint)i, result[i].Ordinal);
                Assert.AreEqual((uint)(1000 + i), result[i].UniqueSequencePoint);
            }
            Assert.AreEqual((uint)(1000 + result.Length), next);
        }

        private static int SampleBranch(int x)
        {
            if (x > 0)
                return 1;
            return -1;
        }

        private static SequencePoint MakePoint(int offset, int line, int visited)
        {
            var sp = new SequencePoint();
            sp.Offset = offset;
            sp.StartLine = line;
            sp.EndLine = line;
            sp.FileId = 1;
            sp.VisitCount = visited;
            return sp;
        }
    }
}
