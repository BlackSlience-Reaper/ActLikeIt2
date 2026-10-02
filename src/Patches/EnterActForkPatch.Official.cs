#if STS2_0_111_0
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Runs;

namespace ActLikeIt2.Patches;
internal static partial class EnterActForkPatch
{
    private static Task FadeOut(RunManager manager) => manager.FadeOut();
}
#endif
