using System;

namespace SoulPlayer.UI
{
    /// <summary>
    /// Owns the one-shot close transition independently from Unity input so the
    /// window and its tests share the same duplicate-call protection.
    /// </summary>
    internal sealed class SoulPlayerWindowCloseController
    {
        private bool _isOpen;
        private bool _closeInProgress;

        internal bool IsOpen
        {
            get { return _isOpen; }
        }

        internal void MarkOpened()
        {
            _isOpen = true;
        }

        internal void MarkClosed()
        {
            _isOpen = false;
        }

        internal bool TryCloseFromEscape(
            bool ownsWindowFocus,
            Action closeWindow)
        {
            return ownsWindowFocus && TryClose(closeWindow);
        }

        internal bool TryCloseFromButton(Action closeWindow)
        {
            return TryClose(closeWindow);
        }

        private bool TryClose(Action closeWindow)
        {
            if (!_isOpen || _closeInProgress || closeWindow == null)
            {
                return false;
            }

            _closeInProgress = true;
            _isOpen = false;
            try
            {
                closeWindow();
                return true;
            }
            finally
            {
                _closeInProgress = false;
            }
        }
    }
}
