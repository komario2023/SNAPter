using System;
using System.Threading;

namespace Snapter
{
    /// <summary>Restart on request: "Start SNAPter.bat" starts the exe with --restart; when SNAPter is already running, that second start
    /// signals the running one through a named event, the running one exits like the tray Exit (unless it is busy), and the new one takes over.</summary>
    internal sealed partial class AppController
    {
        internal const string RestartEventName = "Local\\Snapter.Restart";

        private EventWaitHandle _restartEvent;
        private RegisteredWaitHandle _restartWait;

        public void ListenForRestart(string suffix)
        {
            try
            {
                _restartEvent = new EventWaitHandle(false, EventResetMode.AutoReset, RestartEventName + suffix);
                _restartWait = ThreadPool.RegisterWaitForSingleObject(_restartEvent, delegate (object state, bool timedOut)
                {
                    _app.Dispatcher.BeginInvoke(new Action(HandleRestartRequest));
                }, null, Timeout.Infinite, false);
            }
            catch (Exception ex) { Log.Write("restart listener failed: " + ex.Message); }
        }

        private void StopRestartListener()
        {
            try { if (_restartWait != null) _restartWait.Unregister(null); } catch { }
            try { if (_restartEvent != null) _restartEvent.Dispose(); } catch { }
            _restartWait = null; _restartEvent = null;
        }

        private void HandleRestartRequest()
        {
            // do not lose work: a running recording, an open editor or an open settings window block the restart
            if (IsRecording || _recBusy || _overlay != null || _settingsWindow != null)
            {
                Log.Write("restart refused: busy (recording, editor or settings are open)");
                if (_tray != null) _tray.Balloon(I18n.T("app.name"), I18n.T("msg.restart_busy"));
                return;
            }
            Log.Write("restart requested by a second start");
            Quit();
        }

        /// <summary>Used by the second start: asks the running copy to exit and waits (up to 15 s) until it is gone. True: we own the instance mutex now.</summary>
        internal static bool AskRunningToExit(Mutex mutex, string suffix)
        {
            try
            {
                using (EventWaitHandle ev = EventWaitHandle.OpenExisting(RestartEventName + suffix)) ev.Set();
            }
            catch (Exception ex) { Log.Write("restart: no running copy to signal: " + ex.Message); return false; }
            try { return mutex.WaitOne(15000); }
            catch (AbandonedMutexException) { return true; }     // the old copy ended without releasing it: the mutex is ours
        }
    }
}
