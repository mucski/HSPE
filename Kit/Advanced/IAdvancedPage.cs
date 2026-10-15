using UnityEngine;

namespace HSPE.Kit.Advanced
{
    /// <summary>One page of the kit Advanced window (a module or Presets). Built once, shown / hidden by the window.</summary>
    internal interface IAdvancedPage
    {
        RectTransform Root { get; }
        void Build(RectTransform parent);
        /// <summary>The page became visible, or the target changed while it is visible.</summary>
        void Show(PoseController target);
        /// <summary>Every frame while visible.</summary>
        void Tick(PoseController target);
        void Hide();
    }
}
