using UnityEngine;

namespace WXGameMaker.TapAdn
{
    internal sealed class TapAdnLifecycle : MonoBehaviour
    {
        internal TapAdnService Service;
        private bool paused;
        private void OnApplicationPause(bool value) { paused = value; Service?.SetForeground(!paused && Application.isFocused); }
        private void OnApplicationFocus(bool value) { Service?.SetForeground(value && !paused); }
        private void OnDestroy() { var service = Service; Service = null; service?.Dispose(); }
    }
}
