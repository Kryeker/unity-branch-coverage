using System;
using System.Collections.Generic;
using System.Reflection.Emit;
using Mono.Reflection;
using OpenCover.Framework.Model;

namespace Kryeker.BranchCoverage
{
    /// <summary>
    /// Builds synthetic OpenCover <see cref="BranchPoint"/>s from IL.
    /// Unity's Coverage API only exposes sequence points, so visit counts
    /// are heuristic: a path counts as visited when the sequence point at its
    /// target offset was hit. This is an approximation, not instrumentation.
    /// </summary>
    internal static class BranchSynthesizer
    {
        internal static BranchPoint[] Synthesize(System.Reflection.MethodBase method, SequencePoint[] sequencePoints, ref uint nextUspid)
        {
            var empty = new BranchPoint[0];
            if (method == null || sequencePoints == null || sequencePoints.Length == 0)
                return empty;

            IList<Instruction> instructions;
            try
            {
                instructions = method.GetInstructions();
            }
            catch (Exception)
            {
                return empty; // no IL body (extern, abstract, runtime-implemented)
            }

            if (instructions == null || instructions.Count == 0)
                return empty;

            var orderedList = new List<SequencePoint>(sequencePoints.Length);
            foreach (SequencePoint sp in sequencePoints)
            {
                if (sp != null)
                    orderedList.Add(sp);
            }
            orderedList.Sort((a, b) => a.Offset.CompareTo(b.Offset));
            SequencePoint[] ordered = orderedList.ToArray();
            if (ordered.Length == 0)
                return empty;

            var branches = new List<BranchPoint>();
            uint ordinal = 0;

            foreach (Instruction instruction in instructions)
            {
                if (instruction.OpCode.OperandType == OperandType.InlineSwitch)
                {
                    Instruction[] targets = instruction.Operand as Instruction[];
                    if (targets == null || targets.Length == 0)
                        continue;
                    SequencePoint anchor = FindAnchor(ordered, instruction.Offset);
                    for (int i = 0; i < targets.Length; i++)
                    {
                        if (targets[i] == null)
                            continue;
                        branches.Add(MakeBranch(ordered, anchor, instruction.Offset, targets[i].Offset, i, ordinal++, ref nextUspid));
                    }
                }
                else if (instruction.OpCode.FlowControl == FlowControl.Cond_Branch)
                {
                    Instruction target = instruction.Operand as Instruction;
                    if (target == null)
                        continue;
                    SequencePoint anchor = FindAnchor(ordered, instruction.Offset);
                    int fallThroughOffset = instruction.Next != null ? instruction.Next.Offset : instruction.Offset;
                    branches.Add(MakeBranch(ordered, anchor, instruction.Offset, fallThroughOffset, 0, ordinal++, ref nextUspid));
                    branches.Add(MakeBranch(ordered, anchor, instruction.Offset, target.Offset, 1, ordinal++, ref nextUspid));
                }
            }

            return branches.ToArray();
        }

        /// <summary>
        /// Fallback when IL is unavailable (assembly not loaded, e.g. batch/CI):
        /// emits one branch path per sequence point, visited iff the point was hit.
        /// Coarse, but guarantees the branch column is populated.
        /// </summary>
        internal static BranchPoint[] SynthesizeFromSequencePoints(SequencePoint[] sequencePoints, ref uint nextUspid)
        {
            var empty = new BranchPoint[0];
            if (sequencePoints == null || sequencePoints.Length == 0)
                return empty;

            var branches = new List<BranchPoint>();
            uint ordinal = 0;
            foreach (SequencePoint sp in sequencePoints)
            {
                if (sp == null)
                    continue;
                var branchPoint = new BranchPoint();
                branchPoint.StartLine = sp.StartLine;
                branchPoint.FileId = sp.FileId;
                branchPoint.Offset = sp.Offset;
                branchPoint.EndOffset = sp.Offset;
                branchPoint.Path = 0;
                branchPoint.Ordinal = ordinal++;
                branchPoint.UniqueSequencePoint = nextUspid++;
                branchPoint.VisitCount = sp.VisitCount > 0 ? 1 : 0;
                branches.Add(branchPoint);
            }

            return branches.ToArray();
        }

        private static BranchPoint MakeBranch(SequencePoint[] ordered, SequencePoint anchor, int branchOffset, int targetOffset, int path, uint ordinal, ref uint nextUspid)
        {
            var branchPoint = new BranchPoint();
            branchPoint.StartLine = anchor.StartLine;
            branchPoint.FileId = anchor.FileId;
            branchPoint.Offset = branchOffset;
            branchPoint.EndOffset = targetOffset;
            branchPoint.Path = path;
            branchPoint.Ordinal = ordinal;
            branchPoint.UniqueSequencePoint = nextUspid++;
            branchPoint.VisitCount = IsTargetVisited(ordered, targetOffset) ? 1 : 0;
            return branchPoint;
        }

        private static SequencePoint FindAnchor(SequencePoint[] ordered, int offset)
        {
            SequencePoint best = ordered[0];
            foreach (SequencePoint sp in ordered)
            {
                if (sp.Offset <= offset)
                    best = sp;
                else
                    break;
            }
            return best;
        }

        private static bool IsTargetVisited(SequencePoint[] ordered, int targetOffset)
        {
            SequencePoint best = null;
            foreach (SequencePoint sp in ordered)
            {
                if (sp.Offset <= targetOffset)
                    best = sp;
                else
                    break;
            }
            if (best == null)
                best = ordered[0];
            return best.VisitCount > 0;
        }
    }
}
