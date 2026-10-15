namespace HSPE.Kit.Transfer
{
    /// <summary>
    /// Blend-shape layout of one skinned mesh renderer. Joan's format stores blend shapes by index, so a paste onto a
    /// renderer whose shapes differ would move the wrong shapes - such renderers are skipped.
    /// </summary>
    public sealed class MeshInfo
    {
        public string Path;
        public int Count;
        /// <summary>Blend-shape names joined with '|'.</summary>
        public string Names;

        public bool SameShapes(MeshInfo other)
        {
            return other != null && other.Count == this.Count && other.Names == this.Names;
        }
    }
}
