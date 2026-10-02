#if STS2_0_107_1
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

namespace ActLikeIt2.Patches;
internal static partial class EnterActForkPatch
{
    // 0.107.1 EnterAct 直接调用过渡节点；保留其 TestMode 条件。
    private static Task FadeOut(RunManager manager) => TestMode.IsOff
        ? NGame.Instance!.Transition.RoomFadeOut()
        : Task.CompletedTask;
}
#endif
