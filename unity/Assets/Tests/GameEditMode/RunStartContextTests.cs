using NUnit.Framework;

namespace TrainSurvival.Game.Tests
{
    public sealed class RunStartContextTests
    {
        [SetUp]
        public void ClearPendingTransitions()
        {
            RunStartContext.ConsumeOpeningDayTransition();
            RunStartContext.ConsumeTitleFadeInTransition();
        }

        [Test]
        public void OpeningDayRequestIsConsumedOnce()
        {
            RunStartContext.RequestOpeningDayTransition();

            Assert.IsTrue(RunStartContext.IsOpeningDayTransitionRequested);
            Assert.IsTrue(RunStartContext.ConsumeOpeningDayTransition());
            Assert.IsFalse(RunStartContext.ConsumeOpeningDayTransition());
        }

        [Test]
        public void TitleFadeInRequestIsConsumedOnce()
        {
            RunStartContext.RequestTitleFadeInTransition();

            Assert.IsTrue(RunStartContext.ConsumeTitleFadeInTransition());
            Assert.IsFalse(RunStartContext.ConsumeTitleFadeInTransition());
        }
    }
}
