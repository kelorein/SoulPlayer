using System;
using System.IO;
using SoulPlayer.UI;
using Xunit;

namespace SoulPlayer.CollectionTests
{
    [Trait("Validation", "LibraryUi")]
    public sealed class SoulPlayerWindowEscapeTests
    {
        [Fact]
        public void EscapeClosesAnOpenOwnedSoulPlayerWindow()
        {
            SoulPlayerWindowCloseController controller =
                new SoulPlayerWindowCloseController();
            int closes = 0;
            controller.MarkOpened();

            bool consumed = controller.TryCloseFromEscape(true, () => closes++);

            Assert.True(consumed);
            Assert.Equal(1, closes);
            Assert.False(controller.IsOpen);
        }

        [Fact]
        public void ClosedWindowIgnoresSoulPlayerEscapeHandler()
        {
            SoulPlayerWindowCloseController controller =
                new SoulPlayerWindowCloseController();
            int closes = 0;

            bool consumed = controller.TryCloseFromEscape(true, () => closes++);

            Assert.False(consumed);
            Assert.Equal(0, closes);
        }

        [Fact]
        public void F12ConfigurationManagerKeepsEscapePriority()
        {
            SoulPlayerWindowCloseController controller =
                new SoulPlayerWindowCloseController();
            int closes = 0;
            controller.MarkOpened();

            bool consumed = controller.TryCloseFromEscape(false, () => closes++);

            Assert.False(consumed);
            Assert.Equal(0, closes);
            Assert.True(controller.IsOpen);
        }

        [Fact]
        public void CloseButtonStillClosesTheOpenWindow()
        {
            SoulPlayerWindowCloseController controller =
                new SoulPlayerWindowCloseController();
            int closes = 0;
            controller.MarkOpened();

            Assert.True(controller.TryCloseFromButton(() => closes++));
            Assert.Equal(1, closes);
            Assert.False(controller.IsOpen);
        }

        [Fact]
        public void CloseTransitionCannotInvokeASecondCloseOrBackAction()
        {
            SoulPlayerWindowCloseController controller =
                new SoulPlayerWindowCloseController();
            int closes = 0;
            int duplicateActions = 0;
            controller.MarkOpened();

            Assert.True(controller.TryCloseFromEscape(true, () =>
            {
                closes++;
                if (controller.TryCloseFromButton(() => duplicateActions++))
                {
                    duplicateActions++;
                }
            }));

            Assert.Equal(1, closes);
            Assert.Equal(0, duplicateActions);
        }

        [Fact]
        public void LibraryRoutingRefreshDoesNotDisableEscapeHandling()
        {
            SoulPlayerWindowCloseController controller =
                new SoulPlayerWindowCloseController();
            SoulPlayerLibraryViewState viewState =
                new SoulPlayerLibraryViewState();
            controller.MarkOpened();
            viewState.SetSearchQuery("ambient");
            viewState.SetPage(2);

            // Routing-only UI refreshes clamp the page without changing window
            // ownership or the close controller lifecycle.
            viewState.ClampToResults(40, 8);
            int closes = 0;
            bool consumed = controller.TryCloseFromEscape(true, () => closes++);

            Assert.True(consumed);
            Assert.Equal(1, closes);
            Assert.Equal("ambient", viewState.SearchQuery);
            Assert.Equal(2, viewState.PageIndex);
        }

        [Fact]
        public void ReopenedWindowCanCloseFromEscapeAgain()
        {
            SoulPlayerWindowCloseController controller =
                new SoulPlayerWindowCloseController();
            int closes = 0;

            controller.MarkOpened();
            Assert.True(controller.TryCloseFromEscape(true, () => closes++));
            controller.MarkOpened();
            Assert.True(controller.TryCloseFromEscape(true, () => closes++));

            Assert.Equal(2, closes);
            Assert.False(controller.IsOpen);
        }

        [Fact]
        public void ConsumedSoulPlayerEscapeDoesNotTriggerTarkovBackAction()
        {
            SoulPlayerWindowCloseController controller =
                new SoulPlayerWindowCloseController();
            controller.MarkOpened();
            int soulPlayerCloses = 0;
            int tarkovBackActions = 0;

            bool consumed = controller.TryCloseFromEscape(
                true,
                () => soulPlayerCloses++);
            if (!consumed)
            {
                tarkovBackActions++;
            }

            Assert.True(consumed);
            Assert.Equal(1, soulPlayerCloses);
            Assert.Equal(0, tarkovBackActions);
        }

        [Fact]
        public void RuntimeConsumesEscapeOnlyAfterOwnedWindowClose()
        {
            string source = File.ReadAllText(Path.Combine(
                FindRepositoryRoot(), "UI", "SoulPlayerWindow.cs"));

            Assert.Contains("[DefaultExecutionOrder(-32000)]", source);
            Assert.Contains("HasEscapeOwnership()", source);
            Assert.Contains("IsConfigurationManagerOpen()", source);
            Assert.Contains("\"DisplayingWindow\"", source);
            Assert.Contains("Input.ResetInputAxes();", source);
            Assert.Contains("TryCloseFromButton(HideImmediately)", source);
            Assert.Contains("private void OnEnable()", source);
            Assert.Contains("_closeController.MarkOpened();", source);
            Assert.DoesNotContain("Application.isFocused", source);
            Assert.Equal(
                1,
                CountOccurrences(source, "Input.GetKeyDown(KeyCode.Escape)"));
        }

        private static int CountOccurrences(string value, string token)
        {
            int count = 0;
            int index = 0;
            while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }
            return count;
        }

        private static string FindRepositoryRoot()
        {
            DirectoryInfo current = new DirectoryInfo(AppContext.BaseDirectory);
            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "SoulPlayer.csproj")))
                {
                    return current.FullName;
                }
                current = current.Parent;
            }
            throw new DirectoryNotFoundException(
                "SoulPlayer repository root not found.");
        }
    }
}
