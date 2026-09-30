using System.Drawing;

namespace DeskMadeline
{
    /// <summary>
    /// What counts as fullscreen, for stepping behind it: desktop policy, nothing of Celeste's.
    /// </summary>
    /// <remarks>
    /// A game, a video, a presentation, a browser in F11 -- whatever the user is in that fills
    /// a monitor, taskbar and all, is something being looked at, and a sprite running across it
    /// is in the way. Rather than hide her outright, her windows go down the z-order to just
    /// behind it: on that monitor she is covered like anything else under it, and on every other
    /// monitor she carries on as before. Her world does not change, only whether it is drawn in
    /// front.
    ///
    /// Only the window the user is in counts, never a fullscreen window merely showing
    /// somewhere. The z-order is one stack for every monitor, so to go behind a video left
    /// playing on one screen she would also have to go behind whatever the user is working in on
    /// the other -- always on top given up everywhere to stay out of one screen.
    /// </remarks>
    static class FullscreenAvoidance
    {
        /// <summary>
        /// Whether a window is fullscreen: it covers a whole monitor and is not merely maximized.
        /// </summary>
        /// <remarks>
        /// A maximized window stops at the working area, short of the taskbar, so covering the
        /// monitor usually settles it -- except with an auto-hiding taskbar, where the working
        /// area is the whole monitor and every maximized window would pass. A maximized window
        /// keeps its title bar; fullscreen drops it (games are captionless popups, and browsers
        /// and players strip WS_CAPTION on the way into fullscreen), which tells the two apart.
        /// </remarks>
        public static bool IsFullscreen(Rectangle rect, bool maximizedWithCaption, Rectangle monitor)
            => !maximizedWithCaption && rect.Contains(monitor);
    }
}
