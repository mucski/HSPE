using System.Collections.Generic;
using RootMotion.FinalIK;

namespace HSPE.Kit.Small
{
    /// <summary>
    /// Non-boxing comparers for Joan's enum-keyed IK tables (CharaPoseController._effectorToIndex / _chainToIndex). On
    /// HS1's Mono a Dictionary&lt;enum, ...&gt; uses the default comparer, which boxes the key on every lookup - and those
    /// tables are read every frame while an IK joint is selected or dragged.
    /// </summary>
    internal static class IkComparers
    {
        public static readonly IEqualityComparer<FullBodyBipedEffector> Effector = new EffectorComparer();
        public static readonly IEqualityComparer<FullBodyBipedChain> Chain = new ChainComparer();

        private sealed class EffectorComparer : IEqualityComparer<FullBodyBipedEffector>
        {
            public bool Equals(FullBodyBipedEffector a, FullBodyBipedEffector b) { return a == b; }
            public int GetHashCode(FullBodyBipedEffector x) { return (int)x; }
        }

        private sealed class ChainComparer : IEqualityComparer<FullBodyBipedChain>
        {
            public bool Equals(FullBodyBipedChain a, FullBodyBipedChain b) { return a == b; }
            public int GetHashCode(FullBodyBipedChain x) { return (int)x; }
        }
    }
}
